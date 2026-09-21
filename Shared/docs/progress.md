# 開発進捗まとめ(マスターデータ基盤〜Server API)

このドキュメントは、マスターデータパイプラインの導入からパチモン・技マスタの投入、
Server側API実装状況までの作業内容・進捗・残タスクを整理したものです。

## 1. マスターデータ基盤(master-data-pipeline)

### やったこと

- `master-data-pipeline`(別リポジトリ、Atlas配下にclone)を使ってマスターデータ変換基盤を導入
- Enumスキーマに `key`(生成コード上の識別子)を `name`(スプレッドシート入力用の表示名)と
  分離して追加。`name`に日本語を入れても生成コードが壊れないようにした
- Rustの予約語(`type`等)と列名が衝突するとコンパイルエラーになる問題を発見。
  ツール側は直さず、列名を`move_type`のように変更して回避する方針とし、READMEに
  予約語一覧と回避策を明記
- READMEを全面改訂
  - 古い「Phase1未実装」という誤った注記を削除
  - Getting Started・前提ツール・既知の制約(nullable非対応、複合PK非対応、
    `copy_destinations`のプレースホルダ危険性、`copy-loader`のclient/realtime同時書き込み等)を追記
- **リポジトリ管理方針の確立**: `master-data-pipeline`は別`.git`を持つ独立リポジトリのため、
  Atlas固有のスキーマ/データをそちらにコミットすると汎用ツールが汚染されると判明。
  → `config.yaml`の参照先(`tables_dir`/`enums_dir`/`csv_dir`)を`Shared/master-data/`に向け、
  **定義データはAtlasリポジトリ側、ツール本体は汎用のまま**という構成に整理。
  `config.yaml`自体は`master-data-pipeline/.gitignore`に追加し追跡除外(`credentials/`と同様の扱い)

### 進捗

- [x] スキーマ定義(`schema/tables/*.yaml`, `schema/enums/*.yaml`)
- [x] CSVデータ配置(`Shared/master-data/csv/`)
- [x] クライアント向け生成・配置 → Unityプロジェクト作成後、`config.yaml`の配置先を
  `Client/AtlasUnityProject/Assets/`配下に更新し、`./run.sh copy-models && ./run.sh copy-loader
  && ./run.sh copy-client-bytes`で配置済み。型・Loader・Enumsは全て
  `Scripts/MasterData/`配下の単一アセンブリ(`Atlas.MasterData.asmdef`)にまとめている
  (Domain/Infrastructureへのレイヤー分割はMasterMemory/MessagePackのSource Generator制約上
  不可能と判明したため不採用、詳細はarchitecture.md「マスターデータ運用」参照)。
  `masterdata.bytes`は`StreamingAssets`ではなくAddressables経由で配布する方針のため
  `Assets/Addressables/MasterData/masterdata.bytes`に配置(Addressable Groupへのマーク付けは
  Unity Editor側の作業として別途必要)
- [x] サーバー向け生成・配置(`Server/src/master/generated/*.rs`, `Server/master_data/*.json`)
- [x] `cargo check`によるコンパイル確認
- [x] `config.yaml`の`realtime_loader_dest_dir`/`realtime_bytes_dest_dir`を、確定した配置先
  (`BattleServer/`、design/battle.md参照)に更新(プレースホルダ`__REALTIME_SERVER_NOT_YET_CREATED__`から変更)
- [ ] `BattleServer/`プロジェクト自体がまだ存在しないため、`copy-loader`/`copy-realtime-bytes`は
  引き続き実行できない(パスは正しいが対象ディレクトリが無い)

## 2. マスターデータ内容(パチモン・技)

### やったこと

gamewith.jp「ポケモンチャンピオンズ」のSS環境トップ18体(rarity=S)、および使用率ランキング
19-36位(rarity A/B/C各6体)のタイプ・種族値を参考値として流用。名称は
`design/architecture.md`の「pachimonは商標混同を避けるための独自名」方針に沿ってオリジナルに変更。

