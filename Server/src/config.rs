//! 環境変数から読む、ログの設定。

use std::env;

use voltech_grpc_log::LogConfig;
use voltech_grpc_transaction::GameLogSink;

use crate::grpc::BoxError;

/// どの行にも付けるサービス名の既定(`OTEL_SERVICE_NAME` で上書きできる)。
const DEFAULT_SERVICE_NAME: &str = "atlas-api";
/// 環境名の既定(`APP_ENV` で上書きできる)。
const DEFAULT_ENVIRONMENT: &str = "local";

/// ログの設定を環境変数から読む。無い項目は基盤の既定の値にする。
///
/// - `LOG_FORMAT`: `json`(既定)か `text`(開発中にターミナルで読むとき)
/// - `RUST_LOG`: レベルのフィルター(既定は `info`)
/// - `LOG_FIELD_NAMES`: JSON の項目名。`standard`(既定)か `gcp`
/// - `OTEL_SERVICE_NAME`: サービス名(既定は `atlas-api`)
/// - `APP_ENV`: 環境名(既定は `local`)
///
/// # Errors
/// `LOG_FORMAT`・`LOG_FIELD_NAMES` が知らない値のときにエラーを返す。
pub fn log_config_from_env() -> Result<LogConfig, BoxError> {
    let service_name =
        env::var("OTEL_SERVICE_NAME").unwrap_or_else(|_| DEFAULT_SERVICE_NAME.to_owned());
    let environment = env::var("APP_ENV").unwrap_or_else(|_| DEFAULT_ENVIRONMENT.to_owned());
    let mut log = LogConfig::new(service_name, environment);
    if let Ok(format) = env::var("LOG_FORMAT") {
        log.format = format.parse()?;
    }
    if let Ok(filter) = env::var("RUST_LOG") {
        log.filter = filter;
    }
    if let Ok(field_names) = env::var("LOG_FIELD_NAMES") {
        log.field_names = field_names.parse()?;
    }
    Ok(log)
}

/// ゲームログの出力先を `GAME_LOG_SINK` から読む。`database`(既定。`game_logs` テーブル)か `stdout`。
///
/// # Errors
/// 知らない値のときにエラーを返す(書き間違いのまま別の出力先で動き続けないように、起動を止める)。
pub fn game_log_sink_from_env() -> Result<GameLogSink, BoxError> {
    match env::var("GAME_LOG_SINK").as_deref() {
        Err(_) | Ok("database") => Ok(GameLogSink::Database),
        Ok("stdout") => Ok(GameLogSink::Stdout),
        Ok(other) => Err(format!(
            "unknown GAME_LOG_SINK {other:?} (expected one of: database, stdout)"
        )
        .into()),
    }
}
