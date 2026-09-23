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
| `starter_party_slots` | 6 | 新規プレイヤーへ自動付与する固定スターター編成(1-6slot)。仮値としてrarity=Cの6体(1031-1036)を採用 |
| `items` | 1 | `item_id: 1`=ジェム(gems)のみ。`players.gems`列を廃止し所持数は`player_items`(下記「3. Server API実装状況」参照)で管理する |

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
| POST | /signup |
| POST | /sign-in |
| POST | /chat/send |
| GET | /chat/poll |
| GET | /scout/banners |
| POST | /scout/rolls |
| POST | /scout/rolls/{rollId}/select |
| POST | /edit/party |
| POST | /edit/pachimon_moves |
| POST | /battle/queue |
| DELETE | /battle/queue |
| GET | /battle/queue/status |

- 認証は`argon2`でdevice_secretをハッシュ化、IDは`ulid`
- テスト: `tests/{auth,battle,chat,device,player,master_data,scout}_api_test.rs`(計61件)
- マイグレーション23本(devices/access_tokens/messages/players再構成/pachimonテーブル/型サイズ最適化/
  move_groups・moves・move_group_masterテーブル作成/pachimon→move_groups外部キー追加/
  players.gemsデフォルト値をoutgame.md設計(300)に整合/player_pachimon・player_pachimon_moves/
  scout_banners・scout_rolls/move_group_master→move_group_movesへのリネーム/
  player_party_slotsテーブル作成・player_pachimon.party_slot列削除/
  starter_party_slotsテーブル作成/player_pachimon.ivs列削除/
  player_pachimon_movesへのULID主キー(player_pachimon_move_id)追加/
  itemsテーブル作成・player_itemsテーブル作成・players.gems列削除/
  master-data-pipelineのtype: int列をBIGINT→INTへ縮小(下記「今回発見したギャップ」参照))
