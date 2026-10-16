use tonic::{Request, Status};
use voltech_grpc_auth::AuthContext;
use voltech_grpc_error::{Internal, ResultExt, failed_precondition, internal};

use crate::proto::errors;

/// `AuthLayer` が入れた認証の情報を取り出す。
pub(crate) fn auth_context<T>(request: &Request<T>) -> Result<&AuthContext, Status> {
    // AuthLayer を付け忘れたときだけ無い。クライアントには原因が無いので INTERNAL にする
    request.extensions().get::<AuthContext>().ok_or_else(|| {
        internal(Internal::msg(
            "AuthContext is missing. Is AuthLayer applied to this service?",
        ))
    })
}

/// サインアップ済みのプレイヤーの ID(ULID の文字列)を取り出す。まだなら PLAYER_NOT_FOUND にする。
pub(crate) fn player_id<T>(request: &Request<T>) -> Result<String, Status> {
    let context = auth_context(request)?;
    if context.player_created {
        Ok(context.player_id.to_string())
    } else {
        Err(player_not_found())
    }
}

/// プレイヤーがまだ作られていないことを表す業務エラーを作る。
pub(crate) fn player_not_found() -> Status {
    failed_precondition(errors::PLAYER_NOT_FOUND, "player is not created yet")
}

/// 冪等性の対象の RPC は、基盤の `Transactions::run` に `player_id` を ULID で渡す。
pub(crate) fn to_ulid(player_id: &str) -> Result<ulid::Ulid, Status> {
    ulid::Ulid::from_string(player_id).or_internal()
}
