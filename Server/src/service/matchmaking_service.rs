use std::collections::{HashMap, HashSet, VecDeque};
use std::sync::Mutex;

use chrono::Utc;
use jsonwebtoken::{EncodingKey, Header};
use serde::{Deserialize, Serialize};
use sqlx::MySqlPool;
use ulid::Ulid;

use crate::error::AppError;
use crate::service::battle_service;

/// `battle_token`の有効期限(秒)。マッチ成立直後にBattleServerへ接続するだけなので短くてよい。
pub const BATTLE_TOKEN_TTL_SECONDS: i64 = 30;

/// `BATTLE_SERVER_URL`未指定時に返すBattleServerのURL(ローカル開発用)。
const DEFAULT_BATTLE_SERVER_URL: &str = "http://127.0.0.1:5000";

/// マッチング(BattleServer連携)に関する設定値。起動時に環境変数から読み込む。
pub struct BattleConfig {
    /// マッチ成立時にクライアントへ返すBattleServerのURL
    pub battle_server_url: String,
    /// `battle_token`(JWT, HS256)の署名に使う共有シークレット。BattleServer側にも同じ値を配布する
    pub battle_token_secret: String,
    /// 内部API(`/internal/battle/result`)の`X-Internal-Secret`ヘッダーと照合するサービス間シークレット
    pub internal_api_secret: String,
}

impl BattleConfig {
    /// 環境変数(`.env`含む)から設定を読み込む。
    ///
    /// # Panics
    /// `BATTLE_TOKEN_SECRET`または`INTERNAL_API_SECRET`が未設定の場合。
    pub fn from_env() -> Self {
        dotenvy::dotenv().ok();

        let battle_token_secret =
            std::env::var("BATTLE_TOKEN_SECRET").expect("BATTLE_TOKEN_SECRET must be set in .env");
        let internal_api_secret =
            std::env::var("INTERNAL_API_SECRET").expect("INTERNAL_API_SECRET must be set in .env");
        let battle_server_url = std::env::var("BATTLE_SERVER_URL")
            .unwrap_or_else(|_| DEFAULT_BATTLE_SERVER_URL.to_string());

        BattleConfig {
            battle_server_url,
            battle_token_secret,
            internal_api_secret,
        }
    }
}

/// マッチ成立結果。`GET /battle/queue/status`で取得されるまで`MatchmakingQueue::matched`に保持する。
#[derive(Clone)]
pub struct MatchInfo {
    pub match_id: String,
    pub battle_server: String,
    pub battle_token: String,
}

/// マッチング待機列。DB永続化せず、プロセスメモリのみで管理する(サーバー1台構成が前提)。
#[derive(Default)]
pub struct MatchmakingQueue {
    /// マッチ待ちの`player_id`(先頭ほど古い)
    pub waiting: VecDeque<String>,
    /// `player_id` -> 未取得のマッチ成立結果
    pub matched: HashMap<String, MatchInfo>,
    /// ペアは決まったが`battle_matches`への書き込み中(ロック外)の`player_id`。
    /// この間に同じプレイヤーが再参加して二重にマッチしないよう、`waiting`/`matched`と同様に扱う
    pub pairing: HashSet<String>,
}

/// `battle_token`のclaims。`exp`は有効期限(UNIX秒)。
#[derive(Serialize, Deserialize)]
pub struct BattleTokenClaims {
    pub match_id: String,
    pub player_id: String,
    pub exp: i64,
}

