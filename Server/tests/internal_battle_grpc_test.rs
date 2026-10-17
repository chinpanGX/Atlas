// BattleInternalService(バトルサーバー向けの内部API)の結合テスト。起動した内部APIのサーバーに、tonic の生成クライアントで呼ぶ。
// 対戦はサービスの関数で待機列に入れて成立させる(クライアント向けのマッチングの API は matchmaking_grpc_test.rs)。
// 結果報告が届かない対戦の後始末(battle_service::abort_stale_matches)もここで確かめる。
mod common;

use serde_json::{Value, json};
use sqlx::MySqlPool;
use tonic::transport::Channel;
use tonic::{Code, Request, Status};
use ulid::Ulid;

use Server::grpc::INTERNAL_SECRET_HEADER;
use Server::proto::atlas::internal::v1::battle_internal_service_client::BattleInternalServiceClient;
use Server::proto::atlas::internal::v1::{
    BattleTurn, EffortValues, GetLoadoutsRequest, GetPartyRequest, Loadout, PartyPachimon,
    ReportResultRequest,
};
use Server::service::battle_service::{
    BATTLE_WIN_REWARD_GEMS, STALE_MATCH_TIMEOUT, abort_stale_matches,
};
use Server::service::game_log::GameLogCollector;
use Server::service::{matchmaking_service, player_service};
use common::{TestServer, game_logs, item_game_logs, prepare_db};

async fn start(pool: MySqlPool) -> TestServer {
    prepare_db(&pool).await;
    TestServer::start(pool).await
}

fn internal(server: &TestServer) -> BattleInternalServiceClient<Channel> {
    BattleInternalServiceClient::new(server.internal_channel())
}

fn internal_secret() -> String {
    std::env::var("INTERNAL_API_SECRET").unwrap()
}

/// `x-internal-secret` を付けたリクエストを作る。`None` なら付けない。
fn with_secret<T>(message: T, secret: Option<&str>) -> Request<T> {
    let mut request = Request::new(message);
    if let Some(secret) = secret {
        request
            .metadata_mut()
            .insert(INTERNAL_SECRET_HEADER, secret.parse().unwrap());
    }
    request
}

/// 正しい共有シークレットを付けたリクエストを作る。
fn authorized<T>(message: T) -> Request<T> {
    with_secret(message, Some(&internal_secret()))
}

/// プレイヤーの行だけを作り、player_id を返す(デバイス登録は gRPC 基盤が行うので要らない)。
async fn create_player(server: &TestServer, nickname: &str) -> String {
    let player_id = Ulid::generate().to_string();
    let mut tx = server.state.pool.begin().await.unwrap();
    player_service::create(
        &mut tx,
        &mut GameLogCollector::default(),
        &server.state.master,
        &player_id,
        nickname,
    )
    .await
    .unwrap_or_else(|err| panic!("failed to create player: {err:?}"));
    tx.commit().await.unwrap();
    player_id
}

async fn join_queue(server: &TestServer, player_id: &str) {
    let mut tx = server.state.pool.begin().await.unwrap();
    matchmaking_service::join(
        &mut tx,
        &server.state.matchmaking,
        &server.state.battle,
        player_id,
    )
    .await
    .unwrap_or_else(|err| panic!("failed to join queue: {err:?}"));
    tx.commit().await.unwrap();
}

/// マッチが成立していればその match_id を返す。
fn matched_id(server: &TestServer, player_id: &str) -> Option<String> {
    matchmaking_service::find_match(&server.state.matchmaking, player_id)
        .unwrap_or_else(|err| panic!("failed to find match: {err:?}"))
        .map(|info| info.match_id)
}

/// マッチ成立済みの対戦。`player_a`が先に待っていた側(`battle_matches.player1_id`)。
struct MatchedPlayers {
    match_id: String,
    player_a: String,
    player_b: String,
}

