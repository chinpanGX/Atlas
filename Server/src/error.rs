/// サービス層のエラー。gRPC のハンドラで `tonic::Status` に変える。
pub enum AppError {
    BadRequest(String),
    NotFound,
    Conflict,
    InternalError,
}

impl AppError {
    /// ログに出すための種類の名前。
    pub fn kind(&self) -> &'static str {
        match self {
            AppError::BadRequest(_) => "BadRequest",
            AppError::NotFound => "NotFound",
            AppError::Conflict => "Conflict",
            AppError::InternalError => "InternalError",
        }
    }
}
