use axum::extract::FromRequestParts;
use axum::http::header;
use axum::http::request::Parts;
use subtle::ConstantTimeEq;

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

/// `X-Internal-Secret`ヘッダーのヘッダー名(BattleServer側`BattleResultReporter.SecretHeaderName`と同じ値)。
pub const INTERNAL_SECRET_HEADER: &str = "x-internal-secret";

/// 内部API(BattleServer→APIサーバー)用のextractor。
///
/// `X-Internal-Secret`ヘッダーを環境変数`INTERNAL_API_SECRET`の値と定数時間で比較する。
/// ヘッダーが無い・値が一致しない場合は`401`を返す。エンドユーザーの`access_token`とは別軸の認証。
pub struct InternalService;

#[axum::async_trait]
impl FromRequestParts<AppState> for InternalService {
    type Rejection = AppError;

    async fn from_request_parts(
        parts: &mut Parts,
        state: &AppState,
    ) -> Result<Self, Self::Rejection> {
        let secret = parts
            .headers
            .get(INTERNAL_SECRET_HEADER)
            .ok_or(AppError::Unauthorized)?
            .as_bytes();

        if secret
            .ct_eq(state.battle.internal_api_secret.as_bytes())
            .into()
        {
            Ok(InternalService)
        } else {
            Err(AppError::Unauthorized)
        }
    }
}
