# 開発進捗

最終更新: 2026-09-24

クライアント・APIサーバー・バトルサーバーの実装状況と残タスクをまとめた、常に最新に保つ進捗文書。
設計の詳細や、案を採用・不採用にした経緯は`design/`配下の設計書に書く。この文書には「何が終わっていて、何が残っているか」だけを書く。
2026-09-24以前の作業経緯(時系列の詳細)はgit履歴の旧版を参照。

- `[x]` 実施済み / `[ ]` TODO
- TODOには`C-1`(クライアント)・`S-1`(APIサーバー)・`B-1`(バトルサーバー)・`M-1`(共通)の番号を振る。
  完了したら`[x]`にして「実施済み」へ移す。番号は振り直さない

## 全体サマリー

| 領域 | 状況 |
|---|---|
| クライアント(Unity) | 認証〜サインイン、Home、パーティ編成、マッチング〜対戦〜結果までは実装済み。スカウト・チャット・技の付け替え・ニックネーム入力の画面は未着手。**実サーバーにつないだPlay Modeでの確認は未実施** |
| APIサーバー(Rust/Axum) | 設計書にあるAPIはすべて実装済み(結合テスト62件)。残りはコンテナ化とテスト環境の不具合 |
| バトルサーバー(C#/MagicOnion) | Hub一式・切断と再接続・制限時間・結果報告・選出個体の実データ化まで実装済み(xUnit約30件)。ボット同士の実サーバー対戦で確認済み |
| 共通(マスターデータ・コード生成) | パイプラインはClient/Server/BattleServerの3か所へ配置済み。技の種類が少ない |

## 次に進める順番

目標は「Unity ClientからBattleServer上で、プレイヤーの所持データを使って対戦できる」こと。
Server・BattleServerはボット同士の対戦で確認済みなので、Client側の確認から始める。

1. C-1 Multiplayer Play Modeでの実サーバー対戦確認(不具合が見つかれば修正を最優先)
2. C-3 自分側の切断→再接続
3. C-4 対戦まわりの失敗時のエラーModal
4. C-5 ターン制限時間と相手の切断中表示
5. その後、C-11 IL2CPP対応、C-7〜C-10 各画面、M-3 `.meta`が消える問題

---

## 1. クライアント(Unity)

`Client/AtlasUnityProject`(Unity 6.6)。設計は[design/client-architecture.md](design/client-architecture.md)。

### 実施済み

#### プロジェクト基盤

- [x] Unityプロジェクトの作成、利用ライブラリの導入、コンパイル確認
  - VContainer / UniTask / R3 / UnityScreenNavigator / MagicOnion.Client / YetAnotherHttpHandler /
    MessagePack / MasterMemory / ZeroMessenger / ZLinq / Supplement(submodule)。導入経路はarchitecture.md「クライアント利用ライブラリ」
  - MasterMemory等はUPM版にSource Generatorが無いため、NuGetForUnityでNuGet版を`Assets/Packages/`に配置
- [x] マスターデータの組み込み(`Scripts/MasterData/`の単一アセンブリ`Atlas.MasterData`、`masterdata.bytes`はAddressablesで配布)
- [x] REST APIクライアントの組み込み(`api-codegen`で生成し`Infrastructure/Api/`へ配置、`uloop compile`で0エラー)
- [x] 開発用の通信ログ(`UnityApiRequestLogger`、`[API] --> / <--`形式、`secretKey`/`accessToken`は伏字。開発ビルドのみ)
- [x] `uloop`(uLoopMCP)でEditor操作・コンパイル確認を自動化
- [x] 共通UI部品`CommonButton`を独立アセンブリ`UIPackages.Runtime`へ分離

#### 画面遷移・DI・アーキテクチャ

- [x] Bootstrap / Home / Battle の3シーン構成。シーン単位は`ISceneNavigator`、シーン内はUSNのPage/Modal
- [x] `IScreenNavigator`(Push時にViewDtoを渡し、Pop時に結果を型付きで受け取る)
- [x] Page prefabに子`LifetimeScope`を持たせるDI方式、Presenterは`RegisterEntryPoint`で構築
- [x] Connection抽象(`IXxxConnection`)でMock/Realを切り替える構成
- [x] 画面をまたぐ通知に`IMessageBroker`(ZeroMessenger)を導入
- [x] `ScreenNavigator`のPop結果通知タイミングの不具合を修正(遷移アニメーション完了後に通知)
- [x] `SceneNavigator`のシーン破棄タイミングを調整(Page/Modalの遷移完了を待ってからUnload)
- [x] UIをScreen Space - Camera(UICamera)に統一、1920x1080基準、横向き固定

#### 認証・サインイン

- [x] デバイス登録・認証(`device_id`/`secret_key`をAES暗号化してローカル保存)
- [x] `ISignInService`: 認証 → `POST /sign-in`(404なら`POST /signup` → 再度`/sign-in`)
- [x] `playerDiff`の適用(items / pachimon / pachimonMoveMap / partySlots の各Applier・Repository)
- [x] `AccessTokenRefresher`: 期限切れ5分前の再認証、401時に再認証して1回リトライ、同時実行の共有
- [x] Mock/Realの認証Connectionを`RootLifetimeScope.ConfigureAuthConnections`で切り替え
- [x] セーブデータの保存先をインスタンスごとに分ける`SaveDataDirectory`(Multiplayer Play Mode用)

#### Home

- [x] `HomePage`/`HomePresenter`: PlayerId・ジェム表示、Scout/Party/Battle/Chatへのボタン
  (ScoutとChatは遷移先が無いためログ出力のみ)

#### パーティ編成

- [x] `PartyPage`: 左に編成(slot1〜6)、中央に所持パチモン一覧、右に詳細(タイプ・ステータス・技)
- [x] 入れ替え・外す・最後の1体は外せない、のルールを`PartyEditor`に分離(EditModeテスト`PartyEditorTests`)
- [x] 戻る時に変更があれば`POST /edit/party`で保存、失敗時は画面に留まる
- [x] ステータス計算を`PachimonStatCalculator`(レベル50固定)に統一(`TestPartyFactory`は除く、C-12参照)

#### バトル

- [x] `BattlePage`/`BattlePresenter`とViewの部品分割(`CommandView`/`SelfInfoView`/`OpponentInfoView`)
- [x] `MockBattleConnection`(BattleCoreをクライアント内で動かすオフライン対戦)
- [x] コマンドUI: たたかう/こうたい のメニュー、技4つ(タイプ・残りPP表示、PP0は押せない)
- [x] 交代UI(`SwitchSelectModal`、瀕死時の強制交代も同じModal)、行動送信後の入力ロック
- [x] 決着処理(`BattleResultModal`、投了確認Modal → `ForfeitAsync`)、Homeへ戻る
- [x] Home⇔Battleのシーン分離(選出の受け渡しはRoot常駐の`BattleEntryStore`)
- [x] 通信契約を`Shared/BattleContracts/`(`Atlas.BattleContracts`)に切り出してBattleServerと共有
- [x] マッチング: `MatchmakingModal`(キャンセル可) → `ApiBattleMatchmaker`(`POST /battle/queue` + 1秒ごとの`GET /battle/queue/status`)
- [x] `RealtimeBattleConnection`(MagicOnion)でBattleServerへ接続。`RootLifetimeScope`の`Use Battle Server`でMockと切り替え
- [x] Multiplayer Play Mode(`com.unity.multiplayer.playmode` 3.0.0)の導入
- [x] PlayModeテスト`BattlePagePlayModeTests`(Mock接続: 対戦・交代・投了 → 結果 → Home復帰)

### TODO

- [ ] **C-1 Multiplayer Play Modeでの実サーバー対戦確認**
  - Unity Client同士で、実際のServer + BattleServerを通して対戦できるかを確認する(これまではボット同士でのみ確認)
  - 事前準備: BattleServerのuser-secrets設定(DEVELOPMENT.md「3.」)、両サーバーを最新のコードで起動
  - 初期技4つの変更前に作ったアカウントは、セーブデータ(`SaveData`/`SaveData_VP_<id>`)を消して作り直す
  - 確認すること: マッチング → 選出 → 技4つの表示と送信 → 交代 → 決着 → 結果Modal → Home復帰、相手パチモンの名前・タイプ表示
- [ ] **C-2 サインインの実サーバー疎通確認**
  - デバイス登録 → `POST /signup` → `POST /sign-in` → `playerDiff`の適用を、ローカルのAPIサーバー・MySQLにつないでPlay Modeで確認する
  - `AccessTokenRefresher`(期限前の再認証・401時のリトライ)もコンパイル確認しかしていない。C-1と同時に確認できる
- [ ] **C-3 自分側の切断→再接続**
  - 現状は一瞬切れただけで`DisconnectTimeout`負けになる
  - `RealtimeBattleConnection`で切断を検知し(`WaitForDisconnect`)、同じ`battleToken`/`matchId`で`JoinAsync`を再実行する(猶予60秒以内に数回)
  - 再送される`OnMatchStart`で盤面を戻す。再接続中は入力を止めて、その旨を表示する
- [ ] **C-4 対戦まわりの失敗時のエラーModal**
  - エラーModalを作り、BattleServerへの参加失敗・マッチング失敗・行動の送信失敗の3か所で表示する(現状はログのみ)
  - 通信エラー全体の仕組み(C-6)は後回しにし、まず対戦の3か所に絞る
- [ ] **C-5 ターン制限時間と相手の切断中表示**
  - `TurnTimeLimitSeconds`をもとに残り秒数をカウントダウン表示する
  - `OnOpponentDisconnected`/`OnOpponentReconnected`を購読して「相手の接続を待っています」を表示する(イベントは`IBattleConnection`にあるが、Presenterでは未購読)
- [ ] **C-6 通信エラー共通の仕組み**
  - 通信基盤(Infrastructure)でエラーを検知 → `IMessageBroker`で通知 → 各シーンの購読者がErrorModalを出す構成を想定
  - 設計から見直す点: `IScreenNavigator`がシーン単位で`RootLifetimeScope`に無い、起動時のサインイン失敗はNavigatorが無い段階で起きる、
    認証不要の`DeviceConnection`は`AccessTokenRefresher.SendAsync`を通らない
  - リトライ/タイトルへ戻す等のUXも合わせて決める(現状、パーティ編成の保存失敗はログ出力のみ)
- [ ] **C-7 スカウト画面**
  - `IScoutConnection`(`GET /scout/banners`・`POST /scout/rolls`・`POST /scout/rolls/{rollId}/select`)と画面を作る
  - Connectionの各APIは`AccessTokenRefresher.SendAsync`で包み、`playerDiff`(items / pachimon / pachimonMoveMap)を適用する
- [ ] **C-8 チャット画面**
  - `IChatConnection`(`POST /chat/send`・`GET /chat/poll`)と画面を作る。Connectionは`SendAsync`で包む
- [ ] **C-9 技の付け替え画面**
  - `IPlayerConnection`に`POST /edit/pachimon_moves`の呼び出しを追加し、パーティ編成の詳細などから開ける画面を作る
- [ ] **C-10 ニックネーム入力画面**
  - 新規作成時のニックネームが固定文字列「プレイヤー」になっている。入力画面を作って`POST /signup`へ渡す
  - Homeは開発中の識別用にPlayerIdを表示しているので、ニックネーム表示へ戻すかも合わせて決める
- [ ] **C-11 IL2CPPビルド対応**
  - UnityにMessagePackのSource Generatorが入っておらず、`Atlas.BattleContracts`のPayloadは動的シリアライズ(Editor/Monoでのみ動作)
  - 実機・モバイル向けにはGeneratorの導入かResolverの事前生成が必要
- [ ] **C-12 小さなコード整理**
  - `Domain/PlayerProfile.cs`のコメントが廃止済みの`IPlayerRepository.SignInAsync`を参照している
  - `Infrastructure/PlayerAccountService.cs`に未使用フィールド`playerData`が残っている
  - `TestPartyFactory.CalculateStat`(Mock用)が独自のステータス計算を持っている。`PachimonStatCalculator`へ統一する
- [ ] **C-13 パチモンのサムネイル画像**
  - `PachimonDto.Thumbnail`がnullのため、単色のプレースホルダで表示している

---

## 2. APIサーバー(Rust/Axum)

`Server/`。詳細は[Server/CLAUDE.md](../../Server/CLAUDE.md)、`Server/docs/notes/api-design.md`。

### 実施済み

#### エンドポイント(すべてルーティング登録・結合テストあり)

| メソッド | パス | 概要 |
|---|---|---|
| POST | /devices | デバイス登録 |
| POST | /devices/authenticate | デバイス認証(アクセストークン発行) |
| GET | /auth/verify | トークン検証 |
| POST | /signup | プレイヤー作成(スターター編成・初期ジェム300を同一トランザクションで付与) |
| POST | /sign-in | プレイヤー情報 + `playerDiff`(所持データ全件) |
| POST | /chat/send | チャット送信 |
| GET | /chat/poll | チャット取得 |
| GET | /scout/banners | バナー一覧 |
| POST | /scout/rolls | スカウト実行(ジェム消費、候補10体) |
| POST | /scout/rolls/{rollId}/select | 候補から1体を選んで入手 |
| POST | /edit/party | パーティ編成(全置き換え) |
| POST | /edit/pachimon_moves | 技の付け替え |
| POST | /battle/queue | マッチング待機列に参加 |
| DELETE | /battle/queue | 待機列から離脱 |
| GET | /battle/queue/status | マッチング状況(成立時に`battle_token`) |
| POST | /internal/battle/result | 対戦結果の記録(BattleServer専用、OpenAPI対象外) |
| POST | /internal/battle/loadouts | 選出個体の所持データ取得(BattleServer専用、OpenAPI対象外) |

#### 機能・仕組み

- [x] デバイス認証(`secret_key`をArgon2でハッシュ化、IDはULID、1デバイス1トークン)と共通認証Extractor`AuthenticatedDevice`
- [x] `playerDiff`共通レスポンス形式(items / pachimon / pachimonMoveMap / partySlots)
- [x] 所持リソースを`player_items`に一般化(`players.gems`列を廃止)
- [x] スターター編成の自動付与(`starter_party_slots`マスタを複製。個体生成は`player_pachimon_service::grant`でスカウトと共通化)
- [x] スカウト(レアリティの重み付き抽選 → パチモンを等確率で選出 → 初期技を確定。ジェム減算はトランザクション)
- [x] パーティ編成は`player_party_slots`テーブル(nullableを使わない設計)、技は`player_pachimon_moves`(ULID主キー)
- [x] 個体値(IV)の概念を廃止(種族値 + 努力値のみ、レベル50固定)
- [x] マッチング: プロセスメモリの待機列、`battle_token`はHS256のJWT(30秒)、成立時に`battle_matches`を作成
- [x] 内部API: `X-Internal-Secret`を定数時間比較。結果記録は1トランザクション(`FOR UPDATE`で二重報告は409)、勝者にジェム50
- [x] 勝者なしの結果(`winnerId`空文字)は`aborted`。結果報告が1時間届かない対戦は5分ごとの定期処理で`aborted`にする
- [x] マスタは起動時にDBからメモリキャッシュへ読み込む(`src/master/cache.rs`)。投入は`cargo run --bin seed_master_data`、バナーは`seed_scout_banners`
- [x] OpenAPI出力(`utoipa`、`cargo run --bin export_openapi`)と`/swagger-ui`
- [x] 開発用の通信ログ(`tracing`、`RUST_LOG`でボディ・SQLも出力。`secretKey`/`accessToken`は伏字)
- [x] マイグレーション27本、結合テスト62件(`tests/{auth,battle,chat,device,master_data,player,scout}_api_test.rs`)
- [x] `type: int`を32bit(Rust `i32` / DB `INT`)に統一し、API DTOの`pachimonId`/`moveId`/`itemId`も`i32`に揃えた

### TODO

- [ ] **S-1 `sqlx::test`の結合テストが`PoolTimedOut`で失敗する**
  - `failed to connect to setup test database: PoolTimedOut`で失敗する。直近の変更前のコードでも同じなので、ローカル環境側の問題と思われる
  - 原因は未調査。直るまでは「テスト62件」が今の環境で全部通るとは言えない
- [ ] **S-2 APIサーバーのコンテナ化**
  - Dockerfileのマルチステージビルド + `SQLX_OFFLINE`(`.sqlx`オフラインキャッシュ)、composeのprofileで開発時の`cargo run`と併用する
  - Windows上のDockerではビルドが遅くデバッグもしにくいため見送り中。デプロイ方式を決めるときに行う(design/battle.mdのBattleServer側も同じ判断)

---

## 3. バトルサーバー(C#/MagicOnion)

`BattleServer/`(ASP.NET Core + `MagicOnion.Server` 7.11.0)と、共通のバトルロジック`Shared/BattleCore/`(`Atlas.BattleCore`)。
設計は[design/battle.md](design/battle.md)。

### 実施済み

#### BattleCore(共通ロジック)

- [x] `BattleEngine.ProcessTurn`: 強制交代チェック → 行動順 → 行動 → 交代/技効果 → 命中 → ダメージ → 瀕死 → 技効果後処理
- [x] ダメージ計算(STAB・タイプ相性・急所・最低1)、行動順、命中判定、PP消費。乱数・タイプ相性は`IRandomSource`/`ITypeChart`で注入
- [x] 技効果後処理はEventフック(`MoveHitEvent`/`IMoveHitEventHandler`)の形のみ用意(反応するHandlerは0個、設計どおり)
- [x] EditModeテスト(`DamageCalculatorTests`/`TurnResolverTests`/`BattleEngineTests`)
- [x] BattleServer用のcsprojを`BattleServer/BattleCore/`に配置(Unityパッケージ内にbin/objを出さないため)

#### BattleServer

- [x] `IBattleHub`/`IBattleHubReceiver`(契約は`Shared/BattleContracts/`でClientと共有)
- [x] `battle_token`の検証(HS256、`BATTLE_TOKEN_SECRET`が32バイト未満なら起動失敗)
- [x] `BattleCoordinator`: Join、選出、ターン解決、投了、切断猶予(60秒)と再接続(盤面復元)、ターンタイムアウト(30秒)
- [x] 選出の制限時間(2分)、放置(ターンタイムアウトが3連続で敗北)。両者該当なら勝者なし
- [x] `BattleResultReporter`: `/internal/battle/result`へ報告(通信エラー・5xxは1秒/5秒/15秒で再送、409は成功扱い)
- [x] 選出個体の実データ化: `/internal/battle/loadouts`で所持データを取得し(所持チェックはRust側)、マスタから`LoadoutBuilder`で組み立て
- [x] マスターデータの読み込み(`BattleServer/MasterData/`、起動時に`MemoryDatabase`)
- [x] `BattleTimingOptions`で制限時間類を差し替え可能に(テスト用)
- [x] 対戦相手ボット`BattleServer/BattleBot/`(REST + MagicOnionで自動対戦、`--loop`/`--count`)
- [x] 自動テスト(xUnit、`dotnet test BattleServer.slnx`): インプロセス起動した結合テスト、`LoadoutBuilderTests`、`ApiParticipantDataSourceTests`、`MasterDatabaseFactoryTests`
- [x] ボット2体で実サーバーを通した対戦(マッチング → 強制交代 → 全滅決着 → 結果記録)を確認

### TODO

- [ ] **B-1 このPCでのuser-secrets設定**
  - `BATTLE_TOKEN_SECRET`/`INTERNAL_API_SECRET`が`dotnet user-secrets`に未設定(前回の確認は環境変数で渡して起動した)
  - DEVELOPMENT.md「3.」の手順を実施する。C-1の事前準備を兼ねる
- [ ] **B-2 技の追加効果の実装**
  - `IMoveHitEventHandler`を実装するHandlerが0個のため、状態異常・能力変化などの追加効果は動かない
  - 状態技をマスタに追加する(M-1)のと合わせて実装する
- [ ] **B-3 BattleCoreのEditModeテストをUnity上で実行する**
  - これまでは`dotnet`でのビルド確認のみで、Unity EditorのTest Runnerでは実行していない

---

## 4. 共通(マスターデータ・コード生成ツール)

### 実施済み

#### master-data-pipeline(submodule)

- [x] Atlas固有のスキーマ・CSVは`Shared/master-data/`に置き、ツール本体は汎用のまま(`config.yaml`は追跡除外)
- [x] Enumの`key`(コード上の識別子)と`name`(表示名)の分離、テーブル単位の`targets`フラグ
- [x] 生成物の配置: Client(`Scripts/MasterData/` + Addressablesの`masterdata.bytes`)、Server(`src/master/generated/` + `master_data/*.json`)、BattleServer(`BattleServer/MasterData/`)
- [x] `type: int`を32bitに統一し、64bit用に`type: long`を追加。enumのシリアライズも`i32`に統一

#### マスターデータ内容

| テーブル | 件数 | 内容 |
|---|---|---|
| `pachimon` | 36 | ID 1001〜1036。S 18体、A/B/C 各6体 |
| `move_groups` | 36 | 現状は1パチモン1グループ |
| `moves` | 64 | 専用技36、汎用技2(19/20)、汎用タイプ技26(39〜64)。状態技は0件 |
| `move_group_moves` | 180 | 各グループに初期技4つ + 付け替え候補1つ(エナジーウェーブ) |
| `type_chart` | 324 | 18×18タイプ(第9世代準拠) |
| `starter_party_slots` | 6 | 新規プレイヤーに付与するスターター(rarity C の6体) |
| `items` | 1 | `item_id: 1` = ジェム |

- Serverでメモリキャッシュする対象は`pachimon`・`move_group_moves`・`starter_party_slots`・`items`(`src/master/cache.rs`)。`moves`/`move_groups`/`type_chart`はDB投入のみ

#### api-codegen(submodule)

- [x] OpenAPI → Unity向けDTO(`Dto/*.cs`)とタグ単位のAPIクライアント(`UnityWebRequest` + `UniTask`)を生成
- [x] ロガーの差し込み口(`ApiRequest.Logger`/`IApiRequestLogger`)

### TODO

- [ ] **M-1 技の拡充**
  - 状態技が0件。技の付け替え候補がエナジーウェーブ(20)の1つだけで選択肢が無い
  - 状態技を入れる場合はB-2(追加効果)と合わせて行う
- [ ] **M-2 `base_power`のNULL代替(0埋め)の検証**
  - 状態技が無いため、`base_power = 0`の運用を実データで確かめていない。M-1のときに確認する
- [ ] **M-3 `copy-client-bytes`でUnity側の`.meta`が消える問題**
  - 現状は`master-data-pipeline`スキルの復元手順で対処している。pipeline側で`.meta`を残すよう直す
- [ ] **M-4 api-codegenの未対応機能**
  - 列挙型・クエリパラメータに未対応(今のAPIには無いため後回し)
  - `nullable`は対応しない方針。必要になったらAPI/スキーマ側の設計で回避する
- [ ] **M-5 Supplementの`AssetLoader`機能追加**
  - Addressables実装にラベル指定ロード・進捗通知が無い。Supplement側に追加を依頼中
