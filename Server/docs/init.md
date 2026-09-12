# プロジェクト初期構築手順

このドキュメントは、プロジェクトを最初にゼロから作成する際の手順をまとめたもの。他の作業者がクローンして開発に参加する場合は `docs.md` を参照すること。

## 前提

- Rust(edition 2024 対応バージョン)がインストール済みであること
- Docker / Docker Desktop がインストール済みであること

```bash
rustc --version
cargo --version
docker --version
docker compose version
```

## 1. プロジェクトの作成

```bash
cargo new Server
cd Server
```

`Atlas/Client`(Unity)と合わせて、以下のフォルダ構成にする。

```
Atlas/
├── Client/   # Unityクライアント
└── Server/   # Rustバックエンド(このプロジェクト)
```

## 2. 依存クレートの追加

`Cargo.toml` の `[dependencies]` に以下を追加する。

```toml
[dependencies]
axum = "0.7"
tokio = { version = "1", features = ["full"] }
sqlx = { version = "0.8", features = ["runtime-tokio", "mysql", "macros"] }
dotenvy = "0.15"
```

## 3. docker-compose.yml の作成

プロジェクトルート(`Cargo.toml`と同じ階層)に作成する。

```yaml
services:
  mysql:
    image: mysql:8.0
    container_name: atlas_mysql
    restart: unless-stopped
    environment:
      MYSQL_ALLOW_EMPTY_PASSWORD: "yes"
      MYSQL_DATABASE: atlas_dev
    ports:
      - "3306:3306"
    volumes:
      - mysql_data:/var/lib/mysql

volumes:
  mysql_data:
```

`MYSQL_ALLOW_EMPTY_PASSWORD: "yes"` は、`root`ユーザーのパスワードなし接続を許可する設定(学習用途のローカル環境専用。本番環境では絶対に使わないこと)。

## 4. Makefile の作成

プロジェクトルートに作成する。

```makefile
.PHONY: up down ps logs mysql clean restart

up:
	docker compose up -d

down:
	docker compose down

ps:
	docker compose ps

logs:
	docker compose logs -f mysql

mysql:
	docker exec -it atlas_mysql mysql -u root atlas_dev

clean:
	docker compose down -v

restart: down up
```

## 5. .gitignore の設定

`.env`など、ローカル環境固有のファイルをGit管理対象から除外する。

```
.env
/target
```

## 6. 環境構築確認用コードの実装(examples/setup_check.rs)

`src/main.rs`は今後実際のAtlasサーバーを実装していく本体として空けておき、axum + tokioの疎通確認だけを行う検証用コードは`examples/`配下に分離する(`cargo run --example <名前>`で本体と独立して実行できる)。

```rust
// examples/setup_check.rs
use axum::{routing::get, Router};

#[tokio::main]
async fn main() {
    let app = Router::new().route("/ping", get(ping_handler));

    let listener = tokio::net::TcpListener::bind("127.0.0.1:3000")
        .await
        .unwrap();

    println!("[setup_check] Server running on http://127.0.0.1:3000");
    axum::serve(listener, app).await.unwrap();
}

async fn ping_handler() -> &'static str {
    "pong"
}
```

`src/main.rs`は`cargo new`直後のデフォルトのままでよい。

```rust
// src/main.rs
fn main() {
    println!("Hello, world!");
}
```

## 7. 動作確認

```bash
make up
make mysql
```

```sql
SHOW DATABASES;
```

`atlas_dev` が表示されればDBは正常。`exit`で抜ける。

```bash
cargo build
cargo run --example setup_check
```

```
[setup_check] Server running on http://127.0.0.1:3000
```

Postmanなどで以下を確認する。

```
GET http://127.0.0.1:3000/ping
```

`pong` が返れば初期構築は完了。

以降、他の作業者がこのリポジトリをクローンする際は `docs.md` の手順に従う。