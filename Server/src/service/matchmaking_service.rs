use std::collections::{HashMap, HashSet, VecDeque};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use chrono::Utc;
use jsonwebtoken::{EncodingKey, Header};
use serde::{Deserialize, Serialize};
use sqlx::{MySql, MySqlPool, Transaction};
use ulid::Ulid;
use voltech_grpc_error::Internal;

use crate::error::AppError;
use crate::master::MasterData;
use crate::service::{battle_service, bot_player_service};

/// `battle_token`の有効期限(秒)。マッチ成立直後にBattleServerへ接続するだけなので短くてよい。
pub const BATTLE_TOKEN_TTL_SECONDS: i64 = 30;

/// 待機列に入ってから、ボットとの対戦にするまでの時間。
pub const BOT_MATCH_WAIT: Duration = Duration::from_secs(10);

/// 待ち時間を過ぎたプレイヤーを探してボットと組む間隔。
const BOT_MATCH_INTERVAL: Duration = Duration::from_secs(1);

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

/// マッチ成立結果(バトルサーバーへの接続情報)。
#[derive(Clone)]
pub struct MatchInfo {
    pub match_id: String,
    pub battle_server: String,
    pub battle_token: String,
}

/// 待機列で待っているプレイヤー。
pub struct WaitingPlayer {
    pub player_id: String,
    /// 待機列に入った時刻。`BOT_MATCH_WAIT`を過ぎたらボットと組む
    pub joined_at: Instant,
}

/// マッチング待機列。DB永続化せず、プロセスメモリのみで管理する(サーバー1台構成が前提)。
#[derive(Default)]
pub struct MatchmakingQueue {
    /// マッチ待ちのプレイヤー(先頭ほど古い)
    pub waiting: VecDeque<WaitingPlayer>,
    /// `player_id` -> マッチ成立結果。状況の確認で返しても消さず、次の参加・離脱か、対戦の結果の報告で消す
    /// (応答が届かずに確認し直しても、接続情報を失わないため)
    pub matched: HashMap<String, MatchInfo>,
    /// ペアは決まったが`battle_matches`への書き込み中(ロック外)の`player_id`。
    /// この間に同じプレイヤーが再参加して二重にマッチしないよう、`waiting`と同様に扱う
    pub pairing: HashSet<String>,
}

/// `battle_token`のclaims。`exp`は有効期限(UNIX秒)。
#[derive(Serialize, Deserialize)]
pub struct BattleTokenClaims {
    pub match_id: String,
    pub player_id: String,
    /// 相手がボットならその`player_id`、人との対戦なら空文字。BattleServerはこれを見て、
    /// ボットの側を自分で動かす(ボットは接続してこないため)
    #[serde(default)]
    pub bot_player_id: String,
    pub exp: i64,
}

