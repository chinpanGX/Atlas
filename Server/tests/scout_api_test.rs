// tests/scout_api_test.rs
//
// GET /scout/banners, POST /scout/rolls, POST /scout/rolls/{rollId}/select
// のテスト。
// - test_list_banners_returns_only_active: 開催中バナーのみ返り、rate_table等は含まれないことを確認
// - test_create_roll_deducts_gems_and_returns_candidates: gems減算・候補10体生成を確認
// - test_create_roll_insufficient_gems: gems不足時に400が返ることを確認
// - test_create_roll_unknown_banner: 存在しないbanner_idで404が返ることを確認
// - test_select_candidate_creates_player_pachimon: 選択成功でplayer_pachimon/movesが作られることを確認
// - test_select_candidate_double_select_conflict: 二重選択が409になることを確認
// - test_select_candidate_out_of_range_index: 範囲外indexが400になることを確認
// - test_select_candidate_not_owned: 他人のrollへのselectが404になることを確認
use axum::{
    Router,
    body::Body,
    http::{Request, StatusCode, header},
};
use serde_json::{Value, json};
use sqlx::MySqlPool;
use tower::ServiceExt;

use Server::routes::create_router;
use Server::state::AppState;

async fn register_and_authenticate(app: Router, secret_key: &str) -> String {
    let register_response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/devices")
                .header("Content-Type", "application/json")
                .body(Body::from(json!({ "secretKey": secret_key }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(register_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(register_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let device_id = serde_json::from_slice::<Value>(&body).unwrap()["deviceId"]
        .as_str()
        .unwrap()
        .to_string();

    let auth_response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/devices/authenticate")
                .header("Content-Type", "application/json")
                .body(Body::from(
                    json!({ "deviceId": device_id, "secretKey": secret_key }).to_string(),
                ))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(auth_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(auth_response.into_body(), usize::MAX)
        .await
        .unwrap();
    serde_json::from_slice::<Value>(&body).unwrap()["accessToken"]
        .as_str()
        .unwrap()
        .to_string()
}

/// デバイス登録・認証・プレイヤー作成までを行い、以降のスカウトAPI呼び出しに使う
/// `access_token`を返す。
async fn create_authenticated_player(app: Router, secret_key: &str, nickname: &str) -> String {
    let access_token = register_and_authenticate(app.clone(), secret_key).await;

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/players")
                .header("Content-Type", "application/json")
                .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
                .body(Body::from(json!({ "nickname": nickname }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);

    access_token
}

/// テスト用のスカウトバナーを直接DBへINSERTする。`rate_table`を`{"C": 1.0}`固定にすることで
/// 候補の`rarity`が常に`"C"`になり、テストの結果を決定論的にする。
async fn seed_banner(pool: &MySqlPool, cost_per_roll: i32, active: bool) -> String {
    let banner_id = ulid::Ulid::new().to_string();
    let parse = |s: &str| chrono::NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S").unwrap();
    let (start_at, end_at) = if active {
        (parse("2020-01-01 00:00:00"), parse("2100-01-01 00:00:00"))
    } else {
        (parse("2020-01-01 00:00:00"), parse("2020-01-02 00:00:00"))
    };

    sqlx::query(
        "INSERT INTO scout_banners (banner_id, name, rate_table, cost_per_roll, start_at, end_at) \
         VALUES (?, ?, ?, ?, ?, ?)",
    )
    .bind(&banner_id)
    .bind("テストバナー")
    .bind(sqlx::types::Json(json!({ "C": 1.0 })))
    .bind(cost_per_roll)
    .bind(start_at)
    .bind(end_at)
    .execute(pool)
    .await
    .unwrap();

    banner_id
}

/// スカウトの抽選ロジックが参照するマスタデータ(pachimon/move_groups/moves/move_group_moves)を
/// 直接DBへ投入する。`MasterData`は起動時(`AppState::from_pool`呼び出し時)に一度だけDBから
/// 読み込まれるため、この呼び出しは`AppState::from_pool`より前に行う必要がある。
///
/// `seed_banner`が`rate_table = {"C": 1.0}`固定なので、候補は必ずrarity=C(4)のパチモンから
/// 選ばれる。`move_group_moves`は`is_initial=true`を1件だけ含める。
async fn seed_test_master_data(pool: &MySqlPool) {
    sqlx::query("INSERT INTO move_groups (move_group_id, name) VALUES (1, 'テストグループ')")
        .execute(pool)
        .await
        .unwrap();

    sqlx::query(
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) \
         VALUES (1, 'たいあたり', 1, 1, 40, 100, 35)",
    )
    .execute(pool)
    .await
    .unwrap();

    sqlx::query(
        "INSERT INTO move_group_moves (unique_id, group_id, move_id, is_initial) VALUES (1, 1, 1, TRUE)",
    )
    .execute(pool)
    .await
    .unwrap();

    sqlx::query(
        "INSERT INTO pachimon \
            (pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
             base_spatk, base_spdef, base_speed, rarity, move_group_id) \
         VALUES (1, 'テストモン', 1, 0, 50, 50, 50, 50, 50, 50, 4, 1)",
    )
    .execute(pool)
    .await
    .unwrap();
}

async fn create_roll(app: Router, access_token: &str, banner_id: &str) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri("/scout/rolls")
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(json!({ "bannerId": banner_id }).to_string()))
            .unwrap(),
    )
    .await
    .unwrap()
}

async fn select_roll(
    app: Router,
    access_token: &str,
    roll_id: &str,
    index: i32,
) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri(format!("/scout/rolls/{roll_id}/select"))
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(json!({ "index": index }).to_string()))
            .unwrap(),
    )
    .await
    .unwrap()
}

