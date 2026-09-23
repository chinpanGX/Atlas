# 開発環境ガイド

Atlasの開発者(Client / Server / BattleServer)が共通で行う設定と、よく使うコマンドをまとめたもの。
設計の背景は[README.md](README.md)・`Shared/docs/design/`、各コンポーネント固有の詳細は下記リンク先を参照。

## 全体像

```
Unity Client ──REST──▶ Server(Rust/Axum)      http://127.0.0.1:3000
     │                    │  ▲
     │                    ▼  │ POST /internal/battle/result(対戦結果)
     │                 MySQL(Docker)            127.0.0.1:3306 / DB: atlas_dev
     │
     └──gRPC/StreamingHub──▶ BattleServer(C#/MagicOnion)  http://127.0.0.1:5000
```

| コンポーネント | ディレクトリ | 役割 | ポート |
|---|---|---|---|
| Client | `Client/AtlasUnityProject/` | Unityクライアント | — |
| Server | `Server/` | REST API(認証・スカウト・パーティ・マッチング・結果記録) | 3000 |
| BattleServer | `BattleServer/` | リアルタイム対戦(MagicOnion) | 5000 |
| MySQL | `Server/docker-compose.yml` | 唯一のデータストア(書き込みはServer経由) | 3306 |

クライアントはマッチング成立時にServerから`battleServer`(URL)と`battleToken`を受け取り、BattleServerへ接続する。

## 1. 必要なツール

| ツール | バージョン | 使う人 | 確認コマンド |
|---|---|---|---|
| Git(サブモジュールはSSHで取得) | — | 全員 | `git --version` |
| Unity | 6000.6.2f1 | Client | Unity Hubで確認 |
| Docker Desktop | — | Server / BattleServer / Client(通しで動かす場合) | `docker compose version` |
| Rust | edition 2024対応(1.85以上) | Server | `cargo --version` |
| make | — | Server | `make --version`(Git Bashには無いので導入が必要。[Server/docs/setup.md](Server/docs/setup.md)「0.」参照) |
| sqlx-cli | — | Server | `sqlx --version`(`cargo install sqlx-cli --no-default-features --features mysql`) |
| .NET SDK | 10 | BattleServer / api-codegen / master-data-pipeline | `dotnet --version` |
| Python | 3.10以上 | master-data-pipeline | `python --version`(`pip install -r master-data-pipeline/requirements.txt`) |

Client担当でも、実際のサーバーにつないで動かすならServer・BattleServerを起動できる状態にしておく。

## 2. リポジトリの取得

```bash
git clone --recurse-submodules <リポジトリURL>
# 既にcloneしている場合
git submodule update --init --recursive
```

`master-data-pipeline` / `api-codegen` / `Supplement` はサブモジュール([.gitmodules](.gitmodules))。

## 3. 共通の設定(シークレット・環境変数)

**ServerとBattleServerで同じ値を設定する必要があるシークレットが2つある。** 値が食い違うと、エラーにならずに
「対戦に入れない」「結果が記録されない」という形で表面化するので注意する。

| 変数 | 設定先 | 用途 | 注意 |
|---|---|---|---|
| `BATTLE_TOKEN_SECRET` | Server・BattleServer(**同じ値**) | `battleToken`(JWT, HS256)の署名・検証 | **32バイト以上**(BattleServerは未満だと起動時にエラー)。食い違うと`JoinAsync`が全て`InvalidToken`になる |
| `INTERNAL_API_SECRET` | Server・BattleServer(**同じ値**) | BattleServer→Serverの内部API(`X-Internal-Secret`ヘッダー) | 食い違うと結果記録が`401`になる。BattleServer側が未設定だと結果を送らず警告ログのみ |
| `DATABASE_URL` | Server | MySQL接続先 | 例: `mysql://root@127.0.0.1:3306/atlas_dev` |
| `SERVER_ADDR` | Server | APIサーバーの待受アドレス | 省略時`127.0.0.1:3000`。変えたらClientの`ApiBaseUrl`も合わせる |
| `BATTLE_SERVER_URL` | Server | マッチ成立時にクライアントへ返すBattleServerのURL | 省略時`http://127.0.0.1:5000` |
| `API_SERVER_URL` | BattleServer | 対戦結果の送信先(Server) | 省略時`http://127.0.0.1:3000` |
| `RUST_LOG` | Server(任意) | ログ出力レベル | 下記「Server」参照 |

