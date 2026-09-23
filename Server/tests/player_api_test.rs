// tests/player_api_test.rs
//
// POST /signup (プレイヤー作成) と POST /sign-in (プレイヤー情報・所持データ全件取得) のテスト。
// - test_signup_and_sign_in: signup(ボディ無し200)→sign-inでnickname/playerDiff(items含む)を確認
// - test_signup_grants_starter_party: starter_party_slotsマスタの内容がplayerDiff.pachimon/
//   pachimonMoveMapへ複製されることを確認
// - test_signup_duplicate: 同一デバイスでの2件目の作成が409になることを確認
// - test_signup_unauthenticated: 未認証での作成が401になることを確認
// - test_sign_in_before_signup: プレイヤー未作成状態でのsign-inが404になることを確認
// - test_sign_in_unauthenticated: 未認証でのsign-inが401になることを確認
//
// POST /edit/party, POST /edit/pachimon_moves (パーティ編成・技の付け替え)のテスト。
// - test_edit_party_success: パーティ編成成功、playerDiff.partySlotsの内容を確認
// - test_edit_party_replaces_previous: 2回目のPOSTで前回の編成が解除され、removedに含まれることを確認
// - test_edit_party_too_many_slots: 7体指定で400になることを確認
// - test_edit_party_duplicate_slot: 同一slotの重複指定で400になることを確認
// - test_edit_party_duplicate_player_pachimon_id: 同一player_pachimon_idの重複指定で400になることを確認
// - test_edit_party_not_owned: 他人のplayer_pachimon_idを指定すると404になることを確認
// - test_edit_pachimon_move_success: 候補技への付け替え成功を確認
// - test_edit_pachimon_move_preserves_id_on_existing_slot: 既存slotへの付け替えで
//   player_pachimon_move_id(割当自体のULID)が変わらないことを確認
// - test_edit_pachimon_move_invalid_candidate: グループ外の技を指定すると400になることを確認
// - test_edit_pachimon_move_out_of_range_slot: slotが範囲外(5)だと400になることを確認
// - test_edit_pachimon_move_not_owned: 他人のplayerPachimonIdを指定すると404になることを確認
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

async fn signup(app: Router, access_token: &str, nickname: &str) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri("/signup")
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(json!({ "nickname": nickname }).to_string()))
            .unwrap(),
    )
    .await
    .unwrap()
}

async fn sign_in(app: Router, access_token: Option<&str>) -> axum::response::Response {
    let mut builder = Request::builder().method("POST").uri("/sign-in");
    if let Some(token) = access_token {
        builder = builder.header(header::AUTHORIZATION, format!("Bearer {token}"));
    }

    app.oneshot(builder.body(Body::empty()).unwrap())
        .await
        .unwrap()
}

async fn json_body(response: axum::response::Response) -> Value {
    let body = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    serde_json::from_slice(&body).unwrap()
}

/// `POST /sign-in`から呼び出し元プレイヤーの`player_id`を取得する。
async fn get_player_id(app: Router, access_token: &str) -> String {
    let response = sign_in(app, Some(access_token)).await;
    json_body(response).await["playerId"]
        .as_str()
        .unwrap()
        .to_string()
}

