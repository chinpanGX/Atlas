//! クライアント向けの gRPC サーバー。認証・冪等性・メンテナンス等の共通処理は gRPC 基盤(VolTech gRPC)の Layer が行う。

pub mod context;
mod debug;
mod internal_battle;
mod matchmaking;
mod party;
mod player;
mod player_data;
mod player_lookup;
mod scout;

use std::future::Future;

use tokio::net::TcpListener;
use tokio_stream::wrappers::TcpListenerStream;
use tonic::transport::Server;
use tower::Layer;
use voltech_grpc_auth::{AuthLayer, DeviceServiceImpl};
use voltech_grpc_log::RpcLogLayer;
use voltech_grpc_status::{DateChangeLayer, ServerStatus, ServerStatusLayer};
use voltech_grpc_transaction::{GameLogSink, IdempotencyLevels, RpcMethodLayer, Transactions};

pub use debug::DebugServiceImpl;
pub use internal_battle::{BattleInternalServiceImpl, INTERNAL_SECRET_HEADER};
pub use matchmaking::MatchmakingServiceImpl;
pub use party::PartyServiceImpl;
pub use player::PlayerServiceImpl;
pub use player_lookup::PlayerLookup;
pub use scout::ScoutServiceImpl;

use crate::proto;
use crate::state::AppState;

pub type BoxError = Box<dyn std::error::Error + Send + Sync>;

/// 開発中だけ有効にする機能。
#[derive(Clone, Copy, Default)]
pub struct DevFeatures {
    /// gRPC リフレクション(grpcurl や Postman から .proto 無しで呼べる)
    pub reflection: bool,
    /// 開発用の API(`DebugService`)。無効なら登録せず、呼ぶと UNIMPLEMENTED になる
    pub debug_api: bool,
}

/// `listener` で受け付けた接続で gRPC サーバーを動かす。`shutdown` が完了すると止まる。
pub async fn serve(
    listener: TcpListener,
    state: AppState,
    status: ServerStatus,
    rpc_log: RpcLogLayer,
    game_log_sink: GameLogSink,
    dev: DevFeatures,
    shutdown: impl Future<Output = ()>,
) -> Result<(), BoxError> {
    let reflection = if dev.reflection {
        Some(
            tonic_reflection::server::Builder::configure()
                .register_encoded_file_descriptor_set(voltech_grpc_auth::FILE_DESCRIPTOR_SET)
                .register_encoded_file_descriptor_set(proto::FILE_DESCRIPTOR_SET)
                .build_v1()?,
        )
    } else {
        None
    };

    // 冪等性の対象かどうかは、.proto の idempotency_level から決める
    let transactions = Transactions::new(
        state.pool.clone(),
        IdempotencyLevels::from_file_descriptor_set(proto::FILE_DESCRIPTOR_SET)?,
        game_log_sink,
    );
    let auth = AuthLayer::new(state.pool.clone(), PlayerLookup::new(state.pool.clone()));
    let date_change = DateChangeLayer::new(status.clone());
    let debug =
        dev.debug_api.then(|| {
            auth.layer(date_change.layer(
                DebugServiceImpl::new(transactions.clone(), state.master.clone()).into_server(),
            ))
        });

    Server::builder()
        // メンテナンスや認証で返したエラーもアクセスログに出すように、一番外側に付ける
        .layer(rpc_log)
        // 強制アップデートとメンテナンスは、認証より先に全サービスで判定する
        .layer(ServerStatusLayer::new(status))
        // Transactions::run が RPC の名前を読めるように、全サービスの外側に付ける
        .layer(RpcMethodLayer)
        .add_service(DeviceServiceImpl::new(state.pool.clone()).into_server())
        // 認証が必要なサービスにだけ AuthLayer を付ける。日付変更は認証の後に判定するので、内側に DateChangeLayer を付ける
        .add_service(auth.layer(date_change.layer(
            PlayerServiceImpl::new(transactions.clone(), state.master.clone()).into_server(),
        )))
        .add_service(auth.layer(date_change.layer(
            PartyServiceImpl::new(transactions.clone(), state.master.clone()).into_server(),
        )))
        .add_service(auth.layer(date_change.layer(
            ScoutServiceImpl::new(transactions.clone(), state.master.clone()).into_server(),
        )))
        .add_service(
            auth.layer(
                date_change.layer(
                    MatchmakingServiceImpl::new(
                        transactions,
                        state.matchmaking.clone(),
                        state.battle.clone(),
                    )
                    .into_server(),
                ),
            ),
        )
        .add_optional_service(debug)
        .add_optional_service(reflection)
        .serve_with_incoming_shutdown(TcpListenerStream::new(listener), shutdown)
        .await?;
    Ok(())
}

/// `listener` で、バトルサーバー向けの内部API(`BattleInternalService`)を動かす。`shutdown` が完了すると止まる。
///
/// クライアント向けとはポートを分け、基盤の Layer のうちログ(`RpcLogLayer`)だけを付ける(認証・冪等性・メンテナンス・日付変更は付けない)。
/// メンテナンス中でもバトルサーバーからの結果の報告を受けるため。認証は共有シークレットで行う。
pub async fn serve_internal(
    listener: TcpListener,
    state: AppState,
    rpc_log: RpcLogLayer,
    game_log_sink: GameLogSink,
    shutdown: impl Future<Output = ()>,
) -> Result<(), BoxError> {
    let service = BattleInternalServiceImpl::new(
        state.pool.clone(),
        state.matchmaking.clone(),
        game_log_sink,
    )
    .into_server(state.battle.internal_api_secret.clone());

    Server::builder()
        .layer(rpc_log)
        .add_service(service)
        .serve_with_incoming_shutdown(TcpListenerStream::new(listener), shutdown)
        .await?;
    Ok(())
}
