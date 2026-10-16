use voltech_grpc_error::Internal;

/// サービス層のエラー。gRPC のハンドラで `tonic::Status` に変える。
#[derive(Debug)]
pub enum AppError {
    BadRequest(String),
    NotFound,
    Conflict,
    /// DB アクセスの失敗など。原因と発生した場所を持ち、`Status` にするときにログへ出す
    /// (呼び出し元へは詳細を返さない)。
    InternalError(Internal),
}

impl AppError {
    /// 元のエラーが無い内部エラー(マスタの不整合など)を、説明だけで作る。
    #[track_caller]
    pub fn internal(message: &'static str) -> Self {
        Self::InternalError(Internal::msg(message))
    }
}

/// DB・JWT などのエラーを `?` で内部エラーにする。発生した場所は `?` を書いた場所になる。
impl<E> From<E> for AppError
where
    E: std::error::Error + Send + Sync + 'static,
{
    #[track_caller]
    fn from(err: E) -> Self {
        Self::InternalError(Internal::from(err))
    }
}

/// 内部エラーは原因をそのまま引き継ぐ。ほかの種類は、クライアント向けのハンドラでは起きないはずのものなので、
/// 種類を説明にした内部エラーにする。
impl From<AppError> for Internal {
    #[track_caller]
    fn from(err: AppError) -> Self {
        match err {
            AppError::InternalError(internal) => internal,
            AppError::BadRequest(message) => {
                Internal::msg(format!("unexpected bad request: {message}"))
            }
            AppError::NotFound => Internal::msg("unexpected not found"),
            AppError::Conflict => Internal::msg("unexpected conflict"),
        }
    }
}
