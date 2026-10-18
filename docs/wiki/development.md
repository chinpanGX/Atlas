---
title: "開発環境ガイド"
status: current
area: root
related: [overview, index]
code: [Makefile, Server/Makefile, Server/docker-compose.yml, Server/Dockerfile, BattleServer/Dockerfile, deploy]
updated: 2026-10-18
---

# 開発環境ガイド

Atlasの開発者(Client / Server / BattleServer)が共通で行う設定と、よく使うコマンドをまとめたもの。
設計の背景は[README.md](../../README.md)とこの wiki([概要](overview.md))、各コンポーネント固有の詳細は下記リンク先を参照。

## 全体像

```
Unity Client ──gRPC──▶ Server(Rust)           gRPC http://127.0.0.1:50052 / 内部API http://127.0.0.1:3000
     │                    │  ▲
     │                    ▼  │ POST /internal/battle/*(パーティ・選出個体の取得、対戦結果)
     │                 MySQL(Docker)            127.0.0.1:3306 / DB: atlas_dev
     │
     └──gRPC/StreamingHub──▶ BattleServer(C#/MagicOnion)  http://127.0.0.1:5000
```

| コンポーネント | ディレクトリ | 役割 | ポート |
|---|---|---|---|
| Client | `Client/AtlasUnityProject/` | Unityクライアント | — |
| Server | `Server/` | クライアント向けのgRPC(認証・プレイヤー。乗せ換え中)と、BattleServer向けの内部API(REST) | 50052(gRPC)/ 3000(内部API) |
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
| make | — | Server / Unity向けのC#の生成 | `make --version`(Git Bashには無いので導入が必要。下記「makeの導入」参照) |
| sqlx-cli | — | Server | `sqlx --version`(`cargo install sqlx-cli --no-default-features --features mysql`) |
| .NET SDK | 10 | BattleServer / master-data-pipeline / Unity向けのC#の生成(`make proto-gen`) | `dotnet --version` |
| Python | 3.10以上 | master-data-pipeline | `python --version`(`pip install -r master-data-pipeline/requirements.txt`) |

Client担当でも、実際のサーバーにつないで動かすならServer・BattleServerを起動できる状態にしておく。

### 推奨の開発ツール

必須ではないが、このプロジェクトは次のツールで開発している。

| ツール | 使う人 | 用途 |
|---|---|---|
| JetBrains Rider | Client / BattleServer | C#のIDE。リポジトリ直下の`.editorconfig`の命名ルールがそのまま警告になる |
| JetBrains RustRover | Server | RustのIDE |
| Postman / grpcurl | Server | gRPCのAPIの動作確認(開発ビルドのサーバーはgRPCリフレクションが有効で、`.proto`無しで呼べる)。Unityでは`Window > VolTech gRPC > gRPC Monitor`で通信を見られる |

### makeの導入(Git Bash)

Git Bashには`make`が含まれていないため、次のどれかで入れる。入れた後はGit Bashを開き直す。

