use argon2::password_hash::{SaltString, rand_core::OsRng};
use argon2::{Argon2, PasswordHasher};
use chrono::Utc;
use sqlx::MySqlPool;
use ulid::Ulid;

use crate::error::AppError;
use crate::model::device::Device;

/// 新しいデバイスを登録する。
///
/// `secret_key`をハッシュ化した上でDBに保存し、新規に発行した
/// `device_id`を持つ`Device`を返す。
///
/// # Arguments
/// * `pool` - MySQLへのコネクションプール
/// * `secret_key` - クライアント側で生成されたランダムな秘密鍵(平文)
///
/// # Returns
/// 登録に成功した場合、生成された`device_id`・ハッシュ化済みの
/// `secret_key_hash`を含む`Device`を返す。
///
/// # Errors
/// パスワードのハッシュ化に失敗した場合、またはDBへの保存に失敗した場合に
/// `AppError::InternalError`を返す。
// 戻り値がResult: `try/catch` の代わりに、失敗をResult<成功,失敗>として表現する
pub async fn register(pool: &MySqlPool, secret_key: &str) -> Result<Device, AppError> {
    // &T/&mut T: C++の参照と同じ構文だが、借用ルールをコンパイラが静的に強制する
    // let: C#のvarと違い、デフォルトで再代入不可(mutで明示的に可変化)
    let device_id = Ulid::new().to_string();

    // secret_keyをハッシュ化(平文保存しない)
    let salt = SaltString::generate(&mut OsRng);
    let argon2 = Argon2::default();
    let secret_key_hash = argon2
        .hash_password(secret_key.as_bytes(), &salt)
        .map_err(|_| AppError::InternalError)? // |_| ...ラムダ式の記述 ?はエラーなら即return, Okなら続行
        .to_string();

    sqlx::query("INSERT INTO devices (device_id, secret_key_hash) VALUES (?, ?)")
        .bind(&device_id)
        .bind(&secret_key_hash)
        .execute(pool)
        .await // Futureは.awaitするまで何もしない（つけ忘れてもコンパイルは通る）
        .map_err(|_| AppError::InternalError)?;

    Ok(Device {
        device_id, // フィールド名=変数名なら `device_id: device_id` を省略できる
        secret_key_hash,
        created_at: Utc::now().naive_utc(),
    })
}