/// 待機列に参加する。他に待機者がいれば先頭の1人と即座にペアを成立させ、`battle_matches`に
/// `in_progress`の行を作成する(先に待っていた側が`player1`)。前のマッチの成立結果が残っていれば捨てる。
/// 誰も来ないまま`BOT_MATCH_WAIT`が過ぎたら、`match_with_bots`がボットと組む。
///
/// DB書き込み中は待機列のロックを保持しない。ペアが決まった2人は書き込みが終わるまで
/// `pairing`に入れておき、書き込みに失敗した場合は相手を待機列の先頭へ戻す
/// (DBに行が無い対戦をクライアントへ返さないため)。
/// 既に待機中・ペアリング中の場合は何もしない(二重参加防止)。
/// 呼び出し元のトランザクション内で`battle_matches`に書く。
///
/// # Errors
/// 待機列のロック取得・`battle_matches`への書き込み・`battle_token`の発行に失敗した場合に
/// `AppError::InternalError`を返す。
pub async fn join(
    tx: &mut Transaction<'_, MySql>,
    queue: &Mutex<MatchmakingQueue>,
    config: &BattleConfig,
    player_id: &str,
) -> Result<(), AppError> {
    let opponent = {
        let mut queue = lock(queue)?;

        if queue.waiting.iter().any(|w| w.player_id == player_id)
            || queue.pairing.contains(player_id)
        {
            return Ok(());
        }
        queue.matched.remove(player_id);

        let Some(opponent) = queue.waiting.pop_front() else {
            queue.waiting.push_back(WaitingPlayer {
                player_id: player_id.to_string(),
                joined_at: Instant::now(),
            });
            return Ok(());
        };

        queue.pairing.insert(opponent.player_id.clone());
        queue.pairing.insert(player_id.to_string());
        opponent
    };
    let opponent_id = opponent.player_id.as_str();

    let match_id = Ulid::generate().to_string();
    let result = battle_service::create_match(&mut **tx, &match_id, opponent_id, player_id)
        .await
        .and_then(|()| {
            [opponent_id, player_id]
                .into_iter()
                .map(|id| Ok((id.to_string(), match_info(config, &match_id, id, "")?)))
                .collect::<Result<Vec<_>, AppError>>()
        });

    let mut queue = lock(queue)?;
    queue.pairing.remove(opponent_id);
    queue.pairing.remove(player_id);

    match result {
        Ok(infos) => {
            queue.matched.extend(infos);
            tracing::info!(
                match_id,
                player1_id = opponent_id,
                player2_id = player_id,
                "battle match formed"
            );
            Ok(())
        }
        Err(e) => {
            queue.waiting.push_front(opponent);
            Err(e)
        }
    }
}

/// 待機列に入ってから`wait`以上経ったプレイヤーを、それぞれボットと組む。
///
/// `battle_matches`にはプレイヤーを`player1`、ボットを`player2`として作り、`battle_token`は
/// プレイヤーの分だけ発行する(claimsの`bot_player_id`で、BattleServerにボットとの対戦だと伝える)。
/// 組んでいる間は`join`と同じく`pairing`に入れておき、失敗したら待機列の先頭へ戻す
/// (次の確認で組み直す)。1人分の失敗はログに残し、残りのプレイヤーを続ける。
///
/// # Errors
/// 待機列のロック取得に失敗した場合に`AppError::InternalError`を返す。
pub async fn match_with_bots(
    pool: &MySqlPool,
    master: &MasterData,
    queue: &Mutex<MatchmakingQueue>,
    config: &BattleConfig,
    wait: Duration,
) -> Result<(), AppError> {
    let expired: Vec<WaitingPlayer> = {
        let mut queue = lock(queue)?;
        let (expired, waiting): (Vec<_>, Vec<_>) = std::mem::take(&mut queue.waiting)
            .into_iter()
            .partition(|w| w.joined_at.elapsed() >= wait);
        queue.waiting = waiting.into();
        for w in &expired {
            queue.pairing.insert(w.player_id.clone());
        }
        expired
    };

    // 失敗した分を先頭へ戻すとき、元の順番(古い順)を保つため、新しいほうから処理する
    for waiting in expired.into_iter().rev() {
        let match_id = Ulid::generate().to_string();
        let result = async {
            let bot_id = bot_player_service::pick(pool, master).await?;
            battle_service::create_match(pool, &match_id, &waiting.player_id, &bot_id).await?;
            let info = match_info(config, &match_id, &waiting.player_id, &bot_id)?;
            Ok::<_, AppError>((bot_id, info))
        }
        .await;

        let mut queue = lock(queue)?;
        queue.pairing.remove(&waiting.player_id);
        match result {
            Ok((bot_id, info)) => {
                tracing::info!(
                    match_id,
                    player_id = waiting.player_id,
                    bot_player_id = bot_id,
                    "battle match with bot formed"
                );
                queue.matched.insert(waiting.player_id, info);
            }
            Err(err) => {
                tracing::error!(
                    "exception.message" = %Internal::from(err),
                    player_id = waiting.player_id,
                    "failed to form battle match with bot"
                );
                queue.waiting.push_front(waiting);
            }
        }
    }

    Ok(())
}

