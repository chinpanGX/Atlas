// ScoutService(バナー一覧・ロール・候補の選択)の結合テスト。起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::scout_service_client::ScoutServiceClient;
use Server::proto::atlas::v1::{
    ListBannersRequest, RollRequest, RollResponse, SelectRequest, SelectResponse,
};
use common::{
    TestServer, authorized, authorized_with_request_id, error_reason, prepare_db, with_request_id,
};
use sqlx::MySqlPool;
use tonic::Code;
use tonic::transport::Channel;

/// 開催中のバナー(1回100ジェム。サインアップで付与される300ジェムで3回ロールできる)。
const OPEN_BANNER: &str = "01J0000000000000000000OPEN";
/// 開催中で、1回のコストがサインアップで付与されるジェムより多いバナー。
const EXPENSIVE_BANNER: &str = "01J000000000000000000EXPNS";
/// 開催が終わったバナー。
const CLOSED_BANNER: &str = "01J00000000000000000CLOSED";
/// 開催前のバナー。
const UPCOMING_BANNER: &str = "01J0000000000000000UPCOMNG";

const COST_PER_ROLL: i32 = 100;
const STARTER_GEMS: i32 = 300;

/// バナーを足す。排出はレアリティ C だけにし、`prepare_db` のパチモン(pachimon_id=1、C)だけが出るようにする。
async fn add_banners(pool: &MySqlPool) {
    let banners = [
        (OPEN_BANNER, "開催中", COST_PER_ROLL, -1, 1),
        (EXPENSIVE_BANNER, "高い", STARTER_GEMS + 1, -2, 1),
        (CLOSED_BANNER, "終了", COST_PER_ROLL, -2, -1),
        (UPCOMING_BANNER, "開催前", COST_PER_ROLL, 1, 2),
    ];
    for (banner_id, name, cost, start_days, end_days) in banners {
        sqlx::query(
            "INSERT INTO scout_banners (banner_id, name, rate_table, cost_per_roll, start_at, end_at) \
             VALUES (?, ?, '{\"C\": 1.0}', ?, \
                     UTC_TIMESTAMP(3) + INTERVAL ? DAY, UTC_TIMESTAMP(3) + INTERVAL ? DAY)",
        )
        .bind(banner_id)
        .bind(name)
        .bind(cost)
        .bind(start_days)
        .bind(end_days)
        .execute(pool)
        .await
        .unwrap();
    }
}

async fn start(pool: MySqlPool) -> TestServer {
    prepare_db(&pool).await;
    add_banners(&pool).await;
    TestServer::start(pool).await
}

fn client(server: &TestServer) -> ScoutServiceClient<Channel> {
    ScoutServiceClient::new(server.channel())
}

async fn roll(
    server: &TestServer,
    token: &str,
    banner_id: &str,
) -> Result<RollResponse, tonic::Status> {
    client(server)
        .roll(authorized_with_request_id(
            RollRequest {
                banner_id: banner_id.to_string(),
            },
            token,
        ))
        .await
        .map(tonic::Response::into_inner)
}

async fn select(
    server: &TestServer,
    token: &str,
    roll_id: &str,
    index: i32,
) -> Result<SelectResponse, tonic::Status> {
    client(server)
        .select(authorized_with_request_id(
            SelectRequest {
                roll_id: roll_id.to_string(),
                index,
            },
            token,
        ))
        .await
        .map(tonic::Response::into_inner)
}

/// サインインの全件から、所持しているジェムの数を取る。
async fn gems(server: &TestServer, token: &str) -> i32 {
    let snapshot = server.sign_in(token).await.unwrap().snapshot.unwrap();
    snapshot
        .items
        .iter()
        .find(|item| item.item_id == 1)
        .map_or(0, |item| item.quantity)
}

async fn owned_pachimon_count(server: &TestServer, token: &str) -> usize {
    let snapshot = server.sign_in(token).await.unwrap().snapshot.unwrap();
    snapshot.pachimon.len()
}

