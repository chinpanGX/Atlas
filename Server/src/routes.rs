use axum::Router;
use tower_http::LatencyUnit;
use tower_http::trace::{DefaultMakeSpan, DefaultOnResponse, TraceLayer};
use tracing::Level;
use utoipa::OpenApi;
use utoipa_swagger_ui::SwaggerUi;

use crate::api::{auth, battle, chat, device, player, scout};
use crate::http_log;
use crate::openapi::ApiDoc;
use crate::state::AppState;

pub fn create_router(state: AppState) -> Router {
    Router::new()
        .route(
            "/devices",
            axum::routing::post(device::register_device_handler),
        )
        .route(
            "/devices/authenticate",
            axum::routing::post(device::authenticate_device_handler),
        )
        .route(
            "/auth/verify",
            axum::routing::get(auth::verify_token_handler),
        )
        .route("/signup", axum::routing::post(player::signup_handler))
        .route("/sign-in", axum::routing::post(player::sign_in_handler))
        .route(
            "/edit/party",
            axum::routing::post(player::edit_party_handler),
        )
        .route(
            "/edit/pachimon_moves",
            axum::routing::post(player::edit_pachimon_move_handler),
        )
        .route(
            "/chat/send",
            axum::routing::post(chat::send_message_handler),
        )
        .route(
            "/chat/poll",
            axum::routing::get(chat::poll_messages_handler),
        )
        .route(
            "/scout/banners",
            axum::routing::get(scout::list_banners_handler),
        )
        .route(
            "/scout/rolls",
            axum::routing::post(scout::create_roll_handler),
        )
        .route(
            "/scout/rolls/:rollId/select",
            axum::routing::post(scout::select_roll_handler),
        )
        .route(
            "/battle/queue",
            axum::routing::post(battle::join_queue_handler).delete(battle::leave_queue_handler),
        )
        .route(
            "/battle/queue/status",
            axum::routing::get(battle::queue_status_handler),
        )
        .merge(SwaggerUi::new("/swagger-ui").url("/api-docs/openapi.json", ApiDoc::openapi()))
        // 後に追加したlayerほど外側になる。TraceLayerのspan(method/uri)の内側で
        // ボディ・SQLのログが出るよう、log_bodiesを先に追加する
        .layer(axum::middleware::from_fn(http_log::log_bodies))
        .layer(
            TraceLayer::new_for_http()
                .make_span_with(DefaultMakeSpan::new().level(Level::INFO))
                .on_response(
                    DefaultOnResponse::new()
                        .level(Level::INFO)
                        .latency_unit(LatencyUnit::Millis),
                ),
        )
        .with_state(state)
}
