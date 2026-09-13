use axum::{
    http::StatusCode,
    response::{IntoResponse, Response},
};

pub enum AppError {
    Unauthorized,
    InternalError,
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        match self {
            AppError::Unauthorized => (StatusCode::UNAUTHORIZED, "Unauthorized").into_response(),
            AppError::InternalError => {
                (StatusCode::INTERNAL_SERVER_ERROR, "Internal Server Error").into_response()
            }
        }
    }
}
