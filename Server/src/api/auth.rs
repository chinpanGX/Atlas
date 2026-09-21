use crate::extractor::AuthenticatedDevice;

/// アクセストークンを検証するAPIハンドラ。
///
/// `Authorization: Bearer <access_token>`ヘッダーのトークンが有効であれば`200`、
/// ヘッダーが無い・トークンが無効(期限切れ・存在しない)であれば`401`を返す。
/// 検証自体は`AuthenticatedDevice`extractorが行う。
#[utoipa::path(
    get,
    path = "/auth/verify",
    responses(
        (status = 200, description = "トークンが有効"),
        (status = 401, description = "トークンが無い、または無効"),
    ),
    security(("bearer_auth" = [])),
    tag = "auth",
)]
pub async fn verify_token_handler(_device: AuthenticatedDevice) {}