- Chocolatey(推奨、管理者権限のPowerShell): `choco install make`
- Scoop: `scoop install make`
- 手動: [ezwinports](https://sourceforge.net/projects/ezwinports/files/)の`make-4.x-without-guile-w32-bin.zip`を解凍し、
  `bin/make.exe`を`C:\Program Files\Git\usr\bin\`にコピーする

## 2. リポジトリの取得

```bash
git clone --recurse-submodules <リポジトリURL>
# 既にcloneしている場合
git submodule update --init --recursive
```

`master-data-pipeline` / `UnityScreenNavigator` はサブモジュール([.gitmodules](../../.gitmodules))。
Supplement(自作の共通ユーティリティ)はサブモジュールにせず、Clientの`manifest.json`からgitのURLで参照している。
`UnityScreenNavigator`(画面遷移ライブラリのフォーク)はClientの`manifest.json`から`file:`で参照しているため、
サブモジュールを取得しないとUnityプロジェクトを開けない。フォークを直したときは、フォーク側でコミット・pushしてから
Atlas側でサブモジュールの参照先コミットを更新してコミットする

## 3. 共通の設定(シークレット・環境変数)

**ServerとBattleServerで同じ値を設定する必要があるシークレットが2つある。** 値が食い違うと、エラーにならずに
「対戦に入れない」「結果が記録されない」という形で表面化するので注意する。

| 変数 | 設定先 | 用途 | 注意 |
|---|---|---|---|
| `BATTLE_TOKEN_SECRET` | Server・BattleServer(**同じ値**) | `battleToken`(JWT, HS256)の署名・検証 | **32バイト以上**(BattleServerは未満だと起動時にエラー)。食い違うと`JoinAsync`が全て`InvalidToken`になる |
| `INTERNAL_API_SECRET` | Server・BattleServer(**同じ値**) | BattleServer→Serverの内部API(`X-Internal-Secret`ヘッダー) | 食い違うと結果記録が`401`になる。BattleServer側が未設定だと結果を送らず警告ログのみ |
| `DATABASE_URL` | Server | MySQL接続先 | 例: `mysql://root@127.0.0.1:3306/atlas_dev` |
| `GRPC_ADDR` | Server | クライアント向けのgRPCの待受アドレス | 省略時`127.0.0.1:50052`。変えたらClientの`RootLifetimeScope.GrpcAddress`も合わせる |
| `SERVER_ADDR` | Server | BattleServer向けの内部APIの待受アドレス | 省略時`127.0.0.1:3000`。変えたらBattleServerの`API_SERVER_URL`も合わせる |
| `SERVER_STATUS_PATH` | Server(任意) | メンテナンス・最低バージョン・ゲーム内の日付が変わる時刻の設定ファイル | 省略時`config/server_status.toml`(`Server/`から)。5秒ごとに読み直すので、書き換えれば再起動しなくても効く |
| `BATTLE_SERVER_URL` | Server | マッチ成立時にクライアントへ返すBattleServerのURL | 省略時`http://127.0.0.1:5000` |
| `ENABLE_DEBUG_API` | Server(任意) | 開発用のAPI(`DebugService`: ジェムの付与・ランダム編成)を登録するか | `true`・`1`で登録、それ以外の値で登録しない。省略時はデバッグビルド(`cargo run`)だけ登録する。ボットはランダム編成を使う |
| `API_SERVER_URL` | BattleServer | 対戦結果の送信先(Server) | 省略時`http://127.0.0.1:3000` |
| `LOG_FORMAT` | Server(任意) | ログの形式 | `json`(省略時。1行1件)か`text`(開発中にターミナルで読むとき) |
| `RUST_LOG` | Server(任意) | ログのレベルのフィルター | 省略時`info`。下記「Server」参照 |
| `LOG_FIELD_NAMES` | Server(任意) | JSONの項目名 | `standard`(省略時)か`gcp`(Google Cloud Loggingの`severity`など) |
| `OTEL_SERVICE_NAME` | Server(任意) | ログのどの行にも付けるサービス名 | 省略時`atlas-api` |
| `APP_ENV` | Server(任意) | ログのどの行にも付ける環境名 | 省略時`local` |
| `GAME_LOG_SINK` | Server(任意) | ゲームログ(所持データの変化の履歴)の出力先 | `database`(省略時。`game_logs`テーブル)か`stdout`(標準出力のJSON)。知らない値なら起動しない |

### シークレットの生成

```bash
openssl rand -hex 32   # 64文字。BATTLE_TOKEN_SECRET / INTERNAL_API_SECRET それぞれに別の値を生成する
```

### Server: `Server/.env`

Git管理外。各自作成する(`Server/`直下、`Cargo.toml`と同じ階層)。見本の`Server/.env.example`をコピーして作ってよい。

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

リポジトリ直下の`Makefile`で、下記1)〜3)(MySQL起動+マイグレーション+マスタデータの投入、Server、BattleServer)を1コマンドで行える。

```bash
make dev    # MySQL起動+マイグレーション → マスタデータの投入 → Server・BattleServerを並列起動(Ctrl+Cで停止)。コンテナのイメージも並行してビルドする
make stop   # Ctrl+Cで止まりきらなかったServer・BattleServerのプロセスを強制終了
make seed   # マスタデータをDBへ投入だけする(make devも毎回行う。起動中のServerには再起動するまで反映されない)
make images # コンテナのイメージ(atlas-api・atlas-battle)をビルドするだけ
```

- BattleServerの共有シークレット(`BATTLE_TOKEN_SECRET`・`INTERNAL_API_SECRET`)は`Server/.env`の値を環境変数で渡すため、
  `make dev`ではBattleServerのuser-secrets設定は不要
- BattleServerは.NET 10 SDK(既定`C:/Program Files/dotnet/dotnet.exe`)で起動する。別の場所なら`make dev DOTNET=<パス>`

