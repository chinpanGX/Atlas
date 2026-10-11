use std::env;
use std::error::Error;
use std::fs;
use std::path::PathBuf;

use prost::Message;

/// Atlas の .proto の置き場所(Unity クライアントの C# も同じものから生成する)。
const PROTO_ROOT: &str = "../Shared/proto";
/// Atlas の業務エラーの定義ファイル。
const ERRORS: &str = "../Shared/errors.toml";
/// サービスを持つ .proto。足したらここにも足す(atlas/internal はバトルサーバー向けの内部API)。
const PROTO_FILES: &[&str] = &[
    "atlas/v1/player.proto",
    "atlas/v1/party.proto",
    "atlas/v1/scout.proto",
    "atlas/v1/debug.proto",
    "atlas/v1/matchmaking.proto",
    "atlas/internal/v1/battle.proto",
];

fn main() -> Result<(), Box<dyn Error>> {
    let fds = protox::compile(PROTO_FILES, [PROTO_ROOT])?;

    let out_dir = PathBuf::from(env::var("OUT_DIR")?);
    fs::write(out_dir.join("atlas_descriptor.bin"), fds.encode_to_vec())?;

    // クライアントは結合テストで使う
    tonic_prost_build::configure().compile_fds(fds)?;

    voltech_grpc_error_build::compile(ERRORS)?;

    println!("cargo:rerun-if-changed={PROTO_ROOT}");
    println!("cargo:rerun-if-changed={ERRORS}");
    Ok(())
}
