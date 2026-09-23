//! 開発時にリクエスト/レスポンスの中身を確認するためのログ出力。
//!
//! `create_router`で`TraceLayer`(method/uri/ステータス/処理時間)の内側に挟む
//! ミドルウェアとして使い、DEBUGレベルが有効なときだけボディ(JSON/エラーのtext)をログに出す。
//! 秘密情報(`secretKey`/`accessToken`)は伏字にし、`Authorization`ヘッダーは出力しない。

use axum::body::{Body, Bytes};
use axum::extract::Request;
use axum::http::{HeaderMap, StatusCode, header};
use axum::middleware::Next;
use axum::response::{IntoResponse, Response};
use serde_json::Value;

/// 伏字にするJSONキー(DTOは`camelCase`だが、念のため`snake_case`も含める)
const MASKED_KEYS: &[&str] = &["secretKey", "accessToken", "secret_key", "access_token"];

/// ログに出すボディの最大文字数(超えた分は切り詰める)
const MAX_LOG_CHARS: usize = 4096;

/// リクエストボディをバッファする上限(axumの`DefaultBodyLimit`と同じ2MB)
const MAX_REQUEST_BODY_BYTES: usize = 2 * 1024 * 1024;

/// リクエスト/レスポンスのボディをDEBUGレベルでログ出力するミドルウェア。
///
/// DEBUGが無効なときはボディを読まずにそのまま次へ渡すため、通常運用時のコストはない。
pub async fn log_bodies(req: Request, next: Next) -> Response {
    if !tracing::enabled!(tracing::Level::DEBUG) {
        return next.run(req).await;
    }

    let (parts, body) = req.into_parts();
    let bytes = match axum::body::to_bytes(body, MAX_REQUEST_BODY_BYTES).await {
        Ok(bytes) => bytes,
        Err(e) => {
            tracing::warn!(error = %e, "failed to read request body");
            return StatusCode::PAYLOAD_TOO_LARGE.into_response();
        }
    };
    if !bytes.is_empty() {
        tracing::debug!(body = %render_body(&bytes), "request body");
    }

    let response = next
        .run(Request::from_parts(parts, Body::from(bytes)))
        .await;

    // Swagger UIのHTML/JS等はログに出さない(AppErrorはtext/plainで返るので対象に含める)
    if !is_loggable(response.headers()) {
        return response;
    }

    let (parts, body) = response.into_parts();
    let bytes = match axum::body::to_bytes(body, usize::MAX).await {
        Ok(bytes) => bytes,
        Err(e) => {
            tracing::warn!(error = %e, "failed to read response body");
            return StatusCode::INTERNAL_SERVER_ERROR.into_response();
        }
    };
    tracing::debug!(status = %parts.status, body = %render_body(&bytes), "response body");

    Response::from_parts(parts, Body::from(bytes))
}

fn is_loggable(headers: &HeaderMap) -> bool {
    headers
        .get(header::CONTENT_TYPE)
        .and_then(|v| v.to_str().ok())
        .is_some_and(|v| v.starts_with("application/json") || v.starts_with("text/plain"))
}

/// ボディをログ用の文字列にする。JSONなら秘密情報を伏字にし、長すぎる場合は切り詰める。
fn render_body(bytes: &Bytes) -> String {
    let text = match serde_json::from_slice::<Value>(bytes) {
        Ok(mut value) => {
            mask_secrets(&mut value);
            value.to_string()
        }
        Err(_) => String::from_utf8_lossy(bytes).into_owned(),
    };
    truncate(text)
}

fn mask_secrets(value: &mut Value) {
    match value {
        Value::Object(map) => {
            for (key, v) in map.iter_mut() {
                if MASKED_KEYS.contains(&key.as_str()) {
                    *v = Value::String("***".to_string());
                } else {
                    mask_secrets(v);
                }
            }
        }
        Value::Array(items) => items.iter_mut().for_each(mask_secrets),
        _ => {}
    }
}

fn truncate(text: String) -> String {
    match text.char_indices().nth(MAX_LOG_CHARS) {
        Some((idx, _)) => format!("{}...(truncated)", &text[..idx]),
        None => text,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_render_body_masks_secrets_recursively() {
        let body = Bytes::from(
            r#"{"deviceId":"d1","secretKey":"s","nested":[{"accessToken":"t","expiresIn":3600}]}"#,
        );

        let rendered = render_body(&body);

        assert!(rendered.contains(r#""deviceId":"d1""#));
        assert!(rendered.contains(r#""secretKey":"***""#));
        assert!(rendered.contains(r#""accessToken":"***""#));
        assert!(rendered.contains(r#""expiresIn":3600"#));
        assert!(!rendered.contains(r#""s""#));
        assert!(!rendered.contains(r#""t""#));
    }

    #[test]
    fn test_render_body_truncates_long_text() {
        let body = Bytes::from("あ".repeat(MAX_LOG_CHARS + 10));

        let rendered = render_body(&body);

        assert!(rendered.ends_with("...(truncated)"));
        assert_eq!(
            rendered.chars().count(),
            MAX_LOG_CHARS + "...(truncated)".len()
        );
    }
}