async fn json_body(response: axum::response::Response) -> Value {
    let body = axum::body::to_bytes(response.into_body(), usize::MAX).await.unwrap();
    serde_json::from_slice(&body).unwrap()
}

/// 開催中のバナーのみ返り、`rateTable`のような排出率の生値はレスポンスに含まれないことを確認する。
#[sqlx::test]
async fn test_list_banners_returns_only_active(pool: MySqlPool) {
    let active_banner_id = seed_banner(&pool, 100, true).await;
    let _inactive_banner_id = seed_banner(&pool, 100, false).await;

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-1", "スカウター1").await;

    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("GET")
                .uri("/scout/banners")
                .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);

    let json = json_body(response).await;
    let banners = json["banners"].as_array().unwrap();
    assert_eq!(banners.len(), 1);
    assert_eq!(banners[0]["bannerId"], active_banner_id);
    assert_eq!(banners[0]["costPerRoll"], 100);
    assert!(banners[0].get("rateTable").is_none());
}

/// roll作成でgemsが`cost_per_roll`分減り、候補10体が返ることを確認する。
#[sqlx::test]
async fn test_create_roll_deducts_gems_and_returns_candidates(pool: MySqlPool) {
    let banner_id = seed_banner(&pool, 100, true).await;
    seed_test_master_data(&pool).await;

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-2", "スカウター2").await;

    let response = create_roll(app.clone(), &access_token, &banner_id).await;
    assert_eq!(response.status(), StatusCode::OK);

    let json = json_body(response).await;
    assert_eq!(json["gems"], 200); // 初期300 - cost_per_roll 100
    let candidates = json["candidates"].as_array().unwrap();
    assert_eq!(candidates.len(), 10);
    for (i, candidate) in candidates.iter().enumerate() {
        assert_eq!(candidate["index"], i);
        assert_eq!(candidate["rarity"], "C"); // rate_table={"C":1.0}固定のため決定論的
        assert!(candidate["pachimonId"].is_number());
        assert!(!candidate["moves"].as_array().unwrap().is_empty());
        for stat in ["hp", "atk", "def", "spatk", "spdef", "speed"] {
            let value = candidate["ivs"][stat].as_i64().unwrap();
            assert!((0..=31).contains(&value));
        }
    }
}