| テーブル | 件数 | 内容 |
|---|---|---|
| `pachimon` | 36 | `pachimon_id`は4桁(1001〜1036)。1001-1018がrarity=S、1019-1024がA、1025-1030がB、1031-1036がC |
| `move_groups` | 36 | 現状は1パチモン=1グループ(仮データ)。スキーマ上は将来の使い回しに対応 |
| `moves` | 38 | 各パチモン専用技36 + 全グループ共通の汎用技2(状態技は0件) |
| `move_group_moves` | 108 | 技グループ所属技の対応表(旧`move_group_master`。「Master」がMasterMemory/MasterDataLoaderと紛らわしいため改名)。代理キー`unique_id`を追加 |
| `type_chart` | 324 | 18タイプ×18タイプの全組み合わせ(第9世代準拠の標準タイプ相性)。`effectiveness`はENUM(`IMMUNE`/`NOT_VERY_EFFECTIVE`/`NORMAL`/`SUPER_EFFECTIVE`相当) |

### 残タスク

- [x] `rarity`がS以外(A/B/C)のパチモンが無い問題 → 使用率ランキング19-36位を元にA/B/C各6体を追加し解消(`scout_banners.rate_table`が機能する状態になった)
- [x] `type_chart`(タイプ相性表)未実装 → `effectiveness`ENUM(`type_effectiveness.yaml`)+代理キー
      `type_chart_id`で複合UNIQUE制約を回避する形でテーブル追加。全324行投入し
      `normalize-csv`/`resolve-enums`/`validate`/`generate-csharp`/`build-client`/`build-server`
      まで一通り実行して検証済み(`cargo check`も確認)。ENUM→倍率変換・複合タイプの掛け合わせは
      `Atlas.BattleCore`側にハードコードする方針(design/battle.md参照)
- [ ] 技の内容が最小限(専用技1+共通技2のみ、状態技0件、技の付け替え候補の広がりが薄い)
- [ ] `base_power`のNULL代替(0埋め)運用が実データ未検証(状態技が無いため)

## 3. Server(Rust/Axum)API実装状況

`design/`配下の機能別設計書(battle/scout/outgame)のAPI設計と突き合わせた現状。

### 実装済み(ルーティング登録・テストあり)

| メソッド | パス |
|---|---|
| POST | /devices |
| POST | /devices/authenticate |
| GET | /auth/verify |
| POST | /players |
| GET | /players/me |
| POST | /chat/send |
| GET | /chat/poll |
| GET | /scout/banners |
| POST | /scout/rolls |
| POST | /scout/rolls/{rollId}/select |

- 認証は`argon2`でdevice_secretをハッシュ化、IDは`ulid`
- テスト: `tests/{auth,chat,device,player,master_data,scout}_api_test.rs`(計36件)
- マイグレーション16本(devices/access_tokens/messages/players再構成/pachimonテーブル/型サイズ最適化/
  move_groups・moves・move_group_masterテーブル作成/pachimon→move_groups外部キー追加/
  players.gemsデフォルト値をoutgame.md設計(300)に整合/player_pachimon・player_pachimon_moves/
  scout_banners・scout_rolls/move_group_master→move_group_movesへのリネーム)
- `pachimon`マスタはDBに保存し、起動時にメモリキャッシュへ読み込む設計(`src/master/cache.rs`)。
  `move_group_moves`も同様にキャッシュ対象(`moves`/`move_groups`はスカウトのロジック上
  参照不要なためDB投入のみでキャッシュ対象外)。更新時は`cargo run --bin seed_master_data`で
  JSON→DBへUPSERTしてから再起動する運用
- スカウト(ガチャ)は`design/scout.md`の設計通り実装。`scout_banners`はmaster-data-pipeline対象外
  (専用バイナリ`cargo run --bin seed_scout_banners`で常設バナー1件をUPSERT)。候補10体の抽選は
  `rand`クレート(`WeightedIndex`)でレアリティを重み付き抽選→`pachimon`を等確率選出→IVsをランダム
  生成(0-31)→`move_group_moves.is_initial=TRUE`の技を初期技として確定、という流れ。gems減算は
  `pool.begin()`によるトランザクションで、offer(roll)作成・候補selectのDB更新はこのコードベースで
  初めての`Transaction`利用
- `player_pachimon`/`player_pachimon_moves`のモデル・テーブルを実装(outgame.md参照)。levelカラムは
  持たない(全パチモン固定レベル50、design/battle.md参照)

### 未実装

