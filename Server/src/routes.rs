use axum::Router;
use utoipa::OpenApi;
use utoipa_swagger_ui::SwaggerUi;

use crate::api::{auth, chat, device, player, scout};
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
        .merge(SwaggerUi::new("/swagger-ui").url("/api-docs/openapi.json", ApiDoc::openapi()))
        .with_state(state)
}