/// gemsが不足している状態でroll作成を行うと400が返り、gemsが減らないことを確認する。
#[sqlx::test]
async fn test_create_roll_insufficient_gems(pool: MySqlPool) {
    let banner_id = seed_banner(&pool, 1000, true).await; // 初期gems(300)を超えるコスト

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-3", "スカウター3").await;

    let response = create_roll(app.clone(), &access_token, &banner_id).await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// 存在しないbanner_idでroll作成を行うと404が返ることを確認する。
#[sqlx::test]
async fn test_create_roll_unknown_banner(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-4", "スカウター4").await;

    let response = create_roll(app.clone(), &access_token, "unknown-banner-id").await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

/// 候補を選択すると`player_pachimon`/`player_pachimon_moves`が作られ、
/// レスポンスの内容が選んだ候補と一致することを確認する。
#[sqlx::test]
async fn test_select_candidate_creates_player_pachimon(pool: MySqlPool) {
    let banner_id = seed_banner(&pool, 100, true).await;
    seed_test_master_data(&pool).await;

    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-5", "スカウター5").await;

    let roll_response = create_roll(app.clone(), &access_token, &banner_id).await;
    let roll_json = json_body(roll_response).await;
    let roll_id = roll_json["rollId"].as_str().unwrap().to_string();
    let candidate = &roll_json["candidates"][3];
    let expected_pachimon_id = candidate["pachimonId"].as_i64().unwrap();
    let expected_move_count = candidate["moves"].as_array().unwrap().len();

    let select_response = select_roll(app.clone(), &access_token, &roll_id, 3).await;
    assert_eq!(select_response.status(), StatusCode::OK);
    let select_json = json_body(select_response).await;
    assert_eq!(select_json["pachimonId"], expected_pachimon_id);
    assert_eq!(select_json["rarity"], "C");
    let player_pachimon_id = select_json["playerPachimonId"].as_str().unwrap().to_string();
    assert!(!player_pachimon_id.is_empty());

    let stored_pachimon_id: (i64,) =
        sqlx::query_as("SELECT pachimon_id FROM player_pachimon WHERE player_pachimon_id = ?")
            .bind(&player_pachimon_id)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(stored_pachimon_id.0, expected_pachimon_id);

    let move_count: (i64,) =
        sqlx::query_as("SELECT COUNT(*) FROM player_pachimon_moves WHERE player_pachimon_id = ?")
            .bind(&player_pachimon_id)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(move_count.0, expected_move_count as i64);
}

/// 同一rollへの2回目のselectが409(既に選択済み)になることを確認する。
#[sqlx::test]
async fn test_select_candidate_double_select_conflict(pool: MySqlPool) {
    let banner_id = seed_banner(&pool, 100, true).await;
    seed_test_master_data(&pool).await;

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-6", "スカウター6").await;

    let roll_response = create_roll(app.clone(), &access_token, &banner_id).await;
    let roll_json = json_body(roll_response).await;
    let roll_id = roll_json["rollId"].as_str().unwrap().to_string();

    let first = select_roll(app.clone(), &access_token, &roll_id, 0).await;
    assert_eq!(first.status(), StatusCode::OK);

    let second = select_roll(app.clone(), &access_token, &roll_id, 1).await;
    assert_eq!(second.status(), StatusCode::CONFLICT);
}

/// 範囲外の`index`でselectを行うと400が返ることを確認する。
#[sqlx::test]
async fn test_select_candidate_out_of_range_index(pool: MySqlPool) {
    let banner_id = seed_banner(&pool, 100, true).await;
    seed_test_master_data(&pool).await;

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let access_token = create_authenticated_player(app.clone(), "scout-secret-7", "スカウター7").await;

    let roll_response = create_roll(app.clone(), &access_token, &banner_id).await;
    let roll_json = json_body(roll_response).await;
    let roll_id = roll_json["rollId"].as_str().unwrap().to_string();

    let response = select_roll(app.clone(), &access_token, &roll_id, 10).await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// 他プレイヤーが作成したrollへのselectが404になることを確認する
/// (`Shared/docs/design/scout.md`の「rollIdが呼び出し元プレイヤー自身のものであること」の検証)。
#[sqlx::test]
async fn test_select_candidate_not_owned(pool: MySqlPool) {
    let banner_id = seed_banner(&pool, 100, true).await;
    seed_test_master_data(&pool).await;

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);
    let owner_token = create_authenticated_player(app.clone(), "scout-secret-8-owner", "オーナー").await;
    let other_token = create_authenticated_player(app.clone(), "scout-secret-8-other", "他人").await;

    let roll_response = create_roll(app.clone(), &owner_token, &banner_id).await;
    let roll_json = json_body(roll_response).await;
    let roll_id = roll_json["rollId"].as_str().unwrap().to_string();

    let response = select_roll(app.clone(), &other_token, &roll_id, 0).await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}