個別に起動する場合:

```bash
# 1) MySQL起動+マイグレーション(初回・DBを作り直したとき)
cd Server
make setup                          # 2回目以降は make up だけでよい
cargo run --bin seed_master_data    # マスタデータをDBへ投入(マスタを更新したとき)

# 2) Server(起動時に基盤とAtlasのマイグレーションを流す)
cargo run                           # gRPC http://127.0.0.1:50052 / 内部API http://127.0.0.1:3000

# 3) BattleServer(別ターミナル)
cd BattleServer
dotnet run                          # http://127.0.0.1:5000

# 4) Client
#    Unity Hubで Client/AtlasUnityProject を開き、Bootstrapシーンから再生
```

Serverはマスタを起動時に1回だけ読み込む。`seed_master_data`の後はServerを再起動する。

### コンテナ(クラウドに置くもの)

普段の開発は`make dev`(Serverは`cargo run`、BattleServerは`dotnet run`)で行い、コンテナはテスト用のクラウドのVMで動かすためだけに使う。
`cargo run`・`dotnet run`のほうがビルドが速く、デバッガもつなげるため。

| ファイル | 内容 |
|---|---|
| `Server/Dockerfile` | APIサーバーのイメージ(`atlas-api`)。マスタの投入(`seed_master_data`)も同じイメージに入れる(マスタのJSONはバイナリに埋め込み済み) |
| `BattleServer/Dockerfile` | バトルサーバーのイメージ(`atlas-battle`) |
| `deploy/compose.yml` | VM 1台で、MySQL・APIサーバー・バトルサーバーと、TLSを受け持つCaddyを動かす。外に開けるのはCaddyの80・443だけ |
| `deploy/Caddyfile` | ドメインごとに、APIサーバー・バトルサーバーへ渡す(TLSをここで終わらせ、中はTLS無しのHTTP/2) |
| `deploy/.env.example` | ドメイン・シークレットの見本 |

- イメージは`make dev`が並行してビルドする(`make images`だけでもよい)。どちらのDockerfileも、リポジトリのルートを起点にビルドする
  (`Shared/proto`などをServerとBattleServerの両方が使うため)。キャッシュが効くので、2回目からは変えたところだけをビルドし直す
- イメージのビルドに失敗しても、`make dev`のServer・BattleServerはそのまま動き続ける(エラーはターミナルに出る)
- VMでの起動:

```bash
cd deploy
cp .env.example .env                       # ドメイン・シークレットを埋める(.envはgitに入れない)
cp ../Server/config/server_status.toml .   # メンテナンス・最低バージョンの設定(gitに入れない)。先にコピーしないと、Dockerが同名のフォルダを作ってしまう
docker compose up -d --build
docker compose run --rm api seed_master_data   # マスタの投入。マスタを変えたら流し、docker compose restart api で読み直させる
```

- 開発用API(`DebugService`)は無効にしてある。BattleBotは開発用APIでパーティを組むため、VMのサーバーには使えない

### 対戦(実サーバー)の確認方法

ClientはマッチングをServer、対戦をBattleServerへ接続して行う(サーバー無しで対戦する仕組みは無い)。
相手は次のいずれかで用意する。どの方法でもServer・BattleServerを先に起動しておく。

| 方法 | 手順 | 向いている用途 |
|---|---|---|
| A. サーバーのボット | UnityでBattleボタンを押して10秒待つ(相手が来なければ、BattleServerの中のボットが相手になる) | 普段の動作確認(Editor 1つでよく、何も起動し足さなくてよい) |
| B. 自動対戦のクライアント | 別ターミナルで BattleBot を起動しておき(下記)、UnityでBattleボタンを押す | 本物のクライアントと同じ経路の相手で確かめたいとき |
| C. Multiplayer Play Mode | Window > Multiplayer > Multiplayer Play Mode で追加のEditorインスタンス(Player 2)を有効にし、両方でBattleボタンを押す | 両方の画面を見ながら確認したいとき |
| D. ビルド+Editor | スタンドアロンビルドを起動し、EditorとそれぞれでBattleボタンを押す(ビルドを複数起動するなら `-saveSlot 2` 等を付ける) | 最終確認 |

- B〜Dで人どうし(またはBattleBot)を組ませたいときは、両方が10秒以内に並ぶ。遅れるとサーバーのボットと組まれる