| セクション | 内容 |
|---|---|
| マッチング | `/battle/queue*` |
| 内部API | `/internal/battle/result` |
| パーティ編成・技の付け替え | `/players/me/pachimon`, `/players/me/party`, `PUT /players/me/pachimon/{id}/moves/{slot}`(outgame.md #8-10、スカウトでの初期取得とは別) |

## 4. クライアント / バトルサーバー / API連携

design/architecture.mdの全体構成(`Unity Client ←REST→ Rust/Axum`, `Unity Client ←gRPC/StreamingHub→ C#/MagicOnion`)
に対して、現状は以下の状態。

### Unity Client

Unityプロジェクトの体裁(`ProjectSettings/`, `Packages/`等)は作成済みで、利用ライブラリ一式を
導入しコンパイルが通る状態まで到達した(通信・UI・ゲームロジックの実装自体はまだこれから)。

- 利用ライブラリ(VContainer/UniTask/MagicOnion.Client/YetAnotherHttpHandler/MessagePack/
  MasterMemory/Supplement)を導入。詳細・導入経路はarchitecture.md「クライアント利用
  ライブラリ」参照
- MagicOnion.ClientやMasterMemory/MessagePackはUPM版が薄いラッパー(Source Generator DLLを
  含まない)だったため、NuGetForUnity経由で本体(NuGet版)を別途`Assets/Packages/`配下に手動配置
  (自動リストアがセッション内で不安定だったため、nupkgを直接展開する形を取った箇所がある)
- MasterMemory本体のSource Generatorが正しく動く配置を確認するまでに複数の誤った試行があった
  (Domain/Infrastructureへのアセンブリ分割+`InternalsVisibleTo`での回避 → 不採用、
  `GeneratedMessagePackResolver`の直接参照 → 不要と判明・`StandardResolver`が内部で自動的に
  含む)。最終的に単一アセンブリ(`Atlas.MasterData`)にまとめる方針に統一(architecture.md参照)
- `Supplement`(`chinpanGX/Supplement`)をgit submoduleとしてAtlas直下に追加し、Client側から
  ローカルパッケージ参照。AssetLoader(Addressables実装)にラベル指定ロード・進捗通知が
  無いことを確認済みで、追加を依頼中(Supplement側での対応待ち)
- R3(View↔Presenterのリアクティブ購読、battle.md参照)は**未導入**。対戦画面の実装着手時に追加する
- `uloop`(Unity CLI Loop、`io.github.hatayama.uloopmcp`)経由でEditor操作・コンパイル確認を
  自動化できる状態
- 未着手: 通信層の実装(MagicOnion StreamingHubクライアント・REST APIクライアントの実際の呼び出し)、
  UI、ゲームロジック、`Atlas.BattleCore`との連携(`MockBattleConnection`)

### Atlas.BattleCore(Shared/BattleCore/)

design/battle.md「バトルコアロジック(ダメージ計算・命中率)」「内部構造(Section / Event /
EventHandler)」に対応する共通ロジック本体を実装済み(Stage 0の骨組みからStage 1相当へ)。

- `BattleEngine.ProcessTurn`を唯一の公開エントリポイントとして、ターン処理Section
  (強制交代チェック→行動順決定→行動実行→交代/技効果→命中判定→ダメージ計算→瀕死チェック→
  技効果後処理)を実装。技効果後処理は`MoveHitEvent`/`IMoveHitEventHandler`によるEventフックの
  形にしてあり、現状反応するEventHandlerは0個(設計通り)
- ダメージ計算式(`DamageCalculator`)・行動順決定と命中判定(`TurnResolver`)は
  `IRandomSource`/`ITypeChart`経由で乱数・タイプ相性を注入する形で実装し、`Domain.MasterData`や
  UnityEngineには依存しない
- Unity EditModeテスト(`Tests/`配下、`DamageCalculatorTests`/`TurnResolverTests`/
  `BattleEngineTests`)を追加。シード固定の`FixedRandomSource`/`StaticTypeChart`で決定論的に
  検証(ダメージ計算式・STAB・タイプ相性(複合タイプの丸め含む)・急所・最低保証ダメージ1・
  行動順序・強制交代の強制/解消・全滅による決着等)。dotnet CLIでのビルド確認は行ったが、
  Unity Editor上での実行(EditModeテストランナー)自体は未実施(Unityプロジェクトが未構築のため)

呼び出し側(Client側`MockBattleConnection`、バトルサーバー側`IBattleHub`実装)は両方ともまだ
存在しないため、Atlas.BattleCoreは単体では動くが実際のバトル画面・通信からはまだ呼ばれていない。

### バトルサーバー(C#/MagicOnion)

design/battle.mdで「対戦中の判定をメモリ上で行う」役割として設計されているが、プロジェクト自体が
まだ存在しない(`BattleServer/`は`.gitkeep`のみ)。`IBattleHub`等のHub定義、選出フェーズは未着手。
ダメージ計算・行動順決定のロジック自体は`Atlas.BattleCore`側に実装済みのため、Hub実装時は
そちらを呼び出すだけで済む(上記「Atlas.BattleCore」参照)。

### APIサーバー ⇔ Unity Client 間のコード生成(API codegen)

`Shared/docs/feature-api-codegen.md`参照。①②とも実装済み。

- ①Rust handler→OpenAPI(`utoipa`):全handler(`device`/`auth`/`player`/`chat`/`scout`)に
  `#[utoipa::path(...)]`を付与し、`cargo run --bin export_openapi`(`Server/`)で
  `Shared/api/openapi.yaml`を生成できる。サーバー起動中は`/swagger-ui`でSwagger UIとしても
  確認できる(Postman代替)
- ②OpenAPI→Unity C#:プロジェクト非依存の共通ツール`api-codegen`(Atlasリポジトリ直下、
  `master-data-pipeline`とは別モジュール)として実装済み。`dotnet run -- generate`でDTO
  (`Dto/*.cs`)とタグ単位の通信APIクライアント(`Client/*ApiClient.cs`、`UnityWebRequest`を
  `UniTask`でラップ、VContainer非依存)を生成し、`dotnet run -- copy`で
  `Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Api/`へ配置する
  (Domainレイヤーではないため`Atlas.Infrastructure.Api`名前空間に配置)。詳細は
  `api-codegen/README.md`参照