- パーティ編成・技の付け替え(`GET/PUT /players/me/pachimon*`, `PUT /players/me/party`)を実装
  (outgame.md #8-10)。パーティ編成は当初`player_pachimon.party_slot`(nullable INT)属性として
  設計したが、①`api-codegen`が現状OpenAPIの`nullable`(`type: [T, 'null']`)に未対応で
  `TypeMapper`が例外停止すること、②割当自体をULIDで一意に参照できる方が他テーブルとの一貫性が
  高いこと、の2点から`player_party_slots`という独立テーブル(`party_slot_id`をPKに持つ)に設計変更した。
  未編成のslotは行が存在しないことで表現し、`nullable`を一切使わずに済む設計にしている。
  `PUT /players/me/party`は既存行を全削除してから指定分だけ新しいULIDで再作成する(全置き換え)。
  技の付け替えは`player_pachimon_moves`に対する`INSERT ... ON DUPLICATE KEY UPDATE`
  (既存slotの上書き・未使用slotへの新規セット両対応)
- `player_pachimon_moves`に専用のULID主キー(`player_pachimon_move_id`)を追加(既存は
  `UNIQUE(player_pachimon_id, slot)`のみでPK無しだった)。`player_party_slots`等と同様に
  「割当自体を独立したエンティティとしてULIDで参照できる」方針に揃える設計上の指摘を受けて修正。
  `INSERT ... ON DUPLICATE KEY UPDATE`の`UPDATE`句に`player_pachimon_move_id`を含めないことで、
  既存slotの付け替え時はIDが変わらず維持されるようにした(新規slotのみ新しいULIDを採番)。
  開発初期のためデータ移行はせずテーブルを作り直す形で対応
- `pachimon`マスタはDBに保存し、起動時にメモリキャッシュへ読み込む設計(`src/master/cache.rs`)。
  `move_group_moves`も同様にキャッシュ対象(`moves`/`move_groups`はスカウトのロジック上
  参照不要なためDB投入のみでキャッシュ対象外)。更新時は`cargo run --bin seed_master_data`で
  JSON→DBへUPSERTしてから再起動する運用
- スカウト(ガチャ)は`design/scout.md`の設計通り実装。`scout_banners`はmaster-data-pipeline対象外
  (専用バイナリ`cargo run --bin seed_scout_banners`で常設バナー1件をUPSERT)。候補10体の抽選は
  `rand`クレート(`WeightedIndex`)でレアリティを重み付き抽選→`pachimon`を等確率選出→
  `move_group_moves.is_initial=TRUE`の技を初期技として確定、という流れ。gems減算は
  `pool.begin()`によるトランザクションで、offer(roll)作成・候補selectのDB更新はこのコードベースで
  初めての`Transaction`利用
- `player_pachimon`/`player_pachimon_moves`のモデル・テーブルを実装(outgame.md参照)。levelカラムは
  持たない(全パチモン固定レベル50、design/battle.md参照)
- **個体値(IV)の概念を廃止**(ポケモンチャンピオンズ準拠)。`player_pachimon.ivs`列を削除し、
  スカウトのIVランダム生成ロジックも削除。実効ステータス計算式(design/battle.md)からIV項を
  除去し、種族値+努力値(`effort_values`、常に0固定)のみで決まる形にした
- **スターター編成機能を実装**。`starter_party_slots`マスタ(新規、6slot)を追加し、`POST /players`
  (プレイヤー作成)と同一トランザクションで、その内容をそのまま`player_pachimon`(初期技込み)・
  `player_party_slots`へ複製する。「pachimon_id+初期技から個体を1体生成する」処理はスカウトの
  `select_candidate`と共通のため、`player_pachimon_service::grant`という共有関数に切り出し、
  スカウト側もそちらを呼ぶようにリファクタした。専用の`reward_service`のような新しい層は作らず、
  既存の`player_pachimon_service`に集約する判断とした

- **`playerDiff`共通レスポンス形式・items/gems一般化・API再編を実装**(architecture.md
  「所持リソース設計(items / gems)」「APIレスポンス設計」、outgame.md、scout.md参照)。
  `master-data-schema-add`スキルで`items`マスタ(`item_id: 1`=ジェム)を追加し、
  `player_items`(`player_id`+`item_id`複合PK、`quantity`)テーブルへ`players.gems`列を移行。
  `POST /players`→`POST /signup`に改名して`200`(ボディ無し)に変更し、同一トランザクションで
  `player_items`へ初期ジェム(300)も付与するようにした。`GET /players/me`・
  `GET /players/me/pachimon`を廃止し、新設の`POST /sign-in`(`playerId`/`nickname`+
  `playerDiff`で所持データ全件を返す。`removed`は常に空のフルスナップショット)に統合。
  `PUT /players/me/party`→`POST /edit/party`、
  `PUT /players/me/pachimon/{id}/moves/{slot}`→`POST /edit/pachimon_moves`
  (識別子をパスパラメータからボディへ移動)に変更し、レスポンスを専用DTO
  (`SetPartyResponse`/`UpdateMoveResponse`)から`playerDiff`へ統一。`PlayerPachimonDto`から
  `moves`を除去し(ネスト解消)、`PlayerPachimonMoveDto`に`playerPachimonId`を追加して
  `pachimonMoveMap`という独立したリソース種別に分離した。スカウトの`POST /scout/rolls`は
  レスポンスの`gems`フィールドを`playerDiff.items`(消費後の残量)に、
  `POST /scout/rolls/{rollId}/select`は`{playerPachimonId,pachimonId,rarity}`を
  `playerDiff`(`pachimon`+`pachimonMoveMap`のみ更新、`rarity`は削除)に置き換えた。
  `player_items`の増減は`WHERE quantity >= ?`を使った条件付き`UPDATE`で原子的に不足検出する
  (旧`players.gems`の実装パターンを踏襲)。Client側は`api-codegen`で再生成したうえで、
  `POST /signup`/`POST /sign-in`の呼び出しと`playerDiff.items`の適用まで実装済み
  (下記「4. Unity Client」の「サインイン・`playerDiff`適用」参照)。スカウト・パーティ編成・
  技の付け替えの呼び出し側は未着手(残タスク#16)
- **`grant`/`set_party`の戻り値を拡張**。`player_pachimon_service::grant`は生成した技
  (`Vec<PlayerPachimonMove>`)も返すようにし(`select_roll`が`pachimonMoveMap.upserted`を
  組み立てるために必要)、`set_party`は削除前の`party_slot_id`一覧も返すようにした
  (`playerDiff.partySlots.removed`用)。呼び出し元が使わない場合は`_`で無視する
- **今回発見したギャップ(Server、master-data-pipelineの型幅の食い違い)**:
  `master-data-pipeline`の`type: int`は、C#側は`int`(32bit)を生成する一方、
  `server_codegen`(Rust)は`i64`(64bit)を生成していた(DB列も追随してBIGINT)。
  `architecture.md`/`outgame.md`の設計書側は元々`INT`と記述しており(`item_id`/`pachimon_id`/
  `move_id`列)、実装だけがBIGINTへ乖離していた形。`type: int`を32bit(C# `int`/Rust `i32`/DB
  `INT`)に統一し、64bit値が要る場合向けに新しい`type: long`(C# `long`/Rust `i64`/DB
  `BIGINT`)を追加。既存の`type: int`列(pachimon/moves/move_groups/move_group_moves/items/
  starter_party_slotsとそれを参照するplayer_pachimon等のFK列)はBIGINT→INTへ縮小する
  マイグレーションで揃えた。同じ理由で`enum`列(`PachimonType`/`Rarity`/`MoveCategory`等)も
  Rust側の生成`Serialize`/`Deserialize`実装が`i64`でシリアライズしていた(C#のenumはデフォルトで
  `int`裏付け型のため、ここも32bit/64bitの食い違いだった)。`server_codegen`が生成する
  `Serialize`/`Deserialize`実装を`i32`ベースに変更して揃えた(DB列は元々値域に合わせて
  `TINYINT UNSIGNED`にしていたため変更不要)。REST API DTO(`api/player.rs`/`api/scout.rs`)の
  `pachimonId`/`moveId`/`itemId`は互換性維持のため`int64`のまま据え置き、内部の`i32`との
  境界で明示変換している(Unity側`api-codegen`の再生成は未実施。API DTO側もint32へ揃えるかは
  今後の検討課題)
- **開発用の通信ログ(tracing)を導入**。`tracing`/`tracing-subscriber`/`tower-http`(`TraceLayer`)を
  追加し、`cargo run`したターミナルに method/uri/ステータス/処理時間を出す。`RUST_LOG`でDEBUGを
  有効にすると、`src/http_log.rs`のミドルウェアがリクエスト/レスポンスのボディ(JSONとエラーの
  text)を、`sqlx::query`が実行SQLを、同じリクエストのspan内に時系列で出す。`secretKey`/
  `accessToken`は伏字、`Authorization`ヘッダーは出力しない(使い方は`server-dev-env`スキル)。
  併せて`Cargo.toml`に`default-run = "Server"`を追加(`src/bin/`に複数binがあり、`cargo run`単体が
  「どのbinを実行するか決められない」エラーになっていた)
  - 検討したが採用しなかった案:
    - **mitmproxyコンテナでの中継**: Docker内からホストのAPIへ届かせるため`SERVER_ADDR`を
      `0.0.0.0`にする必要があり、同じネットワークの他の端末からもAPIに届くようになる。Unity側の
      接続先切り替えも必要。SQLやサーバー内部の処理との対応が取れない。MagicOnion(gRPC)には
      流用しにくい。通信の改ざん・再送が必要になったら、Windows上で`mitmweb --mode
      reverse:http://127.0.0.1:3000 -p 8080`をその場で起動すれば足りる
    - ~~**Unity側での`Debug.Log`出力**~~ → 後から実装した。`api-codegen`が生成する`ApiRequest`に
      ロガーの差し込み口(`ApiRequest.Logger`/`IApiRequestLogger`)を追加し、Atlas側の
      `UnityApiRequestLogger`(`Infrastructure/Api/`)が`[API] --> ...`/`[API] <-- ...`の形式で
      Unity Consoleへ出す。`secretKey`/`accessToken`は伏字、4096文字超は切り詰め。
      `RootLifetimeScope`で`Debug.isDebugBuild`(Editor・開発ビルド)の時だけ設定する
      (生成物を直接編集しない・汎用ツールに`Debug.Log`を固定で入れないため、差し込み口方式にした)
    - **APIサーバーのコンテナ化**: WindowsのDocker上ではRustのビルドが遅く、sqlxのコンパイル時
      チェックに`.sqlx`オフラインキャッシュ(`SQLX_OFFLINE`)の運用も必要で、Riderでのデバッグも
      しにくくなるため今は見送り。MagicOnionサーバーの着手時(サービス間通信が増える)か、
      デプロイ方式を決めるときに行う(残タスク#17)
- マッチングAPI(`POST/DELETE /battle/queue`, `GET /battle/queue/status`)を実装(design/battle.md
  「API仕様(マッチング、Rust側)」)。待機列は`AppState.matchmaking`(`Mutex<MatchmakingQueue>`)で
  プロセスメモリのみに保持し、`POST`時に同期的にペアリングする。`battle_token`は`jsonwebtoken`で
  発行するHS256のJWT(claims: `match_id`/`player_id`/`exp`、30秒)。共有シークレットは
  `.env`の`BATTLE_TOKEN_SECRET`(必須、未設定だと起動時panic)、返却するBattleServerのURLは
  `BATTLE_SERVER_URL`(省略時`http://127.0.0.1:5000`)。`GET /battle/queue/status`のレスポンスは
  `api-codegen`がnullable非対応のため`Option`を使わず、待機中は`matchId`等を空文字で返す。
  Unity向けDTOの再生成(`api-codegen`)はClient側のマッチング実装着手時に行う
- **今回発見したギャップ(Server、テスト環境)**: `sqlx::test`を使う結合テスト(`auth_api_test`等)が
  `failed to connect to setup test database: PoolTimedOut`で失敗する。上記の変更前のコードでも同じく
  失敗するため、今回の変更とは無関係。ローカル環境側の問題と思われ、原因は未調査

### 未実装

| セクション | 内容 |
|---|---|
| 内部API | `/internal/battle/result` |

## 4. クライアント / バトルサーバー / API連携

design/architecture.mdの全体構成(`Unity Client ←REST→ Rust/Axum`, `Unity Client ←gRPC/StreamingHub→ C#/MagicOnion`)
に対して、現状は以下の状態。

### Unity Client

Unityプロジェクトの体裁(`ProjectSettings/`, `Packages/`等)は作成済みで、利用ライブラリ一式を
導入しコンパイルが通る状態まで到達した。バトルMock・デバイス認証〜サインイン(`playerDiff`適用)の実装により、
通信層(REST APIクライアントの実呼び出し)にも着手している。Home⇔Battleのシーン分離・決着処理・
ジェム表示・UICamera/横向き対応まで実装し、design/battle.md「Stage 1」の画面まわりは一区切りついた
(下記「バトル画面」参照)。

- 利用ライブラリ(VContainer/UniTask/MagicOnion.Client/YetAnotherHttpHandler/MessagePack/
  MasterMemory/Supplement/R3/UnityScreenNavigator/ZeroMessenger)を導入。詳細・導入経路は
  architecture.md「クライアント利用ライブラリ」参照
- MagicOnion.ClientやMasterMemory/MessagePack/R3/ZeroMessengerはUPM版が薄いラッパー
  (Source Generator DLLを含まない、またはUPM未配布)だったため、NuGetForUnity経由で本体
  (NuGet版)を別途`Assets/Packages/`配下に手動配置(自動リストアがセッション内で不安定だった
  ため、nupkgを直接展開する形を取った箇所がある)。ZeroMessengerはSupplement本体
  (`Supplement.ZeroMessenger`)が要求するバージョン(1.0.4)に合わせ、Supplement側の
  `Assets/Packages/ZeroMessenger.1.0.4/`をそのままAtlas Client側へコピーする形で導入
- MasterMemory本体のSource Generatorが正しく動く配置を確認するまでに複数の誤った試行があった
  (Domain/Infrastructureへのアセンブリ分割+`InternalsVisibleTo`での回避 → 不採用、
  `GeneratedMessagePackResolver`の直接参照 → 不要と判明・`StandardResolver`が内部で自動的に
  含む)。最終的に単一アセンブリ(`Atlas.MasterData`)にまとめる方針に統一(architecture.md参照)
- `Supplement`(`chinpanGX/Supplement`)をgit submoduleとしてAtlas直下に追加し、Client側から
  ローカルパッケージ参照。AssetLoader(Addressables実装)にラベル指定ロード・進捗通知が
  無いことを確認済みで、追加を依頼中(Supplement側での対応待ち)
- R3(View↔Presenterのリアクティブ購読、battle.md参照)・UnityScreenNavigator(画面遷移、
  client-architecture.md参照)は導入済みで、Home/Battle画面のPresenterで実際に利用している
- `uloop`(Unity CLI Loop、`io.github.hatayama.uloopmcp`)経由でEditor操作・コンパイル確認を
  自動化できる状態
- 未着手: MagicOnion StreamingHubクライアントの実装(バトルサーバー自体が未着手のため)、
  Scout/Party/Chat各画面のUI・ゲームロジック。REST APIクライアントの実呼び出しと
  `Atlas.BattleCore`との連携(`MockBattleConnection`)は下記の通り一部着手済み

### クライアントアーキテクチャ設計(画面遷移・DI・Connection抽象)

[design/client-architecture.md](design/client-architecture.md)を新規作成。「UIとコア進行
ロジックをどう繋ぐか」の土台を設計し、Bootstrap→Home→最初の画面(TitlePage)までの最小実装を
作ってPlay Modeで実際に動作確認済み。

- **画面遷移**: [UnityScreenNavigator](https://github.com/Haruma-K/UnityScreenNavigator)
  (USN、MIT License)を採用。`Bootstrap`/`Home`/`Battle`の3シーン構成にし、Scene単位の遷移は
  Supplementの`ISceneLoader`、Scene内の遷移(タイトル/パーティ編成/対戦画面等)はUSNの
  Page/Modalという2階層構成にした。検討したが不採用にしたもの: 自作の`ScreenService`
  (View管理エンジンごと自作、別プロジェクトの未パッケージ化コード)は画面遷移そのものには使わず、
  Push時に`ViewDto`を渡す/Pop時に`Result`を型付きで受け取る、というAPIのエルゴノミクスだけを
  USNの上に実装する形にした
- **DI**: Page prefabに子`LifetimeScope`を同梱し、`LifetimeScope.EnqueueParent`で親付けして
  `Build()`する方式に確定。USN公式デモの「DIコンテナ無しで手書きFactory」方式
  (`RegisterFactory`+`AddTo(GameObject)`で再現する代替案)も検討したが、Presenterの生存期間
  管理をVContainerのスコープ機構に一貫させるため不採用。**実装時に発覚した修正点**:
  `ScreenNavigator`はPresenterの具体型を知らないため`Container.Resolve<XxxPresenter>()`は
  実装不可能で、`RegisterEntryPoint<XxxPresenter>()`(`Build()`の副作用として自動構築)に変更。
  ただし`RegisterEntryPoint`は`IInitializable`等のVContainerライフサイクルインターフェースを
  実装していないと一度も構築されないため、Presenterは`IInitializable`を実装し購読処理を
  コンストラクタから`Initialize()`へ移動する設計に修正した
- **Presenter配置**: `Atlas.Presentation`(新規asmdef)にViewと同居させ、具象Viewを直接
  コンストラクタ注入する(`IXxxView`のような境界インターフェースは作らない)。Mock/Realの
  差し替えが必要な`IXxxConnection`だけインターフェース化する非対称設計
- **`IScreenNavigator`**(旧称`INavigationService`): `PushPageAsync<TPage, TViewDto>(dto)`で
  Push時にデータを渡し、`WaitForPopAsync<TResult>`でPop時の結果を型付きで受け取れる
- **Connection抽象の一般化**: `IBattleConnection`(battle.md)のMock/Real切り替えパターンを
  アウトゲームにも適用。既存Rust API境界(Device/Player/Chat/Scout)ごとに`IXxxConnection`
  を定義する方針(1つの巨大インターフェースにはしない)。`IDeviceConnection`/`IPlayerConnection`
  を実装済み(下記「サインイン・`playerDiff`適用」参照)。Chat/Scout/パーティ編成用は未着手
- **画面をまたぐ通知**: Supplementの`IMessageBroker`(ZeroMessenger実装)をグローバル用途限定で
  導入(トースト通知・gems残高変更等)。同一画面内のView→Presenter通知はR3のObservable
  直接購読のみ。MessagePipeも検討したがSupplementに同種の仕組みが既にあるため不採用。
  `Supplement.ZeroMessenger`は`com.chinpangx.supplement`とは別パッケージだったため
  manifest.jsonへの追加が別途必要だった
- **実装(`Client/AtlasUnityProject`)**: `Atlas.Presentation`アセンブリ
  (`IScreenNavigator`/`ScreenNavigator`/`PageLifetimeScopeWithViewDto`/`RootLifetimeScope`/
  `HomeLifetimeScope`/Title画面一式)、`Bootstrap.unity`/`Home.unity`シーン、
  `Assets/Addressables/Views/Title/TitlePage.prefab`を作成。`USE_VCONTAINER`
  (SupplementのVContainer統合拡張を有効化)・`USN_USE_ADDRESSABLES`
  (USNのAddressablesアセットローダーを有効化)のスクリプティング定義シンボルを追加。
  Play Modeでの実機確認(Bootstrap→Home遷移→TitlePage表示→ボタンクリックでの表示更新)まで完了
- **ホーム画面本体**: `HomePage`/`HomePresenter`/`HomePageLifetimeScope`
  (`Presentation/Home/`)を実装し、Homeシーンの初期表示を(検証用の)TitlePageから置き換えた。
  当初は`IPlayerRepository`+`MockPlayerRepository`(`GET /players/me`のMock)で表示していたが、
  現在は`IPlayerAccountService`経由でサインイン結果を読む形に置き換え済み(下記「サインイン・
  `playerDiff`適用」参照)。ニックネーム・gems表示、Scout/Party/Battle/Chatへの導線ボタン
  (遷移先画面が無いため現状はログ出力のみ)をPlay Modeで実機確認済み。`HomePresenter`は非同期の
  初期データ取得が必要なため`IInitializable`ではなく`IAsyncStartable`を使用(使い分けは
  client-architecture.md「DIによる結線とライフサイクル」参照)
- **バトル画面**: `Presentation/Battle/`(`BattlePage`/`BattlePresenter`/`BattleViewDto`/
  `BattlePageLifetimeScope`、Viewの部品と表示用DTOは`Views/`配下に`CommandView`+`CommandDto`・
  `SelfInfoView`+`SelfInfoDto`・`OpponentInfoView`+`OpponentInfoDto`・`BattleUIStateDto`として
  分割。旧`BattleUiState`は廃止)を実装し、`Atlas.BattleCore`(`BattleEngine`)と
  UIを繋ぐ`MockBattleConnection`(`Atlas.Infrastructure.Mock`)経由で実際にバトルが動く状態まで
  到達した。選出3体はマスターデータから`TestPartyFactory`が組み立てる(`player_pachimon`未使用の
  暫定実装、design/battle.md「Stage 1」参照)。`BattlePagePlayModeTests`でPlay Mode実機確認済み
- **Home⇔Battleのシーン分離**: `Battle.unity`シーンを新規追加し、`ISceneNavigator`/`SceneNavigator`
  (Bootstrap常駐のまま、重ねるシーンを1つだけ保持し前のシーンをUnloadしてから次をLoad)で
  切り替えるようにした。Home→Battleへの選出3体の受け渡しは、シーンをまたぐとHome側のDIスコープが
  破棄されるため、Push時のViewDtoではなくRoot常駐(Singleton)の`BattleEntryStore`経由で行う
  (`HomePresenter`が`Set`→シーン切り替え、`BattleLifetimeScope`の`BattleEntryPoint`が読み出して
  `BattlePage`へのViewDtoに詰め替える)。`BattleLifetimeScope`は`HomeLifetimeScope`と同様
  `RootLifetimeScope`を直接親にする
- **決着処理**: `IBattleConnection.OnBattleEnd`を`BattlePresenter`で購読し、勝敗理由
  (`AllFainted`/`Forfeit`/`DisconnectTimeout`)に応じたテキストで`BattleResultModal`を表示、
  そこからHomeへ戻る。投了ボタン→`ForfeitConfirmModal`(確認Modal)→確定で
  `IBattleConnection.ForfeitAsync`を呼ぶフローを追加。決着後(結果Modal表示中)はコマンド・投了
  ボタンの入力を`battleEnded`フラグで無視する
- **`ScreenNavigator`のPop結果通知バグ修正**: Pop完了直後に結果(`UniTaskCompletionSource`)を
  即座に`TrySetResult`していたため、待機側が続けて別のPage/Modalを`Push`すると、USNの遷移
  アニメーションがまだ終わっていない状態で「screen is already in transition」により拒否される
  不具合があった。Pop対象の待機者はPop前に集めておき、結果通知はPopの遷移アニメーション完了後に
  行う(`CollectPending*Sources`→`SetResults`)よう修正
- **`SceneNavigator`のシーン破棄タイミング調整**: USNの遷移アニメーションは`UpdateDispatcher`
  (DontDestroyOnLoad)に登録され完了時に登録解除されるため、遷移中にシーンごとPage/Modalを
  破棄すると登録が残り、破棄済み`RectTransform`を毎フレーム操作して例外になる。`ChangeSceneAsync`は
  現在のシーンをUnloadする前に、そのシーン内の`PageContainer`/`ModalContainer`が
  遷移中(`IsInTransition`)でなくなるまで待つ
- **UI(Screen Space - Camera・横向き対応)**: Home/BattleのCanvasと画面prefabをUIレイヤーへ移動
  (UICameraがUIレイヤーのみ描画するため)。BattleシーンのCanvasもScreen Space - Cameraに揃え、
  `BootstrapTest`の重複UICameraを削除。`CanvasScaler`の基準を1920x1080(高さ合わせ)にし、
  バトルのパーツが画面いっぱいに伸縮するようにした。端末の向きを横向きのみに制限
- PlayModeテストに投了→結果→Home復帰のシナリオを追加し、パーツView分割後に壊れていた既存テストも
  修正
- **デバイス認証・サインアップ疎通(当初版、下記「サインイン・`playerDiff`適用」で置き換え済み)**:
  design/outgame.md「デバイス認証」「プレイヤー作成」の
  `POST /devices`→`POST /devices/authenticate`→`GET/POST /players*`という初回起動フローを
  実装。`IDeviceConnection`/`RealDeviceConnection`(Atlas.Infrastructure、`DeviceApiClient`
  経由)・`IPlayerRepository`のReal実装`RealPlayerRepository`(`PlayerApiClient`経由)・
  両方をまとめて呼び出す`IAuthService`/`AuthService`を追加し、`BootstrapEntryPoint`から
  Homeシーン遷移前に`EnsureSignedUpAsync`を実行する形にした。`device_id`/`secret_key`は
  `IDeviceCredentialsRepository`/`DeviceCredentialsRepository`が保持し、Supplementの
  `IFileStorageService`(`RegisterEncryptedFileStorage`、AES暗号化)でローカルファイルへ永続化
  する(再起動時は保存済みの`secret_key`で`/devices/authenticate`を再実行し、無ければのみ
  `/devices`から新規登録)。`GET /players/me`が404(プレイヤー未作成)を返した場合のみ
  `POST /players`でプレイヤーを作成する。アクセストークンは`AccessTokenStore`が保持し、
  各ApiClientの`Func<string> accessTokenProvider`へ渡す。有効期限の事前チェックや401時の
  再認証リトライ(outgame.mdの補足で「望ましい」とされる挙動)は未実装で、起動時に一度だけ
  認証する疎通確認レベルの実装に留めている(残タスク参照)。ニックネーム入力画面が無いため
  新規プレイヤー作成時は固定文字列「プレイヤー」を使う
- **サインイン・`playerDiff`適用**: Server側のAPI再編(残タスク#16)に合わせてClientの
  通信・データ保持層を作り直した。旧`IPlayerRepository`/`RealPlayerRepository`/
  `MockPlayerRepository`/`IPlayerService`/`PlayerService`/`IAuthService`/`AuthService`/
  `RealDeviceConnection`を廃止し、以下の構成に置き換えた(設計は
  client-architecture.md「コア進行ロジックのMock/Real切り替え(Connection / Repository / Service)」)
  - 通信ポート(`Atlas.Application`): `IDeviceConnection`(Domainから移動)・`IPlayerConnection`
    (`SignUpAsync`/`SignInAsync`)。Real実装`DeviceConnection`/`PlayerConnection`は
    `Atlas.Infrastructure.Api`、Mock実装`MockDeviceConnection`/`MockPlayerConnection`は
    `Atlas.Infrastructure.Mock`
  - `playerDiff`の適用: `PlayerConnection.SignInAsync`がレスポンス受信直後に
    `IPlayerDiffApplier`→`ItemDiffApplier`→`IItemRepository`(`ApiItemRepository`、メモリ保持)へ
    反映する。`playerId`/`nickname`は`SignInService`が`IPlayerProfileRepository`
    (`ApiPlayerProfileRepository`、メモリ保持)へ保存する。`pachimon`/`pachimonMoveMap`/
    `partySlots`のApplier・Repositoryは未実装(Scout/Party画面実装時に追加)
  - `ISignInService`/`SignInService`: デバイス登録・認証→`POST /sign-in`(404なら
    `POST /signup`→再度`POST /sign-in`)をまとめて実行し、`BootstrapEntryPoint`から呼ぶ。
    `IPlayerAccountService`/`PlayerAccountService`が`HomePresenter`向けにプロフィールを返す
  - Mock/Real切り替えは`RootLifetimeScope.ConfigureAuthConnections`(virtual)のoverrideで行う。
    Battle PlayModeテスト用の`TestRootLifetimeScope`がMock Connectionに差し替え、保存先
    ディレクトリも`BattlePlayModeTestSaveData`に分離する(`BootstrapTest.unity`シーン追加)
  - これにより、以前ここに記載していた「client-architecture.mdとの既知の乖離」(`IDeviceRepository`/
    `IAuthRepository`想定との不一致、`Atlas.Infrastructure.Rest`を作らず`Atlas.Infrastructure`に
    直接配置)は、設計書側を実装に合わせて更新したことで解消した
- **アクセストークンの更新**: `Atlas.Infrastructure.Api`に`AccessTokenRefresher`を追加した。
  要認証APIを送る前に端末内で有効期限をチェックし、残り5分未満なら`/devices/authenticate`で
  再認証する。401が返ったら再認証して1回だけリトライする。実行中の再認証は共有し、同時に
  複数走らないようにした。`AccessTokenStore`は有効期限(`ExpiresAtUtc`)も持つようにした。
  `SignInService`の初回認証も`AccessTokenRefresher.RefreshAsync`経由に統一し、
  `PlayerConnection`の各APIは`SendAsync`で包んだ。今後追加するChat/Scout等のConnectionも
  同じように`SendAsync`で包む必要がある
- **パーティ編成画面(ひな形)**: `PartyEditPage`(Presentation/PartyEdit、Addressablesアドレス
  `PartyEditPage`)を追加し、Homeのパーティボタンから`PushPageAsync<PartyEditPage>`で開くようにした。
  スロット6枠(3列x2段、`slot`は1始まり)・保存・戻るボタンを持つ。現時点では初期表示が空の編成で、
  スロット選択・保存はログ出力のみ(戻るはPop)。残りは、所持パチモン・現在の編成
  (`IPachimonRepository`/`IPartyRepository`)を読み出すService、パチモン選択UI、`POST /edit/party`の
  Connection(`AccessTokenRefresher.SendAsync`で包む)
- **UIPackages**: 共通UI部品`CommonButton`を`Presentation/Common`から独立アセンブリ
  `UIPackages.Runtime`(+Inspector拡張の`UIPackages.Editor`)へ移動
- **今回発見したギャップ(Client、サインイン・`playerDiff`適用)**:
  - ~~`ApiItemRepository.Upsert`が`Dictionary.Add(itemId, GetQuantity(itemId))`になっており、
    受信した`quantity`ではなく既存値(初回は0)を格納する。さらに同じ`itemId`を2回`Upsert`すると
    `ArgumentException`になる(スカウトで`items`を再受信した時点で発生する)。
    `itemEntities[itemEntity.ItemId] = itemEntity.Quantity`への修正が必要~~ → 解消。
    `GetQuantity(long)`(未所持時0埋め)を`TryGet(long, out ItemEntity)`に置き換え、`Upsert`は
    インデクサ代入(`itemEntities[itemEntity.ItemId] = itemEntity`)のみになった
  - ~~Home画面のgems表示: `HomePage`は`gemsText`を持つが、`HomePresenter`が`HomeViewDto.Gems`を
    設定しておらず常に`0`表示になる。`IItemRepository`(`item_id: 1`)の値をPresenterへ渡す
    `IXxxService`がまだ無い~~ → 解消。`IItemFetchService`/`ItemFetchService`(旧`IItemService`から
    改名)を追加し、`HomePresenter`が`GetAmount(GemItemId=1)`の結果を`HomeViewDto.Gems`へ設定する
    ようにした。あわせて`HomeViewDto.Nickname`は`PlayerId`に置き換え(固定文字列「プレイヤー」の
    ニックネームより開発中は個体識別できる方が有用なため)
  - `PlayerProfile`のコメントが廃止済みの`IPlayerRepository.SignInAsync`を参照したまま。
    `PlayerAccountService`に未使用フィールド`playerData`が残っている(未修正)
  - Play Modeでのローカルサーバー疎通確認は未実施(残タスク#15)。バトルのシーン遷移・決着処理
    (投了→結果Modal→Home復帰)についてはMock接続でのPlayModeテストを追加済み
- 実装メモ: `record`/`record struct`はUnity Editorが固定するC#言語バージョン(9.0)では
  使えない(C# 10以降が必要)。`Atlas.Domain`等のシンプルなデータ型は通常の`readonly struct`/
  `class`で書く
- Battle結果からHomeへ戻る画面遷移(結果Modal→`ISceneNavigator`でHomeシーンへ)は実装済み。
  未確定として残っているのは、Scout/Party/Chat各画面(Homeからの導線先)の個別Presenter/ViewDto設計
  (画面実装時に決定)

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
- 残タスク: ~~生成したC#コードをUnityプロジェクト側で実際にコンパイル確認すること~~ → 完了
  (`uloop compile`で0エラーを確認)。
  列挙型・クエリパラメータはapi-codegen未対応(現状のAPIには存在しないため後回し)。
  `nullable`は実際に`PUT /players/me/party`実装時に遭遇し、`TypeMapper`が例外停止することを確認済み
  (ツール自体は未対応のまま)。今回はAPI設計側で回避した(`player_party_slots`テーブル化。
  詳細は上記「3. Server API実装状況」参照)ため、ツール拡張は先送りにしている

## 5. 残タスク一覧(統合)

| # | 内容 | 領域 |
|---|---|---|
| 1 | バトルサーバー(MagicOnion)プロジェクトの新規作成・`IBattleHub`等の実装一式 | バトルサーバー |
| ~~2~~ | ~~`moves`/`move_groups`/`move_group_master`のDBテーブル作成・`cache.rs`/`seed_master_data.rs`対応~~ → 完了(マイグレーション追加・`cache.rs`で`move_group_master`をキャッシュ・`seed_master_data`で3テーブルとも投入。詳細は上記「3. Server API実装状況」参照) | server/master-data |
| ~~3~~ | ~~`player_pachimon`(所持データ)のモデル・テーブル・API実装~~ → 完了(スカウトでの入手時に作成。パーティ編成・技の付け替えAPI自体は#13で完了) | server |
| ~~4~~ | ~~スカウトAPI(`/scout/*`)実装~~ → 完了(`GET /scout/banners`, `POST /scout/rolls`, `POST /scout/rolls/{rollId}/select`。結合テスト8件、詳細は上記「3. Server API実装状況」参照) | server |
| ~~5~~ | ~~マッチングAPI(`/battle/queue*`)実装~~ → 完了(待機列は`AppState`のプロセスメモリ、`battle_token`は`jsonwebtoken`でHS256のJWT発行。結合テスト6件。詳細は上記「3. Server API実装状況」参照) | server |
| 6 | 内部API(`/internal/battle/result`)実装 | server |
| ~~7~~ | ~~`type_chart`(タイプ相性)の設計・実装~~ → 完了(schema/CSV投入・全ツールでの検証済み) | master-data/pipeline |
| 8 | 技の拡充(状態技、候補技の追加) | master-data |
| 9 | Unityクライアント側の実装一式 → 一部完了(プロジェクト構築・利用ライブラリ導入・コンパイル確認、画面遷移/DI/Connection抽象の設計、Bootstrap→Home→TitlePageの最小実装、ホーム画面本体(ジェム表示含む)、バトル画面(Mock、Home⇔Battleのシーン分離・決着処理・結果Modal・投了フロー)、UICamera/横向き対応、デバイス認証〜サインイン(`playerDiff.items`適用)まで完了。Scout/Party/Chat各画面の実装、MagicOnion StreamingHubクライアントは未着手、上記「Unity Client」「クライアントアーキテクチャ設計」参照) | client |
| ~~14~~ | ~~アクセストークンの事前有効期限チェック・401時の再認証リトライ~~ → 完了(`AccessTokenRefresher`。詳細は上記「4. Unity Client」の「アクセストークンの更新」、およびclient-architecture.md「アクセストークンの更新」参照。コンパイル確認のみで、実機での確認は#15と合わせて行う) | client |
| 15 | サインイン疎通(デバイス登録〜`POST /signup`〜`POST /sign-in`・`playerDiff`適用)のPlay Modeでの実機確認(ローカルAPIサーバー・MySQLコンテナが未起動のため今回はコンパイル確認のみ) | client |
| 16 | `playerDiff`(コレクション差分、items・pachimon・pachimonMoveMap・partySlots)共通レスポンス形式の導入 → **Server側は完了**(`items`マスタ・`player_items`テーブル・`POST /signup`/`POST /sign-in`/`POST /edit/party`/`POST /edit/pachimon_moves`・scoutのレスポンス変更まで実装済み、結合テスト55件通過、`api-codegen`再生成済み。詳細は上記「3. Server API実装状況」参照)。Client側は`POST /signup`/`POST /sign-in`と`playerDiff.items`の適用、Home画面のgems表示、`ApiItemRepository.Upsert`の不具合修正まで完了(上記「4. Unity Client」参照)。残りは`pachimon`/`pachimonMoveMap`/`partySlots`のApplier・Repository、スカウト・`POST /edit/party`・`POST /edit/pachimon_moves`のConnection | client |
| 17 | APIサーバーのコンテナ化(Dockerfileのマルチステージビルド+`SQLX_OFFLINE`、composeのprofileで開発時の`cargo run`と併用)。MagicOnionサーバーの着手時に行う(上記「3. Server API実装状況」の「検討したが採用しなかった案」参照) | server |
| ~~10~~ | ~~API codegen(Rust handler→OpenAPI→Unity C#型)の導入~~ → 完了(`api-codegen`実装済み。Unity側での実コンパイル確認のみ、Unityプロジェクト本体の構築待ちで残タスク。詳細は上記「APIサーバー ⇔ Unity Client 間のコード生成」参照) | server/client連携 |
| ~~11~~ | ~~`scout_banners`用seedスクリプト(`seed_scout_banners`)の実装・常設バナー1件の投入~~ → 完了 | server |
| ~~12~~ | ~~`Atlas.BattleCore`(Shared/BattleCore/)の骨組み作成~~ → 完了(ダメージ計算・行動順決定・Section/Event/EventHandler本体の実装・EditModeテストまで完了。詳細は上記「Atlas.BattleCore」参照) | battle/shared |
| ~~13~~ | ~~パーティ編成・技の付け替えAPI(`GET/PUT /players/me/pachimon*`, `PUT /players/me/party`)実装~~ → 完了(`player_party_slots`テーブル新設によりnullableを使わない設計に変更。結合テスト11件、詳細は上記「3. Server API実装状況」参照) | server |