/// 開催中のバナーだけが、開始日時の早い順に返る。
#[sqlx::test(migrations = false)]
async fn list_banners_returns_open_banners(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;

    let banners = client(&server)
        .list_banners(authorized(ListBannersRequest {}, &token))
        .await
        .unwrap()
        .into_inner()
        .banners;

    let ids: Vec<_> = banners
        .iter()
        .map(|banner| banner.banner_id.as_str())
        .collect();
    assert_eq!(ids, vec![EXPENSIVE_BANNER, OPEN_BANNER]);
    assert_eq!(banners[1].name, "開催中");
    assert_eq!(banners[1].cost_per_roll, COST_PER_ROLL);
    let start_at = banners[1].start_at.unwrap();
    let end_at = banners[1].end_at.unwrap();
    assert!(start_at.seconds < end_at.seconds);
}

/// プレイヤーがまだ作られていなければ、バナー一覧も PLAYER_NOT_FOUND。
#[sqlx::test(migrations = false)]
async fn list_banners_requires_player(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.register_and_issue_token().await;

    let status = client(&server)
        .list_banners(authorized(ListBannersRequest {}, &token))
        .await
        .unwrap_err();

    assert_eq!(status.code(), Code::FailedPrecondition);
    assert_eq!(error_reason(&status).as_deref(), Some("PLAYER_NOT_FOUND"));
}

/// ロールすると、ジェムを消費した後の数が items だけに入り、候補10体が index の順に返る。
#[sqlx::test(migrations = false)]
async fn roll_consumes_gems_and_returns_candidates(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;

    let response = roll(&server, &token, OPEN_BANNER).await.unwrap();

    let diff = response.player_diff.unwrap();
    assert!(diff.pachimon.is_none());
    assert!(diff.pachimon_move_map.is_none());
    assert!(diff.party_slots.is_none());
    let items = diff.items.unwrap();
    assert_eq!(items.upserted.len(), 1);
    assert_eq!(items.upserted[0].item_id, 1);
    assert_eq!(items.upserted[0].quantity, STARTER_GEMS - COST_PER_ROLL);
    assert_eq!(gems(&server, &token).await, STARTER_GEMS - COST_PER_ROLL);

    assert!(!response.roll_id.is_empty());
    let indexes: Vec<_> = response
        .candidates
        .iter()
        .map(|candidate| candidate.index)
        .collect();
    assert_eq!(indexes, (0..10).collect::<Vec<_>>());
    for candidate in &response.candidates {
        assert_eq!(candidate.pachimon_id, 1);
        assert_eq!(candidate.move_ids, vec![1]);
    }
}

/// 同じ RequestID で送り直すと、ジェムを二重に消費せず、同じロールが返る。
#[sqlx::test(migrations = false)]
async fn roll_resent_with_same_request_id_runs_once(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let request_id = ulid::Ulid::generate().to_string();

    let mut roll_ids = Vec::new();
    for _ in 0..2 {
        let response = client(&server)
            .roll(with_request_id(
                RollRequest {
                    banner_id: OPEN_BANNER.to_string(),
                },
                &token,
                &request_id,
            ))
            .await
            .unwrap()
            .into_inner();
        roll_ids.push(response.roll_id);
    }

    assert_eq!(roll_ids[0], roll_ids[1]);
    assert_eq!(gems(&server, &token).await, STARTER_GEMS - COST_PER_ROLL);
}

/// バナーが無い・開催期間外・ジェムが足りないときは、それぞれの業務エラーで、ジェムを消費しない。
#[sqlx::test(migrations = false)]
async fn roll_rejects_unavailable_banners(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;

    let cases = [
        ("01J000000000000000000NONE0", "SCOUT_BANNER_NOT_FOUND"),
        (CLOSED_BANNER, "SCOUT_BANNER_CLOSED"),
        (UPCOMING_BANNER, "SCOUT_BANNER_CLOSED"),
        (EXPENSIVE_BANNER, "INSUFFICIENT_GEMS"),
    ];
    for (banner_id, reason) in cases {
        let status = roll(&server, &token, banner_id).await.unwrap_err();
        assert_eq!(status.code(), Code::FailedPrecondition);
        assert_eq!(
            error_reason(&status).as_deref(),
            Some(reason),
            "{banner_id}"
        );
    }
    assert_eq!(gems(&server, &token).await, STARTER_GEMS);
}