- 残タスク: 生成したC#コードをUnityプロジェクト側で実際にコンパイル確認すること
  `nullable`・列挙型・クエリパラメータはapi-codegen未対応(現状のAPIには存在しないため後回し)

## 5. 残タスク一覧(統合)

| # | 内容 | 領域 |
|---|---|---|
| 1 | バトルサーバー(MagicOnion)プロジェクトの新規作成・`IBattleHub`等の実装一式 | バトルサーバー |
| ~~2~~ | ~~`moves`/`move_groups`/`move_group_master`のDBテーブル作成・`cache.rs`/`seed_master_data.rs`対応~~ → 完了(マイグレーション追加・`cache.rs`で`move_group_master`をキャッシュ・`seed_master_data`で3テーブルとも投入。詳細は上記「3. Server API実装状況」参照) | server/master-data |
| ~~3~~ | ~~`player_pachimon`(所持データ)のモデル・テーブル・API実装~~ → 完了(スカウトでの入手時に作成。パーティ編成・技の付け替えAPI自体は引き続き未実装、上記「未実装」参照) | server |
| ~~4~~ | ~~スカウトAPI(`/scout/*`)実装~~ → 完了(`GET /scout/banners`, `POST /scout/rolls`, `POST /scout/rolls/{rollId}/select`。結合テスト8件、詳細は上記「3. Server API実装状況」参照) | server |
| 5 | マッチングAPI(`/battle/queue*`)実装 | server |
| 6 | 内部API(`/internal/battle/result`)実装 | server |
| ~~7~~ | ~~`type_chart`(タイプ相性)の設計・実装~~ → 完了(schema/CSV投入・全ツールでの検証済み) | master-data/pipeline |
| 8 | 技の拡充(状態技、候補技の追加) | master-data |
| 9 | Unityクライアント側の実装一式 → 一部完了(プロジェクト構築・利用ライブラリ導入・コンパイル確認まで完了。通信層の実装・UI・ゲームロジックは未着手、上記「Unity Client」参照) | client |
| ~~10~~ | ~~API codegen(Rust handler→OpenAPI→Unity C#型)の導入~~ → 完了(`api-codegen`実装済み。Unity側での実コンパイル確認のみ、Unityプロジェクト本体の構築待ちで残タスク。詳細は上記「APIサーバー ⇔ Unity Client 間のコード生成」参照) | server/client連携 |
| ~~11~~ | ~~`scout_banners`用seedスクリプト(`seed_scout_banners`)の実装・常設バナー1件の投入~~ → 完了 | server |
| ~~12~~ | ~~`Atlas.BattleCore`(Shared/BattleCore/)の骨組み作成~~ → 完了(ダメージ計算・行動順決定・Section/Event/EventHandler本体の実装・EditModeテストまで完了。詳細は上記「Atlas.BattleCore」参照) | battle/shared |
