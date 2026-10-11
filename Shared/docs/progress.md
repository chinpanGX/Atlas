# 開発進捗

最終更新: 2026-10-11

クライアント・APIサーバー・バトルサーバーの実装状況と残タスクをまとめた、常に最新に保つ進捗文書。
設計の詳細や、案を採用・不採用にした経緯は`design/`配下の設計書に書く。この文書には「何が終わっていて、何が残っているか」だけを書く。
2026-09-24以前の作業経緯(時系列の詳細)はgit履歴の旧版を参照。

- `[x]` 実施済み / `[ ]` TODO
- TODOには`C-1`(クライアント)・`S-1`(APIサーバー)・`B-1`(バトルサーバー)・`M-1`(共通)の番号を振る。
  完了したら`[x]`にして「実施済み」へ移す。番号は振り直さない

## 全体サマリー

| 領域 | 状況 |
|---|---|
| gRPCへの乗せ換え | [計画](proposals/grpc-migration-plan.md)のAPIはすべて移した(チャットは移さずに消した)。残りはgRPCでの対戦の接続確認(G-5b) |
| クライアント(Unity) | 認証〜サインイン、Home、パーティ編成、スカウト、マッチング〜対戦〜結果までは実装済み。サインイン・スカウト・Unity同士の対戦(Multiplayer Play Mode)はREST版の実サーバーで確認済み(gRPC版では未確認)。技の付け替え・ニックネーム入力の画面は未着手 |
| APIサーバー(Rust) | クライアント向けはgRPC(tonic + VolTech gRPC。Player・Party・Scout・Matchmaking・Debug)、BattleServer向けは内部API(REST)(結合テスト: gRPC 27件・内部API 17件) |
| バトルサーバー(C#/MagicOnion) | Hub一式・切断と再接続・制限時間・結果報告・選出フェーズ(パーティの見せ合い)・選出個体の実データ化・強制交代ターンまで実装済み(xUnit 37件)。ボット同士・Unity Client同士の実サーバー対戦で確認済み |
| 共通(マスターデータ・コード生成) | パイプラインはClient/Server/BattleServerの3か所へ配置済み。パチモン136種・技261種をCSVに入力したが、生成物とDBへの反映はまだ(M-5) |

## 次に進める順番

0. gRPCへの乗せ換えの残り(G-5b 対戦の接続確認)

対戦まわり(C-3〜C-5)は手動テストまで進めたところでいったん止め、クライアント側だけで
完結する画面の実装を優先する。

1. C-9 技の付け替え画面、C-10 ニックネーム入力画面
2. その後、対戦まわり(C-3 再接続、C-4 エラーModal、C-5 制限時間表示)に戻る
3. C-11 IL2CPP対応、M-3 `.meta`が消える問題

---

## 0. gRPCへの乗せ換え

[計画](proposals/grpc-migration-plan.md)の10章の順に、サーバー・クライアント・BattleBotをまとまりごとに移す。

- [x] **G-0 準備**: APIサーバーの依存の版上げ(sqlx 0.9等)、UnityにVolTech gRPCのパッケージ(Unity 6000.6.2f1で基盤のテストが通る)
- [x] **G-1 認証とサインイン**: `PlayerService`、基盤のテーブルとマイグレーションの書き換え、REST(内部API以外)の削除、
  クライアントの`ApiCore`・アカウント情報の保存・エラーダイアログ・タイトルでのサインイン
- [x] **G-2 編成**: `PartyService`(編成・技の付け替え)、`player_diff`のコレクター(`PlayerDiffCollector`)、
  クライアントの`IPlayerDiffApplier`(`PlayerDiffApplier`を`ApiCore`に渡す)
- [x] **G-3 スカウト**: `ScoutService`(バナー一覧・ロール・選択)。ジェム不足と開催期間外を別の業務エラーに分けた
- [x] **G-4 デバッグ**: `DebugService`(ジェム付与・ランダム編成。環境変数`ENABLE_DEBUG_API`、未設定ならデバッグビルドだけ登録する)
- [x] **G-5 マッチング**: `MatchmakingService`(状況の確認でマッチ情報を消さない形に変え、次の参加・離脱・結果報告で消す)、
  クライアントの`GrpcBattleMatchmaker`、BattleBotのgRPC化(`Grpc.Tools`で生成した素のクライアント)
- [ ] **G-5b 対戦の接続確認**: YetAnotherHttpHandler 1.11.6で、実サーバーにつないだ対戦(MagicOnion)とボットとの対戦を確かめる
- [x] **G-6 チャット**: 移さずに機能ごと消した(使う画面が無いため)。`messages`テーブルは新しいマイグレーションで消す
- [x] **G-7 後片付け**: `NotMigratedConnection`・utoipa/Swagger・`export_openapi`・`openapi.yaml`・使わなくなった`argon2`を消した
- [x] **G-7b `api-codegen`を外す**: submodule・スキル・README・CLAUDE.md・DEVELOPMENT.md・design.mdの記述

---

## 1. クライアント(Unity)

`Client/AtlasUnityProject`(Unity 6.6)。設計は[design.md](design.md)「6. クライアント」。

### 実施済み

#### プロジェクト基盤

- [x] Unityプロジェクトの作成、利用ライブラリの導入、コンパイル確認
  - VContainer / UniTask / R3 / UnityScreenNavigator / MagicOnion.Client / YetAnotherHttpHandler /
    MessagePack / MasterMemory / ZeroMessenger / ZLinq / LitMotion / Supplement(submodule) / UnityScreenNavigatorのフォーク(submodule)。導入経路はdesign.md「6.1 採用ライブラリ」
  - MasterMemory等はUPM版にSource Generatorが無いため、NuGetForUnityでNuGet版を`Assets/Packages/`に配置
- [x] マスターデータの組み込み(`Scripts/MasterData/`の単一アセンブリ`Atlas.MasterData`、`masterdata.bytes`はAddressablesで配布)
- [x] APIサーバーとの通信(gRPC基盤VolTech gRPCの`ApiCore`と、`.proto`から生成したラッパーを`Infrastructure/Grpc/Generated/`へ配置)
- [x] 開発用の通信ログ(基盤の`UnityApiLogger`、`[Api] → / ←`形式とgRPC Monitor。開発ビルドのみ)
- [x] `uloop`(uLoopMCP)でEditor操作・コンパイル確認を自動化
- [x] 共通UI部品`CommonButton`を独立アセンブリ`UIPackages.Runtime`へ分離
- [x] フォント(`NotoSansJP-Medium SDF`)をDynamicからStaticに変更。Dynamicだと、Play中にアセットが書き換わり、
  Assetsを共有しているMultiplayer Play Modeの追加インスタンスで文字化けしていた。
  `Tools > Bake Static Font`(`Scripts/Editor/FontBaking/StaticFontBaker.cs`)で焼き込む。
  対象はJIS第1水準・ASCII・マスターデータCSV・プレハブ/シーン/C#に出てくる文字で、合計3583文字、
  40pt、2048のアトラス2枚。第1水準に無い文字を追加したら焼き直す(DEVELOPMENT.md「Client」)
- [x] 小さなコード整理(旧C-12): `PlayerProfile.cs`の廃止済みAPI参照コメントを修正、
  `PlayerAccountService`の未使用フィールド`playerData`を削除、`TestPartyFactory`独自の
  ステータス計算を`PachimonStatCalculator`(MasterData)に統一

#### 画面遷移・DI・アーキテクチャ

- [x] Bootstrap / Home / Battle の3シーン構成。シーン単位は`ISceneNavigator`、シーン内はUSNのPage/Modal
- [x] 画面遷移をUSNのフォーク(chinpanGX/UnityScreenNavigator、`develop`)のPresenter起点方式へ移行。
  フォークはサブモジュール`UnityScreenNavigator/`にし、manifestから`file:`で参照する。
  Presenterの型でPush(`[AssetAddress]`でprefabを解決)し、`ScreenNavigator`がPushごとにヘッドレスな子スコープで
  Presenterを解決する。Page/Modal prefabの子`LifetimeScope`・`PageLifetimeScope<TViewDto>`・`ResultModal`/`ResultPage`・
  Atlas独自の`IScreenNavigator`/`ScreenNavigator`/`SceneNavigator`/`TransitionQueue`は廃止。Pop結果は
  `IPresenter.CompleteAsync`+`WaitForPopAsync<TResult>`。Home/BattleシーンにOverlayContainerを追加(未使用)。
  遷移の直列化(`TransitionQueue`)とPresenterの破棄(子スコープに`Scoped`で登録し、スコープごとDispose)はフォーク側で持ち、
  二重Popの防止(`SwitchSelectPresenter.CloseAsync`)・遷移完了を待つシーン切り替え(`TransitionAwareSceneNavigator`)は
  アプリ側で持つ(design.md「6.3 画面の構成」)
- [x] (旧方式)Page prefabに子`LifetimeScope`を持たせるDI方式、Presenterは`RegisterEntryPoint`で構築
- [x] Connection抽象(`IXxxConnection`)でMock/Realを切り替える構成
- [x] 画面をまたぐ通知に`IMessageBroker`(ZeroMessenger)を導入
- [x] `ScreenNavigator`のPop結果通知タイミングの不具合を修正(遷移アニメーション完了後に通知)
- [x] `SceneNavigator`のシーン破棄タイミングを調整(Page/Modalの遷移完了を待ってからUnload)
- [x] (旧方式)Pop結果を`ResultModal<TResult>`/`ResultPage<TResult>`に移行(結果の型を画面の型に持たせてコンパイル時に検査、最上位ではなく自分を閉じる、Complete以外の閉じ方でも待機が終わる)
- [x] 遷移の直列化(`TransitionQueue`、現在はフォーク側): コンテナごとに、Navigatorの Push/Pop と`ResultModal`/`ResultPage`の
  自分を閉じるPopを要求順に1つずつ実行する(遷移中の遷移要求が例外で失敗しなくなった)。
  EditModeテスト`TransitionQueueTests`(順番・失敗後も続く・コンテナ破棄でキャンセル)。Play Modeで、
  Pushの完了を待たずにPopを重ねる/自分を閉じるPopとPush・Popを重ねる、の2通りを確認。
  連打は対象外(遷移中はUSNが入力を止め、遷移前の連打はPresenterで捨てる。design.md「6.4 画面遷移」)
- [x] UIをScreen Space - Camera(UICamera)に統一、1920x1080基準、横向き固定
- [x] VContainerが新規`*LifetimeScope.cs`を空テンプレートで上書きする問題に対応(旧C-15)。
  `VContainerSettings.DisableScriptModifier`はEditモードでは効かないことを実機検証で確認
  (`VContainerSettings.Instance`が`Application.isPlaying`時しか設定されないため)。実効性のある対策は
  「`.cs`作成と同時に`.meta`も自分で用意する(`.meta`が無い状態でUnityに検知させない)」で、これも
  実機検証済み。`CLAUDE.md`(Client/AtlasUnityProject)に手順を明記した。`VContainerSettings.asset`
  (`Assets/Settings/`、`DisableScriptModifier=true`)はPreloadedAssetsに登録済み(Playモードでの
  スクリプト生成には効くが、今回の主因であるEditモードには効かないため、あくまで補助的なもの)

#### 認証・サインイン

- [x] gRPC基盤(VolTech gRPC v0.1.0)の`ApiCore`で通信する。デバイス登録・トークンの発行・期限前の発行し直し・
  `UNAUTHENTICATED`のときの1回だけの再送は基盤が行う
- [x] アカウント情報(`device_id`・`secret_key`・`player_id`)を接続先ごとに暗号化して保存(`FileAccountStore`)
- [x] Homeシーンの最初をタイトルにし、「Start」でサインイン → `PLAYER_NOT_FOUND`ならサインアップ → サインイン → ホーム。
  サインインの全件(`snapshot`)で各Repositoryを置き換える(`PlayerSnapshotApplier`)
- [x] 通信エラーのダイアログ(`ErrorDialog`、Bootstrapシーンに常駐)と、エラーの後の画面遷移(`ErrorNavigator`: タイトル・ホーム)
- [x] テストでは`CallInvoker`を偽のサーバー(`FakeServer` + `FakeCallInvoker`)に差し替える(`TestRootLifetimeScope`)
- [x] セーブデータの保存先をインスタンスごとに分ける`SaveDataDirectory`(Multiplayer Play Mode用)
- [x] 実サーバーで、Start → `SignIn`が`PLAYER_NOT_FOUND` → `SignUp` → `SignIn` → ホーム(ジェム300・スターター)、
  サーバー停止中の通信エラー → 再試行で成功、「タイトルへ」でタイトルに戻る、をPlay Modeで確認

#### Home

- [x] `HomePage`/`HomePresenter`: PlayerId・ジェム表示、Scout/Party/Battleへのボタンと開発用のジェム付与ボタン
  (`DebugService/GrantGems`)。スカウト画面から戻ったらジェム表示を取り直す

#### パーティ編成

- [x] `PartyPage`: 左に編成(slot1〜6)、中央に所持パチモン一覧、右に詳細(タイプ・ステータス・技)
- [x] 入れ替え・外す・最後の1体は外せない、のルールを`PartyEditor`に分離(EditModeテスト`PartyEditorTests`)
- [x] 戻る時に変更があれば`PartyService/SetParty`で保存、失敗時は(ダイアログの後)画面に留まる
- [x] ステータス計算を`PachimonStatCalculator`(レベル50固定)に統一
- [x] 技の付け替え(`PartyService/SetPachimonMove`)の通信・Service層。`IPartyConnection.EditPachimonMoveAsync`と
  `IPachimonMoveMappingService.EditAsync`(`IPartyConnection`へ委譲、`PartyService`と同じ構成)。View/Presenterは未着手(C-9)
- [x] `player_diff`は基盤の`ApiCore`が`PlayerDiffApplier`で反映する(Connection・Serviceは成否だけを返す)

#### スカウト

- [x] `IScoutConnection`(`ScoutService`の`ListBanners`・`Roll`・`Select`)と実装`ScoutConnection`(`ScoutApi`を呼ぶ。
  失敗はnull・falseで返し、ダイアログは`ApiCore`が出す)
- [x] `ScoutPage`/`ScoutPresenter`: 開催中バナーの先頭1件でスカウト、下部に候補10体(セルは`CellTemplate`を流用)、
  選択中の候補のステータスを左・技を右に表示(`PachimonInfoPanel`をネストし配置だけScoutPage側で変更)
- [x] 1回目のタップで選択、同じ候補の2回目のタップで確認Modal(`ScoutConfirmModal`「{パチモン名}\nで、確定しますか？」)→確定。
  ジェム不足ならスカウトボタンを押せない。候補を確定するまで戻るボタンを押せない(ジェム消費済みで取り直すAPIが無いため)
- [x] prefabは`Addressables/Views/Scout/`。Addressablesへの登録は手動(SmartAddresserのルールは自動適用されていない)
- [x] 実サーバーにつないだPlay Modeで、スカウト → 選択 → いいえ/はい → 入手(所持7体・ジェム300→150)→ Home復帰を確認
- [x] スカウト画面でパチモン名・タイプが見えない問題を修正(旧C-14)

#### バトル

- [x] `BattlePage`/`BattlePresenter`とViewの部品分割(`CommandView`/`SelfInfoView`/`OpponentInfoView`)
- [x] `MockBattleConnection`(BattleCoreをクライアント内で動かすオフライン対戦)
- [x] コマンドUI: たたかう/こうたい のメニュー、技4つ(タイプ・残りPP表示、PP0は押せない)
- [x] 交代UI(`SwitchSelectModal`、瀕死時の強制交代も同じModal)、行動送信後の入力ロック
- [x] 決着処理(`BattleResultModal`、投了確認Modal → `ForfeitAsync`)、Homeへ戻る
- [x] Home⇔Battleのシーン分離(マッチ情報の受け渡しはRoot常駐の`BattleEntryStore`)
- [x] 通信契約を`Shared/BattleContracts/`(`Atlas.BattleContracts`)に切り出してBattleServerと共有
- [x] マッチング: `MatchmakingModal`(キャンセル可) → `GrpcBattleMatchmaker`(`MatchmakingService/JoinQueue` + 1秒ごとの`GetQueueStatus`)
- [x] `RealtimeBattleConnection`(MagicOnion)でBattleServerへ接続。`RootLifetimeScope`の`Use Battle Server`でMockと切り替え
- [x] Multiplayer Play Mode(`com.unity.multiplayer.playmode` 3.0.0)の導入
- [x] PlayModeテスト`BattlePagePlayModeTests`(Mock接続: 対戦・交代・投了 → 結果 → Home復帰)
- [x] ターンの演出(テキスト): 行動を送るとコマンド欄を隠し、`turn_result`を行動順に1文ずつメッセージ枠に流す
  (HPもその文に合わせて更新。一定時間で自動送り・タップで早送り)。強制交代・相手の交代待ち・結果Modalは流し終えてから出す
  (design.md「6.8 バトル画面」)
- [x] HPゲージの演出: 技でHPが変わるとゲージと数値が少しずつ減り(LitMotion)、減り終わってから次の文へ進む。
  残りHPでゲージの色を変える(緑/黄(半分以下)/赤(2割以下))。交代先のパチモンは即時に切り替える。
  交代選択Modal・パーティ編成画面のHPゲージは従来どおり単色(緑)
- [x] Multiplayer Play Modeで、Unity Client同士の実サーバー(Server + BattleServer)対戦を確認(旧C-1)。
  マッチング → BattleServerへの接続 → 決着まで通った
- [x] 相手の場のパチモン名が、プレイヤー間で食い違う問題を修正。相手が既に公開済みの枠へ交代し直すと
  (`RevealedPachimon`がnull)、前に出ていたパチモンの名前のままになっていた。`BattlePresenter`で相手の
  選出各枠(PachimonId・HP)と場の枠番号を持ち、`NewActiveIndex`で引き直すようにした
- [x] 強制交代ターン: 相手が強制交代中は`OpponentSwitchingModal`(「相手がパチモンを選んでいます」)を出し、
  入力を止める。次のターン結果か決着で閉じる。`MockBattleConnection`も同じルールにした
  (プレイヤーの強制交代ターンでは簡易AIは行動せず、簡易AIの強制交代は続けて処理する)。
  EditModeテスト`MockBattleConnectionTests`で強制交代ターンの行動(交代とSkip)を確認
- [x] 選出画面(`SelectionModal`/`SelectionPresenter`): 参加後に届く`OnSelectionStart`で開き、自分のパーティから
  3体を選ぶ(選んだ順が選出の並び)。残り時間、相手のパーティ(種族のみ)、タップしたパチモンの詳細
  (`PachimonInfoView`)を表示する。「けってい」後は相手の選出が済む(`OnMatchStart`)までModalのまま待つ。
  マッチング時の自動選出(`BattleMatch.SelectedPlayerPachimonIds`)は廃止。候補・相手の枠はパーツprefab
  `SelectionCandidate`/`SelectionOpponent`。`MockBattleConnection`も参加時に固定パーティで`OnSelectionStart`を送る

### TODO

- [ ] **C-2 DBを作り直した後の、端末に残ったアカウント情報の扱い**
  - 期限前の再認証・401時のリトライはgRPC基盤に置き換えた
  - DBを作り直すと、端末のアカウント情報でトークンを発行できず、タイトルに戻り続ける(基盤の未確定・保留 #19)。
    それまでは保存先のファイルを手で消す(DEVELOPMENT.md)
- [ ] **C-3 自分側の切断→再接続**
  - 現状は一瞬切れただけで`DisconnectTimeout`負けになる
  - `RealtimeBattleConnection`で切断を検知し(`WaitForDisconnect`)、同じ`battleToken`/`matchId`で`JoinAsync`を再実行する(猶予60秒以内に数回)
  - 再送される`OnMatchStart`で盤面を戻す。再接続中は入力を止めて、その旨を表示する
  - `BattleStartPayload`には強制交代の状態が無いため、強制交代ターンの途中で再接続すると、自分の交代先の
    選択や`OpponentSwitchingModal`が出ない。契約(`Shared/BattleContracts/`)に状態を足すか、別の方法で伝える
- [ ] **C-4 対戦まわりの失敗時のエラーModal**
  - エラーModalを作り、BattleServerへの参加失敗・マッチング失敗・行動の送信失敗の3か所で表示する(現状はログのみ)
  - C-6の段階5(Bootstrap常駐の`ISystemDialog`)ができてから、それを使って出す(C-6の段階7に含む)
- [ ] **C-5 ターン制限時間と相手の切断中表示**
  - `TurnTimeLimitSeconds`をもとに残り秒数をカウントダウン表示する
  - `OnOpponentDisconnected`/`OnOpponentReconnected`を購読して「相手の接続を待っています」を表示する(イベントは`IBattleConnection`にあるが、Presenterでは未購読)
- [ ] **C-6 通信中のLoading**
  - 通信エラーのダイアログ・再試行・タイムアウト・二重送信の防止はgRPC基盤で済んだ(design.md「6.9」)
  - 残りは通信中のLoading(基盤の入力ブロックのフックを使う)と、捕まえ漏れの例外の通知
- [ ] **C-9 技の付け替え画面(View)**
  - 通信・Service層は実装済み(下記「パーティ編成」参照)。残っているのは画面(View/Presenter)のみ。
    パーティ編成の詳細パネル(`PachimonInfoView`)などから開けるようにする
  - Service層には「付け替え候補の技一覧」を返す口がまだ無い(`move_group_moves`のうち
    `is_initial=false`のもの)。画面実装時に`PachimonMoveLookup`同様のマスタ参照ヘルパーとして追加する
- [ ] **C-10 ニックネーム入力画面**
  - 新規作成時のニックネームが固定文字列「プレイヤー」になっている。入力画面を作って`PlayerService/SignUp`へ渡す
  - Homeは開発中の識別用にPlayerIdを表示しているので、ニックネーム表示へ戻すかも合わせて決める
- [ ] **C-11 IL2CPPビルド対応**
  - UnityにMessagePackのSource Generatorが入っておらず、`Atlas.BattleContracts`のPayloadは動的シリアライズ(Editor/Monoでのみ動作)
  - 実機・モバイル向けにはGeneratorの導入かResolverの事前生成が必要
- [ ] **C-13 パチモンのサムネイル画像**
  - 正式な画像は未作成。主タイプの色の円にpachimon_idを書いた仮の画像(`pachimon-thumbnail`スキルで生成)を
    パーティ・スカウト・対戦の画面に表示している。正式な画像は`Assets/Addressables/Thumbnail/{id}/pachimon_{id}_thumb.png`を差し替える
  - 一時的にシーン一覧で有効にすると動く(交代・投了の2件は成功)。残る`ClickingBattleButtonOnHome_PropagatesToBattleLogic`は、
    テストが引く`OpponentCanvas/LayoutRoot/Image/CurrentHpPercentage`がprefab側では`OpponentCanvas/LayoutRoot/CurrentHpPercentage`に
    なっていて`GameObject.Find`がnullを返すため失敗する(テストのパスが古い)
- [ ] **C-16 `AddressDefinition.cs`の再生成**
  - 現在の`AddressDefinition.cs`には`ScoutPage`/`ScoutConfirmModal`/`OpponentSwitchingModal`も含まれている。
    画面遷移のフォーク移行でPresenterの`[AssetAddress(AddressDefinition.Xxx)]`がこの定数を使うようになったため、
    Page/Modal prefabを追加したら必ず再生成する(定数が無いとコンパイルエラーで気づける)

---

## 2. APIサーバー(Rust/Axum)

`Server/`。詳細は[Server/CLAUDE.md](../../Server/CLAUDE.md)、[design.md](design.md)「3. APIサーバー」。

### 実施済み

#### API

gRPC(クライアント向け。`tests/{player,party,scout}_grpc_test.rs`で結合テスト)

| サービス / RPC | 概要 |
|---|---|
| `DeviceService/Register`・`IssueToken` | デバイス登録・アクセストークン発行(gRPC基盤) |
| `PlayerService/SignUp` | プレイヤー作成(スターター編成・初期ジェム300を同一トランザクションで付与。作成済みなら作成済みを返す) |
| `PlayerService/SignIn` | プレイヤー情報 + 所持データ全件(`snapshot`)。未作成なら`PLAYER_NOT_FOUND` |
| `PartyService/SetParty` | パーティの全置き換え(`player_diff.party_slots`) |
| `PartyService/SetPachimonMove` | 技の付け替え(`player_diff.pachimon_move_map`) |
| `ScoutService/ListBanners` | 開催中のバナー一覧(排出率は返さない) |
| `ScoutService/Roll` | ジェムを消費して候補10体をロール(`player_diff.items`) |
| `ScoutService/Select` | 候補から1体を入手(`player_diff.pachimon`・`pachimon_move_map`) |

REST(BattleServer向けの内部API。`tests/battle_api_test.rs`で結合テスト)

| メソッド | パス | 概要 |
|---|---|---|
| POST | /internal/battle/result | 対戦結果の記録 |
| POST | /internal/battle/loadouts | 選出個体の所持データ取得 |
| POST | /internal/battle/party | 参加者のパーティ取得 |

クライアント向けのREST APIはすべて消した(チャットはgRPCに移さずに機能ごと消した)。

#### 機能・仕組み

- [x] デバイス認証・トークン・再送しても一度だけ処理する仕組み・メンテナンスの判定はgRPC基盤のLayer(`AuthLayer`等)。
  Atlasは`PlayerResolver`(`PlayerLookup`)でプレイヤーが作成済みかを返す
- [x] `player_diff`共通レスポンス項目(items / pachimon / pachimon_move_map / party_slots)。書き込む関数が`PlayerDiffCollector`に記録する
- [x] 所持リソースを`player_items`に一般化(`players.gems`列を廃止)
- [x] スターター編成の自動付与(`starter_party_slots`マスタを複製。個体生成は`player_pachimon_service::grant`でスカウトと共通化)
- [x] スカウト(レアリティの重み付き抽選 → パチモンを等確率で選出 → 初期技を確定。ジェム減算はトランザクション)
- [x] パーティ編成は`player_party_slots`テーブル(nullableを使わない設計)、技は`player_pachimon_moves`(ULID主キー)
- [x] 個体値(IV)の概念を廃止(種族値 + 努力値のみ、レベル50固定)
- [x] マッチング: プロセスメモリの待機列、`battle_token`はHS256のJWT(30秒)、成立時に`battle_matches`を作成。
  成立の結果は状況の確認で返しても消さず、次の参加・離脱・結果報告で消す
- [x] 内部API: `X-Internal-Secret`を定数時間比較。結果記録は1トランザクション(`FOR UPDATE`で二重報告は409)、勝者にジェム50
- [x] 勝者なしの結果(`winnerId`空文字)は`aborted`。結果報告が1時間届かない対戦は5分ごとの定期処理で`aborted`にする
- [x] マスタは起動時にDBからメモリキャッシュへ読み込む(`src/master/cache.rs`)。投入は`cargo run --bin seed_master_data`、バナーは`seed_scout_banners`
- [x] 開発用の通信ログ(`tracing`、`RUST_LOG`でボディ・SQLも出力。`secretKey`/`accessToken`は伏字)
- [x] 開発用API(`DebugService`)は環境変数`ENABLE_DEBUG_API`で登録するかを決める(未設定ならデバッグビルドだけ)
- [x] マイグレーション25本、結合テスト48件(`tests/{player,party,scout,matchmaking,debug}_grpc_test.rs`・`battle_api_test.rs`・`master_data_test.rs`)
- [x] `type: int`を32bit(Rust `i32` / DB `INT`)に統一し、API DTOの`pachimonId`/`moveId`/`itemId`も`i32`に揃えた

### TODO

- [ ] **S-1 `sqlx::test`の結合テストが`PoolTimedOut`で失敗する**
  - `failed to connect to setup test database: PoolTimedOut`で失敗する。直近の変更前のコードでも同じなので、ローカル環境側の問題と思われる
  - 原因は未調査。直るまでは「テスト62件」が今の環境で全部通るとは言えない
- [ ] **S-3 ショップ・育成チケット・ステータスポイント**
  - ショップ: 購入できる商品の一覧と購入のAPI。ジェムを減らしてチケットを増やし、`playerDiff`の`items`で両方を返す(スカウトと同じ流れで、抽選と履歴は無い)
  - 技の付け替え(`POST /edit/pachimon_moves`)でチケットを1枚消費する
  - ステータスポイントの割り振りのAPI(チケット1枚、64ポイントを6能力に上限なし)。`effort_values`に保存し、BattleServerの
    `LoadoutBuilder`の計算式を`floor(努力値 / 4)`からポイントをそのまま足す形に変える
- [ ] **S-2 APIサーバーのコンテナ化**
  - Dockerfileのマルチステージビルド + `SQLX_OFFLINE`(`.sqlx`オフラインキャッシュ)、composeのprofileで開発時の`cargo run`と併用する
  - Windows上のDockerではビルドが遅くデバッグもしにくいため見送り中。デプロイ方式を決めるときに行う(design.md「7.1 構成」のBattleServer側も同じ判断)

---

## 3. バトルサーバー(C#/MagicOnion)

`BattleServer/`(ASP.NET Core + `MagicOnion.Server` 7.11.0)と、共通のバトルロジック`Shared/BattleCore/`(`Atlas.BattleCore`)。
設計は[design.md](design.md)「7. バトルサーバー」。

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
- [x] 選出フェーズ: 参加時に`/internal/battle/party`でパーティを取得し、両者が揃ったら`OnSelectionStart`で
  自分のパーティ・相手のパーティ(種族のみ)・選出数・残り秒数を送る。選出中の再接続では再接続者にだけ再送。
  選出がパーティ外の個体なら`InvalidArgument`。BattleBotは`OnSelectionStart`を受けてパーティからランダムに3体を選ぶ
- [x] マスターデータの読み込み(`BattleServer/MasterData/`、起動時に`MemoryDatabase`)
- [x] `BattleTimingOptions`で制限時間類を差し替え可能に(テスト用)
- [x] 対戦相手ボット`BattleServer/BattleBot/`(gRPC + MagicOnionで自動対戦、`--loop`/`--count`)。gRPCのクライアントは
  `.proto`から`Grpc.Tools`で生成する(基盤のデバイス登録の`.proto`はビルドのときにタグの版を取ってくる)
- [x] ボットのパーティをランダム化(開発用API`DebugService/RandomizeParty`: マスタから重複なし6体、技は候補技からランダム)
- [x] 自動テスト(xUnit、`dotnet test BattleServer.slnx`): インプロセス起動した結合テスト、`LoadoutBuilderTests`、`ApiParticipantDataSourceTests`、`MasterDatabaseFactoryTests`
- [x] ボット2体で実サーバーを通した対戦(マッチング → 強制交代 → 全滅決着 → 結果記録)を確認
- [x] 強制交代ターン: 前のターンで瀕死になった側の交代だけを受け付け、相手の行動は`FailedPrecondition`で拒否する。
  交代が届いた時点でターンを解決し、時間切れは倒れた側の敗北(`Forfeit`)。放置の回数には数えない。
  `Atlas.BattleCore`は変更なし(design.md「7.4 対戦の進行」)。BattleBotも相手の強制交代中は待つようにした。
  結合テストに`ForcedSwitchTimeout_PlayerWhoDidNotSwitchLoses`・`ForcedSwitchTurn_DoesNotCountAsIdleForWaitingPlayer`を追加
- [x] ローカル一括起動`make dev`(リポジトリ直下の`Makefile`): MySQL起動+マイグレーション → Server・BattleServerを並列起動。
  BattleServerの共有シークレットは`Server/.env`の値を環境変数で渡すため、user-secretsの設定は不要になった(旧B-1)。
  BattleServerは.NET 10 SDKを明示して起動する(Git BashのPATHではUnity付属の.NET 8 SDKが先に見つかるため)

### TODO

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
- [x] `copy-models`/`copy-client-bytes`でUnity側の`.meta`が消える問題を修正(旧M-3)。
  `copy_dir_contents`に`preserve_meta`引数を追加し、対応する実体ファイルが引き続きコピーされる
  `.meta`はGUIDを維持したまま残す(実体が無くなった孤児`.meta`は従来どおり削除)。
  Unity向け(client)の呼び出しのみ有効化。BattleServer向け(realtime)はUnityプロジェクトでは
  ないため未使用

#### マスターデータ内容

| テーブル | 件数 | 内容 |
|---|---|---|
| `pachimon` | 136 | ID 1001〜1136。S 23体、A 34体、B 56体、C 23体。1037〜1136は合計種族値でレア度を決めている |
| `move_groups` | 136 | 現状は1パチモン1グループ |
| `moves` | 261 | 専用技36、汎用技2(19/20)、汎用タイプ技26(39〜64)、付け替え候補の物理技106(65〜170)・特殊技91(171〜261)。状態技は0件 |
| `move_group_moves` | 4403 | 各グループに初期技4つ + 付け替え候補(自分のタイプと補完タイプの技)。1グループ20〜48件 |
| `type_chart` | 324 | 18×18タイプ(第9世代準拠) |
| `starter_party_slots` | 6 | 新規プレイヤーに付与するスターター(rarity C の6体) |
| `items` | 2 | `item_id: 1` = ジェム、`item_id: 2` = 育成チケット |
| `shop_products` | 1 | 育成チケット1枚 = 50ジェム。サーバーのみに出力 |

- Serverでメモリキャッシュする対象は`pachimon`・`move_group_moves`・`starter_party_slots`・`items`(`src/master/cache.rs`)。`moves`/`move_groups`はDB投入のみ。`type_chart`はServer向けJSONを生成するだけで、テーブルもDB投入も無い(タイプ相性はBattleCore側で使う)

### TODO

- [ ] **M-1 技の拡充**
  - 物理・特殊技は付け替え候補を追加済み。状態技が0件
  - 状態技を入れる場合はB-2(追加効果)と合わせて行う
- [ ] **M-2 `base_power`のNULL代替(0埋め)の検証**
  - 状態技が無いため、`base_power = 0`の運用を実データで確かめていない。M-1のときに確認する
- [ ] **M-5 CSVに入力したマスタの反映**
  - パチモン(1037〜1136)・技(65〜261)・育成チケット・`shop_products`はCSVとスキーマの入力だけで、パイプラインでの生成・配置と
    `seed_master_data`での投入をしていない
  - `shop_products`は新しいテーブルなので、マイグレーションと`seed_master_data`への追加が要る(`master-data-schema-add`スキル)
  - `Server/src/master/mod.rs`のテストが件数を決め打ちしている(パチモン36・技64・`move_group_moves` 180など)ので直す
  - パチモン1037〜1136の仮のサムネイルを`pachimon-thumbnail`スキルで作り、Addressablesに登録する(C-13)