- BattleBotは`BattleServer/`で、.NET 10 SDKを明示して起動する: `"C:/Program Files/dotnet/dotnet.exe" run --project BattleBot -- --loop`
  (素の`dotnet`はGit BashのPATHでUnity付属の.NET 8 SDKが先に見つかり、`NETSDK1045`で失敗することがある)
- ボットの引数: `--loop`(対戦が終わるたびに再びマッチングに並ぶ)、`--count N`(N体同時に動かす。`--count 2`でボット同士が
  対戦し、Unity無しでマッチング・BattleServer・結果記録を通しで確認できる)、`--api <URL>`(APIサーバーのgRPC。既定`http://127.0.0.1:50052`)。
  ボットは起動のたびに新しいプレイヤーを作る(DBにボットのプレイヤーが増える)。パーティは開発用API`DebugService/RandomizeParty`で
  pachimonマスタから重複なしの6体・技も候補技からランダムに組み直す。選出は、BattleServerから届くパーティ
  (`OnSelectionStart`)からランダムに3体を選ぶ
- ボットのAPIサーバーのクライアントは、ビルドのときに`.proto`から生成する。基盤のデバイス登録の`.proto`は、初回のビルドで
  基盤のタグの版を`BattleBot/obj/voltech-grpc-src/`に取ってくる(gitとネットワークが要る)
- B・Cでは、同じPCで2人分を動かすとセーブデータ(アカウント情報)が同じになり、自分自身とマッチングしようとして
  しまうため、インスタンスごとに保存先を分けている(`Assets/Scripts/DI/SaveDataDirectory.cs`)。Editor本体は既定の`SaveData`、
  Multiplayer Play Modeの追加インスタンスは`SaveData/VP_<id>`、ビルドは`SaveData/Build`(`-saveSlot N`で`SaveData/Build_N`)
- マッチング成立からBattleServerへの接続までの期限は30秒(`battleToken`の有効期限)

## 5. コンポーネント別

### Server(Rust) — 作業ディレクトリ `Server/`

詳細: [Server/CLAUDE.md](../../Server/CLAUDE.md)、cargoのコマンド: [notes/cargo-commands.md](../notes/cargo-commands.md)

```bash
make up / make down / make restart   # MySQLコンテナの起動・停止
make mysql                           # mysqlクライアントで接続(root, パスワードなし)
make migrate / make migrate-revert   # Atlasのマイグレーション(基盤のテーブルはサーバーの起動時に作られる)
make db-reset                        # DBを空から作り直す(Clientの保存したアカウント情報も消す。下記)

cargo run                            # APIサーバー
cargo test                           # テスト(MySQLコンテナの起動が必要)
```

- ビルドにMySQLは要らない(SQLは実行時に組み立てる`sqlx::query`で、コンパイル時に検査するマクロは使っていない)。
  `cargo run`・`cargo test`は実行時にMySQLへつなぐため、先に`make up`する
- ログは標準出力に1行1件のJSONで出る。ターミナルで読むときは`LOG_FORMAT=text`にする。
  `RUST_LOG="info,sqlx::query=debug"`で実行したSQLも出る。RPCごとのアクセスログには`request_id`・`player_id`が付くので、
  クライアントの`x-request-id`やプレイヤーで探せる
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
- 通信契約(`IBattleHub`/`IBattleHubReceiver`/Payload)は`Shared/BattleContracts/`(Unityのローカルパッケージ)にあり、
  `BattleServer/BattleContracts/Atlas.BattleContracts.csproj`で同じ方式(パッケージ外のcsproj)でビルドする
- 選出個体のステータス・技は、Serverの内部API(`POST /internal/battle/loadouts`)で取得した所持データと、
  BattleServerのマスタ(`MasterData/`)から組み立てる。Serverが起動していない・`INTERNAL_API_SECRET`が
  Serverと一致しないと選出に失敗する(`INTERNAL_API_SECRET`が未設定だとBattleServer自体が起動しない)
- `BattleBot/`: 開発用の自動対戦のクライアント(上記「対戦(実サーバー)の確認方法」参照)
- `.slnx`はUnity付属の.NET 8 SDKでは読めない。PATH上の`dotnet`がUnity付属のものになっている場合は
  .NET 10 SDK(`"C:/Program Files/dotnet/dotnet.exe"`等)で実行する

### Client(Unity) — `Client/AtlasUnityProject/`