/// 2人のプレイヤーを作成してA→Bの順に待機列へ参加させ、成立した対戦を返す。
async fn create_match(server: &TestServer) -> MatchedPlayers {
    let player_a = create_player(server, "プレイヤーA").await;
    let player_b = create_player(server, "プレイヤーB").await;
    join_queue(server, &player_a).await;
    join_queue(server, &player_b).await;

    MatchedPlayers {
        match_id: matched_id(server, &player_a).expect("players should be matched"),
        player_a,
        player_b,
    }
}

async fn report(server: &TestServer, request: Request<ReportResultRequest>) -> Result<(), Status> {
    internal(server).report_result(request).await.map(|_| ())
}

/// 結果報告のリクエスト(バトルサーバーの`BattleResultReporter`が送るものと同じ形)。
fn result_request(
    match_id: &str,
    winner_id: &str,
    player1: (&str, &[&str]),
    player2: (&str, &[&str]),
    turns: Vec<BattleTurn>,
) -> ReportResultRequest {
    let ids = |ids: &[&str]| ids.iter().map(|id| (*id).to_string()).collect();
    ReportResultRequest {
        match_id: match_id.to_string(),
        winner_id: winner_id.to_string(),
        player1_id: player1.0.to_string(),
        player2_id: player2.0.to_string(),
        player1_selected_pachimon: ids(player1.1),
        player2_selected_pachimon: ids(player2.1),
        turns,
    }
}

/// バトルサーバーの`BattleTurnRecord`と同じ形のターン1件分。
fn turn(turn_number: i32, player_id: &str, damage: i32) -> BattleTurn {
    BattleTurn {
        turn_number,
        player_id: player_id.to_string(),
        action_data_json: json!({ "type": "Move", "moveId": "19", "partySlot": null }).to_string(),
        result_data_json: json!({
            "hit": true, "critical": false, "effectiveness": "Normal",
            "damageDealt": damage, "targetRemainingHp": 115,
            "targetFainted": false, "newActiveIndex": null
        })
        .to_string(),
    }
}

