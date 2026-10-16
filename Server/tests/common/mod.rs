//! テストで使う DB の準備と、テスト用の gRPC サーバーの起動・API の呼び出し。

#![allow(
    dead_code,
    reason = "テストのファイルごとに使う関数が違い、使わないファイルでは dead_code になるため"
)]

use Server::db;
use Server::grpc;
use Server::proto::atlas::v1::player_service_client::PlayerServiceClient;
use Server::proto::atlas::v1::{SignInRequest, SignInResponse, SignUpRequest, SignUpResponse};
use Server::state::AppState;
use sqlx::MySqlPool;
use tokio::net::TcpListener;
use tonic::Request;
use tonic::metadata::MetadataValue;
use tonic::transport::Channel;
use tonic_types::StatusExt;
use ulid::Ulid;
use voltech_grpc_auth::proto::voltech::v1::device_service_client::DeviceServiceClient;
use voltech_grpc_auth::proto::voltech::v1::{IssueTokenRequest, RegisterRequest};
use voltech_grpc_log::RpcLogLayer;
use voltech_grpc_status::{ServerStatus, StatusConfig};
use voltech_grpc_transaction::GameLogSink;

/// テストで使うサーバーの状態(メンテナンスなし・最低バージョンなし)。
const TEST_SERVER_STATUS: &str = r#"
[maintenance]
enabled = false

[min_version]
ios = 0
android = 0
windows = 0

[game_day]
reset_hour = 4
timezone = "Asia/Tokyo"
"#;

/// テストで登録に使う secret_key(基盤の条件: 16〜128 文字の ASCII)。
pub const SECRET_KEY: &str = "0123456789abcdef0123456789abcdef";

/// `#[sqlx::test(migrations = false)]` で作った DB に、基盤と Atlas のマイグレーションを流し、
/// サインアップで初期パーティ(pachimon_id=1 を1体)が付与されるだけのマスタを入れる。
/// `AppState::from_pool`(マスタのメモリ読み込み)より前に呼ぶ。
pub async fn prepare_db(pool: &MySqlPool) {
    db::migrate(pool).await.unwrap();
    let queries = [
        "INSERT INTO items (item_id, name) VALUES (1, 'ジェム')",
        "INSERT INTO move_groups (move_group_id, name) VALUES (1, 'グループ1')",
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) \
         VALUES (1, 'たいあたり', 1, 1, 40, 100, 35)",
        "INSERT INTO move_group_moves (unique_id, group_id, move_id, is_initial) \
         VALUES (1, 1, 1, TRUE)",
        "INSERT INTO pachimon \
            (pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
             base_spatk, base_spdef, base_speed, rarity, move_group_id) \
         VALUES (1, 'テストモン', 1, 0, 50, 50, 50, 50, 50, 50, 4, 1)",
        "INSERT INTO starter_party_slots (slot_no, pachimon_id) VALUES (1, 1)",
    ];
    for query in queries {
        sqlx::query(query).execute(pool).await.unwrap();
    }
}

/// テスト用に起動した gRPC サーバー(クライアント向けと内部API)と、そこにつないだチャンネル。
pub struct TestServer {
    pub state: AppState,
    channel: Channel,
    internal_channel: Channel,
}

impl TestServer {
    /// `prepare_db` 済みの DB で、本番と同じ組み立ての gRPC サーバー(クライアント向けと内部API)を
    /// 空いているポートで起動して接続する。
    pub async fn start(pool: MySqlPool) -> Self {
        let state = AppState::from_pool(pool).await;
        let status = ServerStatus::new(StatusConfig::from_toml(TEST_SERVER_STATUS).unwrap());

        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(grpc::serve(
            listener,
            state.clone(),
            status,
            RpcLogLayer::default(),
            GameLogSink::Database,
            grpc::DevFeatures {
                reflection: false,
                debug_api: true,
            },
            std::future::pending(),
        ));

        let internal_listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let internal_addr = internal_listener.local_addr().unwrap();
        tokio::spawn(grpc::serve_internal(
            internal_listener,
            state.clone(),
            RpcLogLayer::default(),
            std::future::pending(),
        ));

        Self {
            state,
            channel: connect(addr).await,
            internal_channel: connect(internal_addr).await,
        }
    }

    pub fn channel(&self) -> Channel {
        self.channel.clone()
    }

    /// バトルサーバー向けの内部APIにつないだチャンネル。
    pub fn internal_channel(&self) -> Channel {
        self.internal_channel.clone()
    }

