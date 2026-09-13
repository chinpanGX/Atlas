use axum::Router;
use crate::api::device;
use crate::state::AppState;

pub fn create_router(state: AppState) -> Router {
    Router::new()
        .route("/devices", axum::routing::post(device::register_device_handler))
        .with_state(state)
}