/// パーティ編成・技の付け替えのテストが参照するマスタデータ(pachimon 1件、技グループ2件)を
/// 直接DBへ投入する。`pachimon_id=1`は`move_group_id=1`に属し、候補技は
/// `move_id=1`(初期技)/`move_id=2`(非初期技)。`move_id=3`は別グループ(`move_group_id=2`)の
/// 技で、技の付け替えバリデーション(グループ外拒否)のテストに使う。
async fn seed_test_master_data(pool: &MySqlPool) {
    sqlx::query(
        "INSERT INTO move_groups (move_group_id, name) VALUES (1, 'グループ1'), (2, 'グループ2')",
    )
    .execute(pool)
    .await
    .unwrap();

    sqlx::query(
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) VALUES \
         (1, 'たいあたり', 1, 1, 40, 100, 35), \
         (2, 'ひっかく', 1, 1, 40, 100, 35), \
         (3, 'ほのおのうず', 2, 1, 35, 85, 15)",
    )
    .execute(pool)
    .await
    .unwrap();

    sqlx::query(
        "INSERT INTO move_group_moves (unique_id, group_id, move_id, is_initial) VALUES \
         (1, 1, 1, TRUE), (2, 1, 2, FALSE), (3, 2, 3, TRUE)",
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

/// アイテムマスタ(item_id=1のジェム)を直接DBへ投入する。`player_items`のFK制約を満たすために、
/// signup前提のテストでは必ず先に呼ぶ必要がある。
async fn seed_items_master(pool: &MySqlPool) {
    sqlx::query("INSERT INTO items (item_id, name) VALUES (1, 'ジェム')")
        .execute(pool)
        .await
        .unwrap();
}

/// `player_pachimon`/`player_pachimon_moves`(初期技1件、`slot=1`/`move_id=1`)を
/// 直接DBへ投入し、`player_pachimon_id`を返す。
async fn seed_owned_pachimon(pool: &MySqlPool, player_id: &str, pachimon_id: i64) -> String {
    let player_pachimon_id = ulid::Ulid::new().to_string();
    let zero_stats = json!({"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0});

    sqlx::query(
        "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, effort_values) \
         VALUES (?, ?, ?, ?)",
    )
    .bind(&player_pachimon_id)
    .bind(player_id)
    .bind(pachimon_id)
    .bind(sqlx::types::Json(&zero_stats))
    .execute(pool)
    .await
    .unwrap();

    sqlx::query(
        "INSERT INTO player_pachimon_moves \
            (player_pachimon_move_id, player_pachimon_id, slot, move_id) \
         VALUES (?, ?, 1, 1)",
    )
    .bind(ulid::Ulid::new().to_string())
    .bind(&player_pachimon_id)
    .execute(pool)
    .await
    .unwrap();

    player_pachimon_id
}

async fn edit_party(app: Router, access_token: &str, body: Value) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri("/edit/party")
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(body.to_string()))
            .unwrap(),
    )
    .await
    .unwrap()
}

async fn edit_pachimon_move(
    app: Router,
    access_token: &str,
    player_pachimon_id: &str,
    slot: i32,
    move_id: i64,
) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri("/edit/pachimon_moves")
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(
                json!({
                    "playerPachimonId": player_pachimon_id,
                    "slot": slot,
                    "moveId": move_id,
                })
                .to_string(),
            ))
            .unwrap(),
    )
    .await
    .unwrap()
}

/// signupが成功し(ボディ無し200)、直後に`sign-in`で同じ内容が取得できることを確認する。
/// 初期アイテム(item_id=1のジェム300個)がplayerDiff.itemsに含まれることも確認する。
#[sqlx::test]
async fn test_signup_and_sign_in(pool: MySqlPool) {
    seed_items_master(&pool).await;
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let signup_response = signup(app.clone(), &access_token, "テストプレイヤー").await;
    assert_eq!(signup_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(signup_response.into_body(), usize::MAX)
        .await
        .unwrap();
    assert!(body.is_empty());

    let sign_in_response = sign_in(app.clone(), Some(&access_token)).await;
    assert_eq!(sign_in_response.status(), StatusCode::OK);
    let json = json_body(sign_in_response).await;
    assert_eq!(json["nickname"], "テストプレイヤー");
    assert!(!json["playerId"].as_str().unwrap().is_empty());

    let items = json["playerDiff"]["items"]["upserted"].as_array().unwrap();
    assert_eq!(items.len(), 1);
    assert_eq!(items[0]["itemId"], 1);
    assert_eq!(items[0]["quantity"], 300);
    assert!(
        json["playerDiff"]["items"]["removed"]
            .as_array()
            .unwrap()
            .is_empty()
    );
}

/// `starter_party_slots`マスタの内容が、signup時に`playerDiff.pachimon`/
/// `playerDiff.pachimonMoveMap`/`playerDiff.partySlots`へ複製されることを確認する。
#[sqlx::test]
async fn test_signup_grants_starter_party(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    sqlx::query("INSERT INTO starter_party_slots (slot_no, pachimon_id) VALUES (1, 1)")
        .execute(&pool)
        .await
        .unwrap();

    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "starter-secret").await;
    let signup_response = signup(app.clone(), &access_token, "スターター太郎").await;
    assert_eq!(signup_response.status(), StatusCode::OK);

    let response = sign_in(app.clone(), Some(&access_token)).await;
    assert_eq!(response.status(), StatusCode::OK);
    let json = json_body(response).await;

    let pachimon = json["playerDiff"]["pachimon"]["upserted"]
        .as_array()
        .unwrap();
    assert_eq!(pachimon.len(), 1);
    assert_eq!(pachimon[0]["pachimonId"], 1);

    let moves = json["playerDiff"]["pachimonMoveMap"]["upserted"]
        .as_array()
        .unwrap();
    assert_eq!(moves.len(), 1);
    assert_eq!(moves[0]["moveId"], 1); // move_group_id=1のis_initial技(move_id=1)
    assert_eq!(
        moves[0]["playerPachimonId"],
        pachimon[0]["playerPachimonId"]
    );

    let party_slots = json["playerDiff"]["partySlots"]["upserted"]
        .as_array()
        .unwrap();
    assert_eq!(party_slots.len(), 1);
    assert_eq!(party_slots[0]["slot"], 1);
    assert_eq!(
        party_slots[0]["playerPachimonId"],
        pachimon[0]["playerPachimonId"]
    );
}

