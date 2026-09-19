use axum::extract::FromRequestParts;
use axum::http::header;
use axum::http::request::Parts;

use crate::error::AppError;
use crate::service::auth_service;
use crate::state::AppState;

/// 要認証エンドポイント用のextractor。
///
/// `Authorization: Bearer <access_token>`ヘッダーを検証し、紐づく`device_id`を
/// ハンドラの引数として取り出す。トークンが無い・無効な場合は`401`を返す。
pub struct AuthenticatedDevice {
    pub device_id: String,
}

#[axum::async_trait]
impl FromRequestParts<AppState> for AuthenticatedDevice {
    type Rejection = AppError;

    async fn from_request_parts(
        parts: &mut Parts,
        state: &AppState,
    ) -> Result<Self, Self::Rejection> {
        let access_token = parts
            .headers
            .get(header::AUTHORIZATION)
            .and_then(|value| value.to_str().ok())
            .and_then(|value| value.strip_prefix("Bearer "))
            .ok_or(AppError::Unauthorized)?;

        let device_id = auth_service::resolve_device_id(&state.pool, access_token).await?;

        Ok(AuthenticatedDevice { device_id })
    }
}