async fn gems_of(pool: &MySqlPool, player_id: &str) -> i32 {
    sqlx::query_scalar("SELECT quantity FROM player_items WHERE player_id = ? AND item_id = 1")
        .bind(player_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

async fn match_status(pool: &MySqlPool, match_id: &str) -> String {
    sqlx::query_scalar("SELECT status FROM battle_matches WHERE match_id = ?")
        .bind(match_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

async fn turn_count(pool: &MySqlPool, match_id: &str) -> i64 {
    sqlx::query_scalar("SELECT COUNT(*) FROM battle_turns WHERE match_id = ?")
        .bind(match_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

/// 終了済み`battle_matches`の`(status, winner_id, player1_selected_pachimon, player2_selected_pachimon)`。
async fn fetch_finished_match(pool: &MySqlPool, match_id: &str) -> (String, String, Value, Value) {
    let (status, winner_id, selected1, selected2): (
        String,
        String,
        sqlx::types::Json<Value>,
        sqlx::types::Json<Value>,
    ) = sqlx::query_as(
        "SELECT status, winner_id, player1_selected_pachimon, player2_selected_pachimon \
         FROM battle_matches WHERE match_id = ? AND ended_at IS NOT NULL",
    )
    .bind(match_id)
    .fetch_one(pool)
    .await
    .unwrap();

    (status, winner_id, selected1.0, selected2.0)
}

/// `battle_matches`の`(status, winner_id)`。`winner_id`はNULLの場合`None`。
async fn match_status_and_winner(pool: &MySqlPool, match_id: &str) -> (String, Option<String>) {
    sqlx::query_as("SELECT status, winner_id FROM battle_matches WHERE match_id = ?")
        .bind(match_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

/// `started_at`を`hours`時間前にずらし、結果報告が届かないまま時間が経った対戦を再現する。
async fn age_match(pool: &MySqlPool, match_id: &str, hours: i32) {
    sqlx::query(
        "UPDATE battle_matches SET started_at = NOW(3) - INTERVAL ? HOUR WHERE match_id = ?",
    )
    .bind(hours)
    .bind(match_id)
    .execute(pool)
    .await
    .unwrap();
}

async fn owned_pachimon_ids(pool: &MySqlPool, player_id: &str) -> Vec<String> {
    sqlx::query_scalar("SELECT player_pachimon_id FROM player_pachimon WHERE player_id = ?")
        .bind(player_id)
        .fetch_all(pool)
        .await
        .unwrap()
}

/// ペア成立時にbattle_matchesへin_progressの行が作られ、先に待っていた側がplayer1になる。
#[sqlx::test(migrations = false)]
async fn match_creates_battle_match_row(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;

    let (player1_id, player2_id, status): (String, String, String) = sqlx::query_as(
        "SELECT player1_id, player2_id, status FROM battle_matches WHERE match_id = ? \
         AND started_at IS NOT NULL AND ended_at IS NULL",
    )
    .bind(&m.match_id)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(player1_id, m.player_a);
    assert_eq!(player2_id, m.player_b);
    assert_eq!(status, "in_progress");
}

/// battle_matchesのfinished更新・battle_turnsの保存・勝者へのジェムの付与と、マッチングの接続情報が両者とも消える。
#[sqlx::test(migrations = false)]
async fn report_result_records_battle(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    let turns = vec![
        turn(1, &m.player_a, 25),
        turn(1, &m.player_b, 30),
        turn(2, &m.player_a, 40),
    ];
    let request = result_request(
        &m.match_id,
        &m.player_a,
        (&m.player_a, &["a1", "a2", "a3"]),
        (&m.player_b, &["b1", "b2", "b3"]),
        turns.clone(),
    );
    report(&server, authorized(request)).await.unwrap();

    let (status, winner_id, selected1, selected2) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(status, "finished");
    assert_eq!(winner_id, m.player_a);
    assert_eq!(selected1, json!(["a1", "a2", "a3"]));
    assert_eq!(selected2, json!(["b1", "b2", "b3"]));

    assert_eq!(matched_id(&server, &m.player_a), None);
    assert_eq!(matched_id(&server, &m.player_b), None);

    // ターン番号→与ダメージの順に並べ、送った順と突き合わせる
    let saved: Vec<(
        i32,
        String,
        sqlx::types::Json<Value>,
        sqlx::types::Json<Value>,
    )> = sqlx::query_as(
        "SELECT turn_number, player_id, action_data, result_data FROM battle_turns \
             WHERE match_id = ? ORDER BY turn_number, result_data->'$.damageDealt'",
    )
    .bind(&m.match_id)
    .fetch_all(&pool)
    .await
    .unwrap();
    assert_eq!(saved.len(), turns.len());
    for (row, expected) in saved.iter().zip(&turns) {
        assert_eq!(row.0, expected.turn_number);
        assert_eq!(row.1, expected.player_id);
        assert_eq!(
            row.2.0,
            serde_json::from_str::<Value>(&expected.action_data_json).unwrap()
        );
        assert_eq!(
            row.3.0,
            serde_json::from_str::<Value>(&expected.result_data_json).unwrap()
        );
    }

    assert_eq!(
        gems_of(&pool, &m.player_a).await,
        gems_a_before + BATTLE_WIN_REWARD_GEMS
    );
    assert_eq!(gems_of(&pool, &m.player_b).await, gems_b_before);
}

/// 結果の報告で、両方の参加者の結果と、勝者の報酬がゲームログに残る。`x-request-id`があれば付く。
#[sqlx::test(migrations = false)]
async fn report_result_writes_game_logs(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let request_id = Ulid::generate().to_string();

    let mut request = authorized(result_request(
        &m.match_id,
        &m.player_b,
        (&m.player_a, &["a1"]),
        (&m.player_b, &["b1"]),
        vec![],
    ));
    request
        .metadata_mut()
        .insert("x-request-id", request_id.parse().unwrap());
    report(&server, request).await.unwrap();

    for (player_id, opponent_id, result) in [
        (&m.player_a, &m.player_b, "lose"),
        (&m.player_b, &m.player_a, "win"),
    ] {
        let logs = game_logs(&pool, player_id, "battle").await;
        assert_eq!(logs.len(), 1);
        assert_eq!(
            logs[0].method,
            "atlas.internal.v1.BattleInternalService/ReportResult"
        );
        assert_eq!(logs[0].request_id.as_deref(), Some(request_id.as_str()));
        assert_eq!(
            logs[0].body,
            json!({
                "target_id": m.match_id,
                "opponent_id": opponent_id,
                "result": result,
                "reason": "battle_result",
            })
        );
    }

    // このテストのプレイヤーはサービスの関数で作っていて初期の付与は残っていないので、報酬の1件だけ
    let rewards = item_game_logs(&pool, &m.player_b).await;
    assert_eq!(rewards.len(), 1);
    assert_eq!(rewards[0].delta, i64::from(BATTLE_WIN_REWARD_GEMS));
    assert_eq!(rewards[0].reason, "battle_reward");
    assert!(item_game_logs(&pool, &m.player_a).await.is_empty());

    // 報告済みで断った2回目の報告は、何も残さない
    let again = result_request(
        &m.match_id,
        &m.player_b,
        (&m.player_a, &["a1"]),
        (&m.player_b, &["b1"]),
        vec![],
    );
    report(&server, authorized(again)).await.unwrap_err();
    assert_eq!(game_logs(&pool, &m.player_b, "battle").await.len(), 1);
    assert_eq!(item_game_logs(&pool, &m.player_b).await.len(), 1);
}

/// バトルサーバー側のplayer1/player2がマッチ成立時と逆でも、選出がIDで突き合わされて正しい列に保存される。
#[sqlx::test(migrations = false)]
async fn report_result_matches_players_by_id(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;

    // バトルサーバーには後から待機列に入ったBが先に接続した(player1 = B)
    let request = result_request(
        &m.match_id,
        &m.player_b,
        (&m.player_b, &["b1", "b2", "b3"]),
        (&m.player_a, &["a1", "a2", "a3"]),
        vec![turn(1, &m.player_b, 25)],
    );
    report(&server, authorized(request)).await.unwrap();

    // battle_matches側はマッチ成立時の順番(player1 = A)のまま、選出がIDで突き合わされる
    let (_, winner_id, selected1, selected2) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(winner_id, m.player_b);
    assert_eq!(selected1, json!(["a1", "a2", "a3"]));
    assert_eq!(selected2, json!(["b1", "b2", "b3"]));
}

/// 相手不参加(選出が空・player2_idが空文字・ターンが無い)を受け付ける。
#[sqlx::test(migrations = false)]
async fn report_result_accepts_opponent_never_joined(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    // Bだけが接続し、Aは一度も接続しなかった
    let request = result_request(
        &m.match_id,
        &m.player_b,
        (&m.player_b, &[]),
        ("", &[]),
        vec![],
    );
    report(&server, authorized(request)).await.unwrap();

    let (status, winner_id, selected1, selected2) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(status, "finished");
    assert_eq!(winner_id, m.player_b);
    assert_eq!(selected1, json!([]));
    assert_eq!(selected2, json!([]));
    assert_eq!(turn_count(&pool, &m.match_id).await, 0);
    assert_eq!(
        gems_of(&pool, &m.player_b).await,
        gems_b_before + BATTLE_WIN_REWARD_GEMS
    );
}

/// 共有シークレットが違う・無いと UNAUTHENTICATED で、何も変えない。
#[sqlx::test(migrations = false)]
async fn report_result_rejects_wrong_secret(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let request = result_request(
        &m.match_id,
        &m.player_a,
        (&m.player_a, &[]),
        (&m.player_b, &[]),
        vec![],
    );

    for secret in [Some("wrong-secret"), None] {
        let status = report(&server, with_secret(request.clone(), secret))
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::Unauthenticated);
    }
    assert_eq!(match_status(&pool, &m.match_id).await, "in_progress");
}

/// 存在しない対戦は NOT_FOUND。
#[sqlx::test(migrations = false)]
async fn report_result_rejects_unknown_match(pool: MySqlPool) {
    let server = start(pool).await;
    let m = create_match(&server).await;

    let request = result_request(
        &Ulid::generate().to_string(),
        &m.player_a,
        (&m.player_a, &[]),
        (&m.player_b, &[]),
        vec![],
    );
    let status = report(&server, authorized(request)).await.unwrap_err();
    assert_eq!(status.code(), Code::NotFound);
}

/// 参加者でない勝者・ターンの行動者、JSONとして読めない行動は INVALID_ARGUMENT で、何も変えない。
#[sqlx::test(migrations = false)]
async fn report_result_rejects_invalid_request(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let outsider = create_player(&server, "プレイヤーC").await;

    let request_with = |winner_id: &str, turn_player_id: &str| {
        result_request(
            &m.match_id,
            winner_id,
            (&m.player_a, &[]),
            (&m.player_b, &[]),
            vec![turn(1, turn_player_id, 10)],
        )
    };
    let mut broken_json = request_with(&m.player_a, &m.player_a);
    broken_json.turns[0].action_data_json = "{".to_string();

    for request in [
        request_with(&outsider, &m.player_a),
        request_with(&m.player_a, &outsider),
        broken_json,
    ] {
        let status = report(&server, authorized(request)).await.unwrap_err();
        assert_eq!(status.code(), Code::InvalidArgument);
    }
    assert_eq!(match_status(&pool, &m.match_id).await, "in_progress");
}

/// 二重の報告は ALREADY_EXISTS で、ジェム・ターンを二重に記録しない。
#[sqlx::test(migrations = false)]
async fn report_result_twice_is_already_exists(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let request = result_request(
        &m.match_id,
        &m.player_a,
        (&m.player_a, &["a1"]),
        (&m.player_b, &["b1"]),
        vec![turn(1, &m.player_a, 25)],
    );

    report(&server, authorized(request.clone())).await.unwrap();
    let status = report(&server, authorized(request)).await.unwrap_err();
    assert_eq!(status.code(), Code::AlreadyExists);

    assert_eq!(
        gems_of(&pool, &m.player_a).await,
        gems_a_before + BATTLE_WIN_REWARD_GEMS
    );
    assert_eq!(turn_count(&pool, &m.match_id).await, 1);
}

/// 勝者なし(winner_idが空文字)の報告でabortedになり、ジェムを付与しない。
#[sqlx::test(migrations = false)]
async fn report_result_without_winner_aborts(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    // 両者放置で勝者なし
    let request = result_request(
        &m.match_id,
        "",
        (&m.player_a, &["a1"]),
        (&m.player_b, &["b1"]),
        vec![turn(1, &m.player_a, 0), turn(1, &m.player_b, 0)],
    );
    report(&server, authorized(request)).await.unwrap();

    assert_eq!(
        match_status_and_winner(&pool, &m.match_id).await,
        ("aborted".to_string(), None)
    );
    assert_eq!(turn_count(&pool, &m.match_id).await, 2);
    assert_eq!(gems_of(&pool, &m.player_a).await, gems_a_before);
    assert_eq!(gems_of(&pool, &m.player_b).await, gems_b_before);
    for player_id in [&m.player_a, &m.player_b] {
        let logs = game_logs(&pool, player_id, "battle").await;
        assert_eq!(logs[0].body["result"], "no_winner");
    }
}

/// 勝者なしで報告済みの対戦には、勝者ありでも勝者なしでも再報告できない。
#[sqlx::test(migrations = false)]
async fn report_result_after_no_winner_is_already_exists(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let request_with = |winner_id: &str| {
        result_request(
            &m.match_id,
            winner_id,
            (&m.player_a, &[]),
            (&m.player_b, &[]),
            vec![],
        )
    };

    report(&server, authorized(request_with(""))).await.unwrap();

    for winner_id in ["", m.player_a.as_str()] {
        let status = report(&server, authorized(request_with(winner_id)))
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::AlreadyExists);
    }
    assert_eq!(gems_of(&pool, &m.player_a).await, gems_a_before);
}

/// 一定時間を過ぎたin_progressだけがabortedになる。
#[sqlx::test(migrations = false)]
async fn abort_stale_matches_aborts_only_old_matches(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;

    // 同じ2人で、成立したばかりの対戦をもう1つ用意する
    let fresh_match_id = Ulid::generate().to_string();
    sqlx::query(
        "INSERT INTO battle_matches (match_id, player1_id, player2_id, status, started_at) \
         VALUES (?, ?, ?, 'in_progress', NOW(3))",
    )
    .bind(&fresh_match_id)
    .bind(&m.player_a)
    .bind(&m.player_b)
    .execute(&pool)
    .await
    .unwrap();

    age_match(&pool, &m.match_id, 2).await;

    let aborted = abort_stale_matches(&pool, STALE_MATCH_TIMEOUT)
        .await
        .ok()
        .unwrap();
    assert_eq!(aborted, 1);
    assert_eq!(
        match_status_and_winner(&pool, &m.match_id).await,
        ("aborted".to_string(), None)
    );
    assert_eq!(match_status(&pool, &fresh_match_id).await, "in_progress");

    // 打ち切り済みの対戦は再度対象にならない
    assert_eq!(
        abort_stale_matches(&pool, STALE_MATCH_TIMEOUT)
            .await
            .ok()
            .unwrap(),
        0
    );
}

/// 打ち切り後に届いた結果報告は受け付け、finishedへ上書きしてジェムを付与する。
#[sqlx::test(migrations = false)]
async fn report_result_after_stale_abort_overwrites(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let m = create_match(&server).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    age_match(&pool, &m.match_id, 2).await;
    abort_stale_matches(&pool, STALE_MATCH_TIMEOUT)
        .await
        .ok()
        .unwrap();
    assert_eq!(match_status(&pool, &m.match_id).await, "aborted");

    // 1時間を超える対戦の結果が、打ち切り後に届いた
    let request = result_request(
        &m.match_id,
        &m.player_b,
        (&m.player_a, &["a1"]),
        (&m.player_b, &["b1"]),
        vec![turn(1, &m.player_b, 25)],
    );
    report(&server, authorized(request)).await.unwrap();

    let (status, winner_id, _, _) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(status, "finished");
    assert_eq!(winner_id, m.player_b);
    assert_eq!(
        gems_of(&pool, &m.player_b).await,
        gems_b_before + BATTLE_WIN_REWARD_GEMS
    );
}

/// 指定した順番で、パチモンID・努力値・技(枠の順)が返る。
#[sqlx::test(migrations = false)]
async fn get_loadouts_returns_in_requested_order(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let player_id = create_player(&server, "選出太郎").await;
    let starter_id = owned_pachimon_ids(&pool, &player_id).await.remove(0);

    // 2体目を直接追加し、努力値・技の並びがそのまま返ることを確かめる。
    let second_id = "01J0000000000000000000LOAD".to_string();
    sqlx::query(
        "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, effort_values) \
         VALUES (?, ?, 1, ?)",
    )
    .bind(&second_id)
    .bind(&player_id)
    .bind(json!({"hp": 4, "atk": 8, "def": 0, "spatk": 0, "spdef": 0, "speed": 52}))
    .execute(&pool)
    .await
    .unwrap();
    sqlx::query(
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) \
         VALUES (2, 'でんこうせっか', 1, 1, 40, 100, 30)",
    )
    .execute(&pool)
    .await
    .unwrap();
    for (move_row_id, slot, move_id) in [
        ("01J0000000000000000000MOV2", 2, 1),
        ("01J0000000000000000000MOV1", 1, 2),
    ] {
        sqlx::query(
            "INSERT INTO player_pachimon_moves (player_pachimon_move_id, player_pachimon_id, slot, move_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(move_row_id)
        .bind(&second_id)
        .bind(slot)
        .bind(move_id)
        .execute(&pool)
        .await
        .unwrap();
    }

    let loadouts = internal(&server)
        .get_loadouts(authorized(GetLoadoutsRequest {
            player_id: player_id.clone(),
            player_pachimon_ids: vec![second_id.clone(), starter_id.clone()],
        }))
        .await
        .unwrap()
        .into_inner()
        .loadouts;

    let zero = EffortValues {
        hp: 0,
        atk: 0,
        def: 0,
        spatk: 0,
        spdef: 0,
        speed: 0,
    };
    assert_eq!(
        loadouts,
        vec![
            Loadout {
                player_pachimon_id: second_id,
                pachimon_id: 1,
                effort_values: Some(EffortValues {
                    hp: 4,
                    atk: 8,
                    speed: 52,
                    ..zero
                }),
                move_ids: vec![2, 1],
            },
            Loadout {
                player_pachimon_id: starter_id,
                pachimon_id: 1,
                effort_values: Some(zero),
                move_ids: vec![1],
            },
        ]
    );
}

/// 他プレイヤーの個体・存在しないIDを含むと NOT_FOUND。
#[sqlx::test(migrations = false)]
async fn get_loadouts_rejects_pachimon_not_owned(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let player_a = create_player(&server, "A").await;
    let player_b = create_player(&server, "B").await;
    let own_id = owned_pachimon_ids(&pool, &player_a).await.remove(0);
    let other_id = owned_pachimon_ids(&pool, &player_b).await.remove(0);

    for ids in [
        vec![own_id.clone(), other_id],
        vec![own_id.clone(), "01J0000000000000000000NONE".to_string()],
    ] {
        let status = internal(&server)
            .get_loadouts(authorized(GetLoadoutsRequest {
                player_id: player_a.clone(),
                player_pachimon_ids: ids,
            }))
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::NotFound);
    }
}

/// パーティの枠の順に、個体IDとパチモンIDが返る(パーティ外の所持個体は含まない)。
#[sqlx::test(migrations = false)]
async fn get_party_returns_party_in_slot_order(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let player_id = create_player(&server, "選出太郎").await;
    let starter_id = owned_pachimon_ids(&pool, &player_id).await.remove(0);

    // スターターを枠2へ移して枠1に2体目を置き、パーティ外の3体目も持たせる
    for id in ["01J0000000000000000000PTY2", "01J0000000000000000000OUT3"] {
        sqlx::query(
            "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, effort_values) \
             VALUES (?, ?, 1, ?)",
        )
        .bind(id)
        .bind(&player_id)
        .bind(json!({"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0}))
        .execute(&pool)
        .await
        .unwrap();
    }
    sqlx::query("UPDATE player_party_slots SET slot = 2 WHERE player_id = ?")
        .bind(&player_id)
        .execute(&pool)
        .await
        .unwrap();
    sqlx::query(
        "INSERT INTO player_party_slots (party_slot_id, player_id, slot, player_pachimon_id) \
         VALUES ('01J0000000000000000000SLT1', ?, 1, '01J0000000000000000000PTY2')",
    )
    .bind(&player_id)
    .execute(&pool)
    .await
    .unwrap();

    let party = internal(&server)
        .get_party(authorized(GetPartyRequest { player_id }))
        .await
        .unwrap()
        .into_inner()
        .pachimon;

    assert_eq!(
        party,
        vec![
            PartyPachimon {
                player_pachimon_id: "01J0000000000000000000PTY2".to_string(),
                pachimon_id: 1,
            },
            PartyPachimon {
                player_pachimon_id: starter_id,
                pachimon_id: 1,
            },
        ]
    );
}

/// 共有シークレットが違う・無いと、パーティ・選出個体の取得も UNAUTHENTICATED。
#[sqlx::test(migrations = false)]
async fn get_party_and_loadouts_reject_wrong_secret(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let player_id = create_player(&server, "選出太郎").await;
    let ids = owned_pachimon_ids(&pool, &player_id).await;

    for secret in [Some("wrong-secret"), None] {
        let status = internal(&server)
            .get_party(with_secret(
                GetPartyRequest {
                    player_id: player_id.clone(),
                },
                secret,
            ))
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::Unauthenticated);

        let status = internal(&server)
            .get_loadouts(with_secret(
                GetLoadoutsRequest {
                    player_id: player_id.clone(),
                    player_pachimon_ids: ids.clone(),
                },
                secret,
            ))
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::Unauthenticated);
    }
}