/// `match_with_bots`を`BOT_MATCH_INTERVAL`ごとに実行するバックグラウンドタスクを起動する。
/// APIサーバー本体(`main.rs`)からのみ呼ぶ(テストでは起動せず、`match_with_bots`を直接呼ぶ)。
pub fn spawn_bot_matching(
    pool: MySqlPool,
    master: Arc<MasterData>,
    queue: Arc<Mutex<MatchmakingQueue>>,
    config: Arc<BattleConfig>,
) {
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(BOT_MATCH_INTERVAL);
        loop {
            interval.tick().await;
            if let Err(err) = match_with_bots(&pool, &master, &queue, &config, BOT_MATCH_WAIT).await
            {
                tracing::error!(
                    "exception.message" = %Internal::from(err),
                    "failed to match waiting players with bots"
                );
            }
        }
    });
}

/// 待機列から離脱し、マッチ成立結果も捨てる。待機列にいなければ何もしない。
///
/// # Errors
/// 待機列のロック取得に失敗した場合に`AppError::InternalError`を返す。
pub fn leave(queue: &Mutex<MatchmakingQueue>, player_id: &str) -> Result<(), AppError> {
    let mut queue = lock(queue)?;
    queue.waiting.retain(|w| w.player_id != player_id);
    queue.matched.remove(player_id);

    Ok(())
}

/// マッチ成立結果を返す(消さない)。未成立(待機中、または待機列にいない)なら`None`を返す。
///
/// # Errors
/// 待機列のロック取得に失敗した場合に`AppError::InternalError`を返す。
pub fn find_match(
    queue: &Mutex<MatchmakingQueue>,
    player_id: &str,
) -> Result<Option<MatchInfo>, AppError> {
    let queue = lock(queue)?;

    Ok(queue.matched.get(player_id).cloned())
}

/// 対戦の結果が報告されたマッチの成立結果を、両方の参加者の分まとめて捨てる。
///
/// # Errors
/// 待機列のロック取得に失敗した場合に`AppError::InternalError`を返す。
pub fn clear_match(queue: &Mutex<MatchmakingQueue>, match_id: &str) -> Result<(), AppError> {
    let mut queue = lock(queue)?;
    queue.matched.retain(|_, info| info.match_id != match_id);

    Ok(())
}

/// `player_id`に返すマッチ成立結果を作る。`bot_player_id`は相手がボットならその`player_id`、
/// 人との対戦なら空文字。
///
/// # Errors
/// `battle_token`の発行に失敗した場合に`AppError::InternalError`を返す。
fn match_info(
    config: &BattleConfig,
    match_id: &str,
    player_id: &str,
    bot_player_id: &str,
) -> Result<MatchInfo, AppError> {
    Ok(MatchInfo {
        match_id: match_id.to_string(),
        battle_server: config.battle_server_url.clone(),
        battle_token: issue_battle_token(
            &config.battle_token_secret,
            match_id,
            player_id,
            bot_player_id,
        )?,
    })
}

/// `battle_token`(JWT, HS256)を発行する。有効期限は発行から`BATTLE_TOKEN_TTL_SECONDS`秒。
///
/// # Errors
/// JWTのエンコードに失敗した場合に`AppError::InternalError`を返す。
fn issue_battle_token(
    secret: &str,
    match_id: &str,
    player_id: &str,
    bot_player_id: &str,
) -> Result<String, AppError> {
    let claims = BattleTokenClaims {
        match_id: match_id.to_string(),
        player_id: player_id.to_string(),
        bot_player_id: bot_player_id.to_string(),
        exp: Utc::now().timestamp() + BATTLE_TOKEN_TTL_SECONDS,
    };

    let token = jsonwebtoken::encode(
        &Header::default(),
        &claims,
        &EncodingKey::from_secret(secret.as_bytes()),
    )?;
    Ok(token)
}

/// 待機列をロックする。ロックを持ったままpanicしたスレッドがあれば内部エラーにする。
fn lock(queue: &Mutex<MatchmakingQueue>) -> Result<MutexGuard<'_, MatchmakingQueue>, AppError> {
    queue
        .lock()
        .map_err(|_| AppError::internal("matchmaking queue lock is poisoned"))
}