    pub fn player(&self) -> PlayerServiceClient<Channel> {
        PlayerServiceClient::new(self.channel.clone())
    }

    /// デバイスを登録してトークンを発行し、(デバイス登録で発行された player_id, access_token) を返す。
    pub async fn register_and_issue_token(&self) -> (String, String) {
        let mut device = DeviceServiceClient::new(self.channel.clone());
        let registered = device
            .register(RegisterRequest {
                secret_key: SECRET_KEY.to_owned(),
            })
            .await
            .unwrap()
            .into_inner();
        let token = device
            .issue_token(IssueTokenRequest {
                device_id: registered.device_id,
                secret_key: SECRET_KEY.to_owned(),
            })
            .await
            .unwrap()
            .into_inner()
            .access_token;
        (registered.player_id, token)
    }

    /// デバイス登録・トークンの発行・サインアップまで行い、(player_id, access_token) を返す。
    pub async fn create_player(&self, nickname: &str) -> (String, String) {
        let (player_id, token) = self.register_and_issue_token().await;
        self.sign_up(&token, nickname).await.unwrap();
        (player_id, token)
    }

    pub async fn sign_up(
        &self,
        token: &str,
        nickname: &str,
    ) -> Result<SignUpResponse, tonic::Status> {
        self.player()
            .sign_up(authorized(
                SignUpRequest {
                    nickname: nickname.to_owned(),
                },
                token,
            ))
            .await
            .map(tonic::Response::into_inner)
    }

    pub async fn sign_in(&self, token: &str) -> Result<SignInResponse, tonic::Status> {
        self.player()
            .sign_in(authorized(SignInRequest {}, token))
            .await
            .map(tonic::Response::into_inner)
    }
}

async fn connect(addr: std::net::SocketAddr) -> Channel {
    Channel::from_shared(format!("http://{addr}"))
        .unwrap()
        .connect()
        .await
        .unwrap()
}

/// `authorization: Bearer <token>` を付けたリクエストを作る。
pub fn authorized<T>(message: T, token: &str) -> Request<T> {
    let mut request = Request::new(message);
    request.metadata_mut().insert(
        "authorization",
        MetadataValue::try_from(format!("Bearer {token}")).unwrap(),
    );
    request
}

/// `authorization` と、新しい `x-request-id` を付けたリクエストを作る(冪等性の対象の RPC 用)。
pub fn authorized_with_request_id<T>(message: T, token: &str) -> Request<T> {
    with_request_id(message, token, &Ulid::generate().to_string())
}

/// `authorization` と、指定した `x-request-id` を付けたリクエストを作る(同じ RequestID での再送を試すとき)。
pub fn with_request_id<T>(message: T, token: &str, request_id: &str) -> Request<T> {
    let mut request = authorized(message, token);
    request
        .metadata_mut()
        .insert("x-request-id", MetadataValue::try_from(request_id).unwrap());
    request
}

/// 業務エラーの `ErrorInfo` の reason を読む。業務エラーでなければ `None`。
pub fn error_reason(status: &tonic::Status) -> Option<String> {
    status.get_details_error_info().map(|info| info.reason)
}

/// ゲームログのアイテムの増減の1件(`game_logs` の `kind = 'item'` の行)。
#[derive(Debug, PartialEq, Eq)]
pub struct ItemGameLog {
    pub method: String,
    pub item_id: String,
    pub delta: i64,
    pub after: i64,
    pub reason: String,
}

/// プレイヤーのアイテムの増減のゲームログを、書いた順に返す。
pub async fn item_game_logs(pool: &MySqlPool, player_id: &str) -> Vec<ItemGameLog> {
    let player_id = Ulid::from_string(player_id).unwrap();
    let rows: Vec<(String, sqlx::types::Json<serde_json::Value>)> = sqlx::query_as(
        "SELECT method, body FROM game_logs WHERE player_id = ? AND kind = 'item' ORDER BY log_id",
    )
    .bind(player_id.to_bytes().as_slice())
    .fetch_all(pool)
    .await
    .unwrap();
    rows.into_iter()
        .map(|(method, body)| ItemGameLog {
            method,
            item_id: body["target_id"].as_str().unwrap().to_owned(),
            delta: body["delta"].as_i64().unwrap(),
            after: body["after"].as_i64().unwrap(),
            reason: body["reason"].as_str().unwrap().to_owned(),
        })
        .collect()
}