### シークレットの生成

```bash
openssl rand -hex 32   # 64文字。BATTLE_TOKEN_SECRET / INTERNAL_API_SECRET それぞれに別の値を生成する
```

### Server: `Server/.env`

Git管理外。各自作成する(`Server/`直下、`Cargo.toml`と同じ階層)。

```env
DATABASE_URL=mysql://root@127.0.0.1:3306/atlas_dev
SERVER_ADDR=127.0.0.1:3000
BATTLE_SERVER_URL=http://127.0.0.1:5000
BATTLE_TOKEN_SECRET=<生成した値A>
INTERNAL_API_SECRET=<生成した値B>
```

`BATTLE_TOKEN_SECRET`・`INTERNAL_API_SECRET`が未設定だと、`cargo run`・`cargo test`とも起動時にpanicする。

### BattleServer: `dotnet user-secrets`

`.env`は読まないため、`dotnet user-secrets`(Development環境で自動的に読み込まれる、リポジトリ外に保存される)で設定する。

```bash
cd BattleServer
dotnet user-secrets set BATTLE_TOKEN_SECRET "<生成した値A>"
dotnet user-secrets set INTERNAL_API_SECRET "<生成した値B>"
dotnet user-secrets list   # 確認
```

`Server/.env`から値をそのまま写す場合(Git Bash):

```bash
cd BattleServer
for key in BATTLE_TOKEN_SECRET INTERNAL_API_SECRET; do
  dotnet user-secrets set "$key" "$(grep "^$key=" ../Server/.env | cut -d= -f2-)"
done
```

user-secretsは`dotnet run`(launchSettingsの`http`プロファイル、Development環境)で読み込まれる。
`--no-launch-profile`等でDevelopment以外で起動する場合は、同名の環境変数で渡す。

## 4. ローカルで全体を動かす(起動順)

```bash
# 1) MySQL起動+マイグレーション(初回・DBを作り直したとき)
cd Server
make setup                          # 2回目以降は make up だけでよい
cargo run --bin seed_master_data    # マスタデータをDBへ投入(マスタを更新したとき)

# 2) Server
cargo run                           # http://127.0.0.1:3000

# 3) BattleServer(別ターミナル)
cd BattleServer
dotnet run                          # http://127.0.0.1:5000

# 4) Client
#    Unity Hubで Client/AtlasUnityProject を開き、Bootstrapシーンから再生
```

Serverはマスタを起動時に1回だけ読み込む。`seed_master_data`の後はServerを再起動する。

## 5. コンポーネント別

### Server(Rust) — 作業ディレクトリ `Server/`

詳細: [Server/CLAUDE.md](Server/CLAUDE.md)、初回構築: [Server/docs/setup.md](Server/docs/setup.md)

```bash
make up / make down / make restart   # MySQLコンテナの起動・停止
make mysql                           # mysqlクライアントで接続(root, パスワードなし)
make migrate / make migrate-revert   # マイグレーション
make db-reset                        # DBを空から作り直す

cargo run                            # APIサーバー
cargo test                           # テスト(MySQLコンテナの起動が必要)
cargo run --bin export_openapi       # Shared/api/openapi.yaml を再生成(api-codegenの入力)
```

- `sqlx`はMySQLに接続できないと`cargo build`自体が失敗する。先に`make up`する
- ログレベル: `RUST_LOG="info,Server=debug"`でリクエスト/レスポンスのボディ、
  `RUST_LOG="info,Server=debug,sqlx::query=debug"`でSQLも出る
- `cargo run`中は`target/debug/Server.exe`がロックされ、`cargo test`等のビルドが
  「アクセスが拒否されました」で失敗する。サーバーを止めるか、`CARGO_TARGET_DIR`で別の出力先を指定する

### BattleServer(C#/MagicOnion) — 作業ディレクトリ `BattleServer/`

```bash
dotnet run                           # http://127.0.0.1:5000(HTTP/2のみ・非TLS、開発用)
dotnet test BattleServer.slnx        # 自動テスト(サーバーをプロセス内で起動するため、MySQL・Serverは不要)
```