/// 同一デバイスで2回目のsignupを行うと409(重複)が返ることを確認する。
#[sqlx::test]
async fn test_signup_duplicate(pool: MySqlPool) {
    seed_items_master(&pool).await;
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let first = signup(app.clone(), &access_token, "プレイヤー1").await;
    assert_eq!(first.status(), StatusCode::OK);

    let second = signup(app.clone(), &access_token, "プレイヤー2").await;
    assert_eq!(second.status(), StatusCode::CONFLICT);
}

/// `Authorization`ヘッダーが無い状態でsignupを行うと401が返ることを確認する。
#[sqlx::test]
async fn test_signup_unauthenticated(pool: MySqlPool) {
    seed_items_master(&pool).await;
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/signup")
                .header("Content-Type", "application/json")
                .body(Body::from(json!({ "nickname": "無認証" }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// デバイス認証は済んでいるがプレイヤー未作成の状態で`sign-in`を呼ぶと404が返ることを確認する。
#[sqlx::test]
async fn test_sign_in_before_signup(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let response = sign_in(app.clone(), Some(&access_token)).await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

/// `Authorization`ヘッダーが無い状態で`sign-in`を呼ぶと401が返ることを確認する。
#[sqlx::test]
async fn test_sign_in_unauthenticated(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = sign_in(app.clone(), None).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// パーティ編成が成功し、DB上の`player_party_slots`に反映されることを確認する。
/// レスポンスの各slotに割当自体のULID(`partySlotId`)が含まれることも確認する。
#[sqlx::test]
async fn test_edit_party_success(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "party-secret-1").await;
    signup(app.clone(), &access_token, "パーティテスト1").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let id_a = seed_owned_pachimon(&pool, &player_id, 1).await;
    let id_b = seed_owned_pachimon(&pool, &player_id, 1).await;

    let response = edit_party(
        app.clone(),
        &access_token,
        json!({ "partySlots": [
            { "slot": 1, "playerPachimonId": id_a },
            { "slot": 2, "playerPachimonId": id_b },
        ] }),
    )
    .await;
    assert_eq!(response.status(), StatusCode::OK);
    let json = json_body(response).await;
    let slots = json["partySlots"]["upserted"].as_array().unwrap();
    assert_eq!(slots.len(), 2);
    assert_eq!(slots[0]["slot"], 1);
    assert_eq!(slots[0]["playerPachimonId"], id_a);
    assert!(!slots[0]["partySlotId"].as_str().unwrap().is_empty());
    assert_eq!(slots[1]["slot"], 2);
    assert_eq!(slots[1]["playerPachimonId"], id_b);
    assert!(json["partySlots"]["removed"].as_array().unwrap().is_empty());
    assert!(json["items"]["upserted"].as_array().unwrap().is_empty());
    assert!(json["pachimon"]["upserted"].as_array().unwrap().is_empty());
    assert!(
        json["pachimonMoveMap"]["upserted"]
            .as_array()
            .unwrap()
            .is_empty()
    );

    let stored: (i32,) =
        sqlx::query_as("SELECT slot FROM player_party_slots WHERE player_pachimon_id = ?")
            .bind(&id_a)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(stored.0, 1);
}

/// 2回目のパーティ編成で、前回設定されていた割当が解除され、`removed`に含まれることを確認する
/// (全置き換えの動作確認)。
#[sqlx::test]
async fn test_edit_party_replaces_previous(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "party-secret-2").await;
    signup(app.clone(), &access_token, "パーティテスト2").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let id_a = seed_owned_pachimon(&pool, &player_id, 1).await;
    let id_b = seed_owned_pachimon(&pool, &player_id, 1).await;

    let first = edit_party(
        app.clone(),
        &access_token,
        json!({ "partySlots": [{ "slot": 1, "playerPachimonId": id_a }] }),
    )
    .await;
    assert_eq!(first.status(), StatusCode::OK);
    let first_json = json_body(first).await;
    let first_party_slot_id = first_json["partySlots"]["upserted"][0]["partySlotId"]
        .as_str()
        .unwrap()
        .to_string();

    let second = edit_party(
        app.clone(),
        &access_token,
        json!({ "partySlots": [{ "slot": 1, "playerPachimonId": id_b }] }),
    )
    .await;
    assert_eq!(second.status(), StatusCode::OK);
    let second_json = json_body(second).await;
    let removed = second_json["partySlots"]["removed"].as_array().unwrap();
    assert_eq!(removed.len(), 1);
    assert_eq!(removed[0], first_party_slot_id);

    let stored_a: (i64,) =
        sqlx::query_as("SELECT COUNT(*) FROM player_party_slots WHERE player_pachimon_id = ?")
            .bind(&id_a)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(stored_a.0, 0);

    let stored_b: (i32,) =
        sqlx::query_as("SELECT slot FROM player_party_slots WHERE player_pachimon_id = ?")
            .bind(&id_b)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(stored_b.0, 1);
}

/// 7体を指定すると400(1-6体の範囲外)が返ることを確認する。
#[sqlx::test]
async fn test_edit_party_too_many_slots(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "party-secret-3").await;
    signup(app.clone(), &access_token, "パーティテスト3").await;
    let player_id = get_player_id(app.clone(), &access_token).await;

    let mut ids = Vec::new();
    for _ in 0..7 {
        ids.push(seed_owned_pachimon(&pool, &player_id, 1).await);
    }
    let slots: Vec<Value> = ids
        .iter()
        .map(|id| json!({ "slot": 1, "playerPachimonId": id }))
        .collect();

    let response = edit_party(app.clone(), &access_token, json!({ "partySlots": slots })).await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// 空の編成(最後の1体まで外す)を指定すると400が返り、既存の編成が消えないことを確認する。
/// クライアントも最後の1体は外せないようにブロックしているが、サーバー側でも拒否する。
#[sqlx::test]
async fn test_edit_party_empty(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "party-secret-empty").await;
    signup(app.clone(), &access_token, "パーティテスト空").await;
    let player_id = get_player_id(app.clone(), &access_token).await;

    let (before,): (i64,) =
        sqlx::query_as("SELECT COUNT(*) FROM player_party_slots WHERE player_id = ?")
            .bind(&player_id)
            .fetch_one(&pool)
            .await
            .unwrap();

    let response = edit_party(app.clone(), &access_token, json!({ "partySlots": [] })).await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);

    let (after,): (i64,) =
        sqlx::query_as("SELECT COUNT(*) FROM player_party_slots WHERE player_id = ?")
            .bind(&player_id)
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(after, before);
}

/// 同一slotへの重複指定で400が返ることを確認する。
#[sqlx::test]
async fn test_edit_party_duplicate_slot(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "party-secret-4").await;
    signup(app.clone(), &access_token, "パーティテスト4").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let id_a = seed_owned_pachimon(&pool, &player_id, 1).await;
    let id_b = seed_owned_pachimon(&pool, &player_id, 1).await;

    let response = edit_party(
        app.clone(),
        &access_token,
        json!({ "partySlots": [
            { "slot": 1, "playerPachimonId": id_a },
            { "slot": 1, "playerPachimonId": id_b },
        ] }),
    )
    .await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// 同一`playerPachimonId`を複数slotに指定すると400が返ることを確認する。
#[sqlx::test]
async fn test_edit_party_duplicate_player_pachimon_id(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "party-secret-5").await;
    signup(app.clone(), &access_token, "パーティテスト5").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let id_a = seed_owned_pachimon(&pool, &player_id, 1).await;

    let response = edit_party(
        app.clone(),
        &access_token,
        json!({ "partySlots": [
            { "slot": 1, "playerPachimonId": id_a },
            { "slot": 2, "playerPachimonId": id_a },
        ] }),
    )
    .await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// 他プレイヤーの`playerPachimonId`を指定すると404が返ることを確認する。
#[sqlx::test]
async fn test_edit_party_not_owned(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let owner_token = register_and_authenticate(app.clone(), "party-secret-6-owner").await;
    signup(app.clone(), &owner_token, "オーナー").await;
    let owner_id = get_player_id(app.clone(), &owner_token).await;
    let owner_pachimon_id = seed_owned_pachimon(&pool, &owner_id, 1).await;

    let other_token = register_and_authenticate(app.clone(), "party-secret-6-other").await;
    signup(app.clone(), &other_token, "他人").await;

    let response = edit_party(
        app.clone(),
        &other_token,
        json!({ "partySlots": [{ "slot": 1, "playerPachimonId": owner_pachimon_id }] }),
    )
    .await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

/// 候補技(グループ内の非初期技)への付け替えが成功し、レスポンス・DB双方に反映されることを確認する。
#[sqlx::test]
async fn test_edit_pachimon_move_success(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "move-secret-1").await;
    signup(app.clone(), &access_token, "技テスト1").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let player_pachimon_id = seed_owned_pachimon(&pool, &player_id, 1).await;

    let response = edit_pachimon_move(app.clone(), &access_token, &player_pachimon_id, 2, 2).await;
    assert_eq!(response.status(), StatusCode::OK);
    let json = json_body(response).await;
    let moves = json["pachimonMoveMap"]["upserted"].as_array().unwrap();
    assert_eq!(moves.len(), 1);
    assert_eq!(moves[0]["playerPachimonId"], player_pachimon_id);
    assert_eq!(moves[0]["slot"], 2);
    assert_eq!(moves[0]["moveId"], 2);
    assert!(
        !moves[0]["playerPachimonMoveId"]
            .as_str()
            .unwrap()
            .is_empty()
    );
    assert!(
        json["pachimonMoveMap"]["removed"]
            .as_array()
            .unwrap()
            .is_empty()
    );

    let stored: (i64,) = sqlx::query_as(
        "SELECT move_id FROM player_pachimon_moves WHERE player_pachimon_id = ? AND slot = 2",
    )
    .bind(&player_pachimon_id)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(stored.0, 2);
}

/// 既存slot(初期技が入っている`slot=1`)への付け替えでは、`player_pachimon_move_id`
/// (割当自体のULID)が変わらず維持されることを確認する。
#[sqlx::test]
async fn test_edit_pachimon_move_preserves_id_on_existing_slot(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "move-secret-preserve").await;
    signup(app.clone(), &access_token, "技テスト維持").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let player_pachimon_id = seed_owned_pachimon(&pool, &player_id, 1).await;

    let before: (String,) = sqlx::query_as(
        "SELECT player_pachimon_move_id FROM player_pachimon_moves \
         WHERE player_pachimon_id = ? AND slot = 1",
    )
    .bind(&player_pachimon_id)
    .fetch_one(&pool)
    .await
    .unwrap();

    // slot=1は初期技(move_id=1)がセット済み。同じslotへ別の候補技へ付け替える。
    let response = edit_pachimon_move(app.clone(), &access_token, &player_pachimon_id, 1, 2).await;
    assert_eq!(response.status(), StatusCode::OK);
    let json = json_body(response).await;
    assert_eq!(
        json["pachimonMoveMap"]["upserted"][0]["playerPachimonMoveId"],
        before.0
    );

    let after: (String,) = sqlx::query_as(
        "SELECT player_pachimon_move_id FROM player_pachimon_moves \
         WHERE player_pachimon_id = ? AND slot = 1",
    )
    .bind(&player_pachimon_id)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(after.0, before.0);
}

/// 技グループ外の技を指定すると400が返ることを確認する。
#[sqlx::test]
async fn test_edit_pachimon_move_invalid_candidate(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "move-secret-2").await;
    signup(app.clone(), &access_token, "技テスト2").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let player_pachimon_id = seed_owned_pachimon(&pool, &player_id, 1).await;

    let response = edit_pachimon_move(app.clone(), &access_token, &player_pachimon_id, 1, 3).await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// slotが範囲外(1-4外)の場合に400が返ることを確認する。
#[sqlx::test]
async fn test_edit_pachimon_move_out_of_range_slot(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "move-secret-3").await;
    signup(app.clone(), &access_token, "技テスト3").await;
    let player_id = get_player_id(app.clone(), &access_token).await;
    let player_pachimon_id = seed_owned_pachimon(&pool, &player_id, 1).await;

    let response = edit_pachimon_move(app.clone(), &access_token, &player_pachimon_id, 5, 1).await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

/// 他プレイヤーの`playerPachimonId`を指定すると404が返ることを確認する。
#[sqlx::test]
async fn test_edit_pachimon_move_not_owned(pool: MySqlPool) {
    seed_items_master(&pool).await;
    seed_test_master_data(&pool).await;
    let state = AppState::from_pool(pool.clone()).await;
    let app = create_router(state);

    let owner_token = register_and_authenticate(app.clone(), "move-secret-4-owner").await;
    signup(app.clone(), &owner_token, "オーナー").await;
    let owner_id = get_player_id(app.clone(), &owner_token).await;
    let owner_pachimon_id = seed_owned_pachimon(&pool, &owner_id, 1).await;

    let other_token = register_and_authenticate(app.clone(), "move-secret-4-other").await;
    signup(app.clone(), &other_token, "他人").await;

    let response = edit_pachimon_move(app.clone(), &other_token, &owner_pachimon_id, 1, 2).await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}
