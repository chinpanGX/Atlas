use axum::Router;
use crate::api::{auth, chat, device, player};
use crate::state::AppState;

pub fn create_router(state: AppState) -> Router {
    Router::new()
        .route("/devices", axum::routing::post(device::register_device_handler))
        .route(
            "/devices/authenticate",
            axum::routing::post(device::authenticate_device_handler),
        )
        .route("/auth/verify", axum::routing::get(auth::verify_token_handler))
        .route("/players", axum::routing::post(player::create_player_handler))
        .route("/players/me", axum::routing::get(player::get_me_handler))
        .route("/chat/send", axum::routing::post(chat::send_message_handler))
        .route("/chat/poll", axum::routing::get(chat::poll_messages_handler))
        .with_state(state)
}