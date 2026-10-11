//! `Shared/proto` の .proto と `Shared/errors.toml` から `build.rs` が生成したコード。

pub mod atlas {
    pub mod v1 {
        tonic::include_proto!("atlas.v1");
    }

    /// バトルサーバー向けの内部API。
    pub mod internal {
        pub mod v1 {
            tonic::include_proto!("atlas.internal.v1");
        }
    }
}

/// Atlas の業務エラー(domain `atlas`)のエラーコード。
pub mod errors {
    include!(concat!(env!("OUT_DIR"), "/errors.rs"));
}

/// Atlas の .proto とその依存の FileDescriptorSet。冪等性の表と gRPC リフレクションに使う。
pub const FILE_DESCRIPTOR_SET: &[u8] =
    include_bytes!(concat!(env!("OUT_DIR"), "/atlas_descriptor.bin"));