- Unity 6000.6.2f1で開く。コーディング規約は[Client/AtlasUnityProject/CLAUDE.md](../../Client/AtlasUnityProject/CLAUDE.md)
- ServerのURLは`Assets/Scripts/DI/RootLifetimeScope.cs`の`GrpcAddress`(`http://127.0.0.1:50052`固定)。
  BattleServerのURLはマッチング成立時にServerから受け取るため、Client側の設定は不要
- 通信ログはUnity Consoleの`[Api] → ...` / `[Api] ← ...`(Editor・開発ビルドのみ)。`Window > VolTech gRPC > gRPC Monitor`で
  リクエスト・レスポンスのヘッダーとBody(JSON)を1件ずつ見られる
- Bootstrapシーンから再生するとタイトルが出る。「Start」でサインイン(初回はデバイス登録・サインアップ)してホームへ進む
- アカウント情報(`device_id`等)は、接続先ごとに暗号化して端末に保存する(`account-<接続先>`)。**ServerのDBを作り直したら、
  保存先のフォルダ(Editorは`Application.persistentDataPath`の`SaveData`)のアカウント情報のファイルを消す**。
  消さないとトークンを発行できず、タイトルに戻り続ける(gRPC基盤の未解決の課題)
- `Atlas.BattleCore`は`Packages/manifest.json`から`Shared/BattleCore`を、`Atlas.BattleContracts`は
  `Shared/BattleContracts`をローカルパッケージとして参照している
- フォント(`Assets/Addressables/Fonts/NotoSansJP-Medium SDF.asset`)は文字を焼き込んだStaticアセット。
  Dynamicにすると、Play中にアセットが書き換わり、Multiplayer Play Modeの追加インスタンスで文字化けする。
  焼き込むのは、JIS第1水準・ASCII・マスターデータCSV・`Assets`内のプレハブ/シーン/C#に出てくる文字。
  **新しい文字(第2水準漢字等)をUIやマスターデータに追加したら、`Tools > Bake Static Font`で焼き直す**(焼き直さないと□になる)

## 6. コード生成

どちらも生成物は手で編集しない(再生成で上書きされる)。

### マスターデータ(master-data-pipeline) — スキーマ/CSVを変えたとき

入力は`Shared/master-data/`(schema・CSV)。作業ディレクトリは`master-data-pipeline/`。

```bash
cd master-data-pipeline
./run.sh normalize-csv && ./run.sh resolve-enums && ./run.sh validate   # 前処理・検証
./run.sh server    # Server向け(Server/src/master/generated/*.rs, Server/master_data/*.json)
./run.sh client    # Client向け(Models/Enums/Loader/masterdata.bytes)
./run.sh realtime  # BattleServer向け(BattleServer/MasterData/ へ同じ生成物をコピー。clientの後に実行)
```

- Server向けを更新したら、`make dev`で起動し直す(投入も行う)。個別に起動しているなら、`cargo run --bin seed_master_data`でDBへ投入してServerを再起動する
- BattleServer向けを更新したら、`dotnet test BattleServer/Tests/BattleServer.Tests.csproj`で読み込めることを確認してBattleServerを再起動する
- ツール本体を変更したときだけ`./build.sh tools`が必要

### API(gRPC) — `Shared/proto/`の`.proto`・`Shared/errors.toml`を変えたとき

```bash
make proto-gen    # .proto → Unity向けのC#(メッセージ型・gRPCクライアント・ラッパー)。buf lintも行う
make errors-gen   # errors.toml → Unity向けのC#のエラーコード(AtlasErrors)
```

- リポジトリ直下で実行する。初回は、gRPC基盤の同じタグ(`Makefile`の`VOLTECH_TAG`)から生成のツール(buf・protoc・
  protocプラグイン)を`.tools/`に取ってくる。失敗したら`.tools/voltech-grpc-src/`を消してやり直す
- 出力先は`Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Grpc/Generated/`(コミットする)
- Server(Rust)は、ビルドのときに`build.rs`が同じ`.proto`・`errors.toml`から生成する(コミットしない)
- PATHで先にUnity付属の.NET 8 SDKが見つかるPCでは、`Makefile`が既定で`C:/Program Files/dotnet/dotnet.exe`を使う。
  別の場所なら`local.mk`(コミットしない)に`DOTNET := <パス>`を書く
- gRPC基盤のタグを上げるときは、`Makefile`の`VOLTECH_TAG`・`Server/Cargo.toml`・Clientの`manifest.json`の3か所をそろえる

