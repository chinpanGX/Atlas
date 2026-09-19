use axum::{
    http::StatusCode,
    response::{IntoResponse, Response},
};

pub enum AppError {
    Unauthorized,
    NotFound,
    Conflict,
    InternalError,
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        match self {
            AppError::Unauthorized => (StatusCode::UNAUTHORIZED, "Unauthorized").into_response(),
            AppError::NotFound => (StatusCode::NOT_FOUND, "Not Found").into_response(),
            AppError::Conflict => (StatusCode::CONFLICT, "Conflict").into_response(),
            AppError::InternalError => {
                (StatusCode::INTERNAL_SERVER_ERROR, "Internal Server Error").into_response()
            }
        }
    }
}
