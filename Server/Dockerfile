# APIサーバー(Rust)のイメージ。リポジトリのルートを起点にビルドする(build.rs が Shared/proto・Shared/errors.toml を読むため)。
#   docker build -f Server/Dockerfile -t atlas-api .
# マイグレーションはサーバーの起動時に流れる。マスタの投入は同じイメージの seed_master_data で行う(マスタの JSON はバイナリに埋め込み済み)。
#   docker compose run --rm api seed_master_data

FROM rust:1.98-bookworm AS build
WORKDIR /src
COPY Shared/proto Shared/proto
COPY Shared/errors.toml Shared/errors.toml
COPY Server Server
WORKDIR /src/Server
RUN cargo build --release --locked --bin Server --bin seed_master_data

FROM debian:bookworm-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /src/Server/target/release/Server /usr/local/bin/Server
COPY --from=build /src/Server/target/release/seed_master_data /usr/local/bin/seed_master_data
# サーバーの状態(メンテナンス・最低バージョン)。デプロイ先ではボリュームで差し替え、再起動せずに切り替える
COPY Server/config/server_status.toml /app/config/server_status.toml
# コンテナの外から届くよう、すべてのアドレスで待ち受ける(既定は 127.0.0.1)
ENV GRPC_ADDR=0.0.0.0:50052 \
    SERVER_ADDR=0.0.0.0:3000 \
    SERVER_STATUS_PATH=/app/config/server_status.toml
EXPOSE 50052 3000
CMD ["Server"]
