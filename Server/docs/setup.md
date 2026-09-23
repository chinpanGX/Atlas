# 環境構築手順

このドキュメントは、既に作成済みのプロジェクトをクローンして開発に参加する際のセットアップ手順をまとめたもの。プロジェクトをゼロから新規作成する手順は `init.md` を参照すること。

## 前提

- Rust(edition 2024 対応バージョン)がインストール済みであること
- Docker / Docker Desktop がインストール済みであること
- `make` コマンドが利用できること(Git Bash利用時は別途セットアップが必要。下記「0. makeコマンドのセットアップ」を参照)

```bash
rustc --version
cargo --version
docker --version
docker compose version
make --version
```

上記コマンドでバージョンが表示されることを確認する。

## 0. makeコマンドのセットアップ(Git Bash)

Git Bashには標準で`make`が含まれていないため、以下のいずれかの方法で導入する。

**方法A: Chocolateyでインストール(推奨)**

PowerShell(管理者権限)で以下を実行する。

```powershell
choco install make
```

インストール後、Git Bashを開き直せば利用できるようになる。

**方法B: Scoopでインストール**

```powershell
scoop install make
```

**方法C: 手動配置**

1. [ezwinports](https://sourceforge.net/projects/ezwinports/files/) から `make-4.x-without-guile-w32-bin.zip` をダウンロード
2. 解凍して `bin/make.exe` を `C:\Program Files\Git\usr\bin\` にコピー
3. Git Bashを再起動

導入後、以下のようにバージョンが表示されれば成功。

```
$ make --version
GNU Make 4.4.1
Built for Windows32
```

以降の手順では、`docker compose ...`の代わりに`Atlas/Server/Makefile`に定義された短縮コマンド(`make up` / `make ps` / `make mysql` / `make down` / `make clean`)も利用できる。

## 1. リポジトリの取得

```bash
git clone <リポジトリURL>
cd Atlas/Server
```

`docker-compose.yml`、`Makefile`、`Cargo.toml`の依存クレート設定はリポジトリに含まれているため、新規に作成する必要はない。

## 2. MySQLコンテナの起動

```bash
make up
```

起動確認。`STATUS` が `Up` になっていればOK。

```bash
make ps
```

## 3. MySQL接続確認

```bash
make mysql
```

パスワードなしでそのまま `mysql>` プロンプトが表示されれば成功。

```sql
SHOW DATABASES;
```

`atlas_dev` が一覧に含まれていることを確認する。`exit` で抜ける。

## 4. 環境変数の設定

プロジェクトルート(`Cargo.toml`と同じ階層)に、各自のローカル環境用として `.env` を新規作成する(Git管理対象外のため、クローンしただけでは存在しない)。

```env
DATABASE_URL=mysql://root@127.0.0.1:3306/atlas_dev
BATTLE_TOKEN_SECRET=<32バイト以上のランダム値>
INTERNAL_API_SECRET=<ランダム値>
```

`BATTLE_TOKEN_SECRET`・`INTERNAL_API_SECRET`はBattleServerと同じ値にする必要がある(未設定だと
起動・テストがpanicする)。生成方法・BattleServer側の設定・その他の変数は
リポジトリ直下の[DEVELOPMENT.md](../../DEVELOPMENT.md)「3. 共通の設定」を参照。

## 5. DBスキーマの反映(マイグレーション)

`migrations/`ディレクトリの内容はリポジトリに含まれているため、`sqlx-cli`でDBに反映するだけでよい(テーブル定義を新規に書く必要はない)。

`sqlx-cli`のインストール(初回のみ)。

```bash
cargo install sqlx-cli --no-default-features --features mysql
```

マイグレーションを実行する(`.env`の`DATABASE_URL`を参照する)。

```bash
sqlx migrate run
```

反映されたか確認する。

```bash
make mysql
```

```sql
SHOW TABLES;
```

`devices`, `access_tokens`, `messages`の3つが表示されれば成功。`exit`で抜ける。

## 6. ビルド・起動確認

環境構築の確認には、本体の`main.rs`ではなく`examples/setup_check.rs`(axum + tokioが動くかどうかだけを確認する検証用コード)を使う。

```bash
cargo build
cargo run --example setup_check
```

以下のようなログが表示されれば起動成功。

```
[setup_check] Server running on http://127.0.0.1:3000
```

## 7. 動作確認(Postman)

Postmanなどで以下にリクエストを送り、`pong` が返ってくることを確認する。

```
GET http://127.0.0.1:3000/ping
```

## コンテナの停止・削除

開発を終了する際は以下で停止する(データは保持される)。

```bash
make down
```

DBデータごと完全に削除したい場合は以下を使う。

```bash
make clean
```

## 日常的に使うコマンド

ビルド・テスト実行など、セットアップ後に日常的に使うコマンドは
`notes/cargo-commands.md` にまとめてある。