## 7. CI(GitHub Actions)

`develop`へのpushとPRで、変更したパスに関係するものだけが走る。生成物を作ってコミットすることはしない
(生成物は、それを使うコードと同じコミットに手元で入れる)。

| workflow | 内容 |
|---|---|
| `server.yml` | `make fmt-check`・`cargo clippy --all-targets`・`cargo test`。MySQLはActionsの`services`で起動し、シークレットはダミーの値を渡す |
| `battle-server.yml` | `dotnet test BattleServer.slnx` |
| `proto.yml` | `buf lint`。PRではマージ先と比べる`buf breaking`も行う |
| `client-windows.yml` | ClientのWindowsビルド(IL2CPP)。時間がかかるので、手で実行したとき(Actionsの画面の`Run workflow`。ワークフローが`main`に入ってから出る)と、このワークフロー自体を変えたときだけ走る。できたビルドはArtifact(`Atlas-Windows`、7日で消える)に置く |

`client-windows.yml`は[GameCI](https://game.ci/)の`game-ci/unity-builder`で、Unityのバージョンに合うDockerイメージ
(`unityci/editor`の`windows-<バージョン>-windows-il2cpp`)を使ってビルドする。IL2CPPのWindowsビルドはWindowsの上でしか
作れないため、Windowsのランナーを使う。`windows-latest`(Windows Server 2025)のイメージではDockerのデーモンが動いていないため、
`windows-2022`に固定している。

- Unityのライセンスを、リポジトリのSecretsに`UNITY_LICENSE`(Hubでアクティベートしたときにできる`C:\ProgramData\Unity\Unity_lic.ulf`の中身)・
  `UNITY_EMAIL`・`UNITY_PASSWORD`で入れておく。Personalでもアクティベートが要る
- Unityのバージョンを上げたら、そのバージョンのイメージがあるかを確かめる(新しいバージョンはイメージが出るまで時間がかかる)
- Addressablesは、プレイヤーのビルドと一緒にビルドされる(Addressablesの設定が「Preferencesに従う」で、その既定値が「一緒にビルドする」)
- Unityのパッケージとサブモジュールは、CIで鍵なしで取れるようにHTTPSで取る(`manifest.json`のgit URLはHTTPSで書く)
- ビルドのClientの接続先はコードの`GrpcAddress`のまま(`127.0.0.1`)。手元で動かすサーバーにつながる

## 8. トラブルシューティング

| 症状 | 原因・対処 |
|---|---|
| `cargo run`/`cargo test`が失敗する(DB接続エラー) | MySQLコンテナが起動していない → `make up` |
| `cargo test`等で`Server.exe`の削除が「アクセスが拒否されました」 | `cargo run`中のサーバーがファイルをロックしている → 停止するか`CARGO_TARGET_DIR`を変える |
| BattleServerが`BATTLE_TOKEN_SECRET must be set` / `must be at least 32 bytes`で起動しない | user-secrets(または環境変数)が未設定・短すぎる → 「3. 共通の設定」 |
| 対戦に入れない(`JoinAsync`が`InvalidToken`) | `BATTLE_TOKEN_SECRET`がServerとBattleServerで違う、またはトークン取得から30秒以上経っている |
| 対戦結果がDBに記録されない | BattleServerのログを確認。`INTERNAL_API_SECRET is not set` → BattleServer側が未設定。`status=401` → 値の食い違い。報告が届かなかった対戦は、1時間後にServerの定期処理で`aborted`になる |
| Serverが起動時のマイグレーションで失敗する | 適用済みのマイグレーションの中身が変わった(チェックサムの不一致)→ `make db-reset`でDBを作り直す |
| Unityでタイトルの「Start」を押すと、すぐタイトルに戻る | ServerのDBを作り直したのに、端末のアカウント情報が残っている → 上記「Client」のとおり、アカウント情報のファイルを消す |
| Unityで通信エラー(サーバーにつながりませんでした) | Serverが起動していない、または`GRPC_ADDR`とClientの`GrpcAddress`が違う |
| UnityでCS1704(`Atlas.BattleCore`の重複) | `Shared/BattleCore/`に`bin/`・`obj/`ができている → `.meta`ごと削除する(上記「BattleServer」参照) |
| マスタを更新したのにServerに反映されない | `seed_master_data`後にServerを再起動していない |