/// 候補を選ぶと、その個体と技が pachimon・pachimon_move_map に入り、所持に加わる。
#[sqlx::test(migrations = false)]
async fn select_grants_candidate(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let rolled = roll(&server, &token, OPEN_BANNER).await.unwrap();

    let diff = select(&server, &token, &rolled.roll_id, 3)
        .await
        .unwrap()
        .player_diff
        .unwrap();

    assert!(diff.items.is_none());
    assert!(diff.party_slots.is_none());
    let pachimon = diff.pachimon.unwrap();
    assert_eq!(pachimon.upserted.len(), 1);
    assert_eq!(
        pachimon.upserted[0].pachimon_id,
        rolled.candidates[3].pachimon_id
    );
    let moves = diff.pachimon_move_map.unwrap();
    let move_ids: Vec<_> = moves.upserted.iter().map(|m| m.move_id).collect();
    assert_eq!(move_ids, rolled.candidates[3].move_ids);
    assert!(
        moves
            .upserted
            .iter()
            .all(|m| m.player_pachimon_id == pachimon.upserted[0].player_pachimon_id)
    );

    let snapshot = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert!(
        snapshot
            .pachimon
            .iter()
            .any(|p| p.player_pachimon_id == pachimon.upserted[0].player_pachimon_id)
    );
}

/// 同じ RequestID で送り直すと、二重に入手せず、同じ個体が返る。
#[sqlx::test(migrations = false)]
async fn select_resent_with_same_request_id_runs_once(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let rolled = roll(&server, &token, OPEN_BANNER).await.unwrap();
    let before = owned_pachimon_count(&server, &token).await;
    let request_id = ulid::Ulid::generate().to_string();

    let mut ids = Vec::new();
    for _ in 0..2 {
        let response = client(&server)
            .select(with_request_id(
                SelectRequest {
                    roll_id: rolled.roll_id.clone(),
                    index: 0,
                },
                &token,
                &request_id,
            ))
            .await
            .unwrap()
            .into_inner();
        ids.push(
            response.player_diff.unwrap().pachimon.unwrap().upserted[0]
                .player_pachimon_id
                .clone(),
        );
    }

    assert_eq!(ids[0], ids[1]);
    assert_eq!(owned_pachimon_count(&server, &token).await, before + 1);
}

/// ロールが無い・他のプレイヤーのもの・番号が範囲外・選択済みのときは、それぞれの業務エラーで、何も入手しない。
#[sqlx::test(migrations = false)]
async fn select_rejects_invalid_requests(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let (_, other_token) = server.create_player("ほかのプレイヤー").await;
    let rolled = roll(&server, &token, OPEN_BANNER).await.unwrap();
    let others = roll(&server, &other_token, OPEN_BANNER).await.unwrap();
    let before = owned_pachimon_count(&server, &token).await;

    let cases = [
        ("01J000000000000000000NONE0", 0, "SCOUT_ROLL_NOT_FOUND"),
        (others.roll_id.as_str(), 0, "SCOUT_ROLL_NOT_FOUND"),
        (rolled.roll_id.as_str(), 10, "SCOUT_CANDIDATE_OUT_OF_RANGE"),
        (rolled.roll_id.as_str(), -1, "SCOUT_CANDIDATE_OUT_OF_RANGE"),
    ];
    for (roll_id, index, reason) in cases {
        let status = select(&server, &token, roll_id, index).await.unwrap_err();
        assert_eq!(status.code(), Code::FailedPrecondition);
        assert_eq!(
            error_reason(&status).as_deref(),
            Some(reason),
            "{roll_id} {index}"
        );
    }
    assert_eq!(owned_pachimon_count(&server, &token).await, before);

    // 別の RequestID で2回目を選ぶと、選択済み
    select(&server, &token, &rolled.roll_id, 0).await.unwrap();
    let status = select(&server, &token, &rolled.roll_id, 1)
        .await
        .unwrap_err();
    assert_eq!(
        error_reason(&status).as_deref(),
        Some("SCOUT_ROLL_ALREADY_SELECTED")
    );
    assert_eq!(owned_pachimon_count(&server, &token).await, before + 1);
}