- `Atlas.BattleCore`(`Shared/BattleCore/`、Unityのローカルパッケージ)は
  `BattleServer/BattleCore/Atlas.BattleCore.csproj`でDLLとしてビルドする。**`Shared/BattleCore/`の中にcsprojを置いたり、
  そこで`dotnet build`したりしない**(`bin/`・`obj/`がパッケージ内にでき、UnityがそのDLLを取り込んでCS1704になる)
- ステータス・技は現在ダミーデータ(マスタデータの配置待ち。`Shared/docs/progress.md`参照)

### Client(Unity) — `Client/AtlasUnityProject/`

- Unity 6000.6.2f1で開く。コーディング規約は[Client/AtlasUnityProject/CLAUDE.md](Client/AtlasUnityProject/CLAUDE.md)
- ServerのURLは`Assets/Scripts/DI/RootLifetimeScope.cs`の`ApiBaseUrl`(`http://127.0.0.1:3000`固定)。
  BattleServerのURLはマッチング成立時にServerから受け取るため、Client側の設定は不要
- 通信ログはUnity Consoleの`[API] --> ...` / `[API] <-- ...`(Editor・開発ビルドのみ)
- `Atlas.BattleCore`は`Packages/manifest.json`から`Shared/BattleCore`をローカルパッケージとして参照している

## 6. コード生成

どちらも生成物は手で編集しない(再生成で上書きされる)。

### マスターデータ(master-data-pipeline) — スキーマ/CSVを変えたとき

入力は`Shared/master-data/`(schema・CSV)。作業ディレクトリは`master-data-pipeline/`。

```bash
cd master-data-pipeline
./run.sh normalize-csv && ./run.sh resolve-enums && ./run.sh validate   # 前処理・検証
./run.sh server    # Server向け(Server/src/master/generated/*.rs, Server/master_data/*.json)
./run.sh client    # Client向け(Models/Enums/Loader/masterdata.bytes)
```

- Server向けを更新したら、`cargo run --bin seed_master_data`でDBへ投入してServerを再起動する
- BattleServer向け(`./run.sh realtime`)は配置先(`config.yaml`の`realtime_*_dest_dir`)が未設定のため、まだ使わない
- ツール本体を変更したときだけ`./build.sh tools`が必要

### API(api-codegen) — ServerのAPI(handler/DTO)を変えたとき

```bash
cd Server && cargo run --bin export_openapi   # Shared/api/openapi.yaml を更新
cd ../api-codegen
dotnet run -- generate                         # DTO・APIクライアントを生成
dotnet run -- copy                             # Client側(Assets/Scripts/Domain.Api/)へ配置
```

OpenAPIの`nullable`には対応しない(Rust側で`Option<T>`をレスポンスに含めない)。

## 7. トラブルシューティング

| 症状 | 原因・対処 |
|---|---|
| `cargo build`/`cargo test`が失敗する(DB接続エラー) | MySQLコンテナが起動していない → `make up` |
| `cargo test`等で`Server.exe`の削除が「アクセスが拒否されました」 | `cargo run`中のサーバーがファイルをロックしている → 停止するか`CARGO_TARGET_DIR`を変える |
| BattleServerが`BATTLE_TOKEN_SECRET must be set` / `must be at least 32 bytes`で起動しない | user-secrets(または環境変数)が未設定・短すぎる → 「3. 共通の設定」 |
| 対戦に入れない(`JoinAsync`が`InvalidToken`) | `BATTLE_TOKEN_SECRET`がServerとBattleServerで違う、またはトークン取得から30秒以上経っている |
| 対戦結果がDBに記録されない | BattleServerのログを確認。`INTERNAL_API_SECRET is not set` → BattleServer側が未設定。`status=401` → 値の食い違い。報告が届かなかった対戦は、1時間後にServerの定期処理で`aborted`になる |
| Serverが`battle_matches`の`aborted`等でDBエラーになる | ローカルDBのマイグレーションが古い → `make migrate` |
| UnityでCS1704(`Atlas.BattleCore`の重複) | `Shared/BattleCore/`に`bin/`・`obj/`ができている → `.meta`ごと削除する(上記「BattleServer」参照) |
| マスタを更新したのにServerに反映されない | `seed_master_data`後にServerを再起動していない |