/// 待機列に参加する。他に待機者がいれば先頭の1人と即座にペアを成立させ、`battle_matches`に
/// `in_progress`の行を作成する(先に待っていた側が`player1`)。
///
/// DB書き込み中は待機列のロックを保持しない。ペアが決まった2人は書き込みが終わるまで
/// `pairing`に入れておき、書き込みに失敗した場合は相手を待機列の先頭へ戻す
/// (DBに行が無い対戦をクライアントへ返さないため)。
/// 既に待機中・ペアリング中・マッチ成立済み(結果未取得)の場合は何もしない(二重参加防止)。
///
/// # Errors
/// 待機列のロック取得・`battle_matches`への書き込み・`battle_token`の発行に失敗した場合に
/// `AppError::InternalError`を返す。
pub async fn join(
    pool: &MySqlPool,
    queue: &Mutex<MatchmakingQueue>,
    config: &BattleConfig,
    player_id: &str,
) -> Result<(), AppError> {
    let opponent_id = {
        let mut queue = queue.lock().map_err(|_| AppError::InternalError)?;

        if queue.waiting.iter().any(|id| id == player_id)
            || queue.pairing.contains(player_id)
            || queue.matched.contains_key(player_id)
        {
            return Ok(());
        }

        let Some(opponent_id) = queue.waiting.pop_front() else {
            queue.waiting.push_back(player_id.to_string());
            return Ok(());
        };

        queue.pairing.insert(opponent_id.clone());
        queue.pairing.insert(player_id.to_string());
        opponent_id
    };

    let match_id = Ulid::new().to_string();
    let result = battle_service::create_match(pool, &match_id, &opponent_id, player_id)
        .await
        .and_then(|()| {
            [opponent_id.as_str(), player_id]
                .into_iter()
                .map(|id| {
                    let battle_token =
                        issue_battle_token(&config.battle_token_secret, &match_id, id)?;
                    Ok((
                        id.to_string(),
                        MatchInfo {
                            match_id: match_id.clone(),
                            battle_server: config.battle_server_url.clone(),
                            battle_token,
                        },
                    ))
                })
                .collect::<Result<Vec<_>, AppError>>()
        });

    let mut queue = queue.lock().map_err(|_| AppError::InternalError)?;
    queue.pairing.remove(&opponent_id);
    queue.pairing.remove(player_id);

    match result {
        Ok(infos) => {
            queue.matched.extend(infos);
            Ok(())
        }
        Err(e) => {
            queue.waiting.push_front(opponent_id);
            Err(e)
        }
    }
}

/// 待機列から離脱する。待機列にいなければ何もしない。
///
/// # Errors
/// 待機列のロック取得に失敗した場合に`AppError::InternalError`を返す。
pub fn leave(queue: &Mutex<MatchmakingQueue>, player_id: &str) -> Result<(), AppError> {
    let mut queue = queue.lock().map_err(|_| AppError::InternalError)?;
    queue.waiting.retain(|id| id != player_id);

    Ok(())
}

/// マッチ成立結果を取り出す。取得したエントリは`matched`から削除する。
/// 未成立(待機中、または待機列にいない)なら`None`を返す。
///
/// # Errors
/// 待機列のロック取得に失敗した場合に`AppError::InternalError`を返す。
pub fn take_match(
    queue: &Mutex<MatchmakingQueue>,
    player_id: &str,
) -> Result<Option<MatchInfo>, AppError> {
    let mut queue = queue.lock().map_err(|_| AppError::InternalError)?;

    Ok(queue.matched.remove(player_id))
}

/// `battle_token`(JWT, HS256)を発行する。有効期限は発行から`BATTLE_TOKEN_TTL_SECONDS`秒。
///
/// # Errors
/// JWTのエンコードに失敗した場合に`AppError::InternalError`を返す。
fn issue_battle_token(secret: &str, match_id: &str, player_id: &str) -> Result<String, AppError> {
    let claims = BattleTokenClaims {
        match_id: match_id.to_string(),
        player_id: player_id.to_string(),
        exp: Utc::now().timestamp() + BATTLE_TOKEN_TTL_SECONDS,
    };

    jsonwebtoken::encode(
        &Header::default(),
        &claims,
        &EncodingKey::from_secret(secret.as_bytes()),
    )
    .map_err(|_| AppError::InternalError)
}
