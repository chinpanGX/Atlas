---
title: "バトル画面"
status: current
area: client
related: [game/battle-rules, battle-server/contracts, battle-server/battle-flow, client/screens]
code: [Client/AtlasUnityProject/Assets/Scripts/Presentation/Battle, Client/AtlasUnityProject/Assets/Scripts/Domain/IBattleConnection.cs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Realtime]
updated: 2026-10-18
---

# バトル画面

`BattlePresenter` が `IBattleConnection` を持つ唯一のクラスで、View は `IBattleConnection` や `Atlas.BattleCore` の型を知らない。
接続の型(`IBattleConnection` と Payload)は `Domain/IBattleConnection.cs` を正とし、バトルサーバーとの
[通信契約](../battle-server/contracts.md) と対になる。`RealtimeBattleConnection` が型を詰め替えるだけ。

## 流れ

1. Battle シーンで `BattlePage` を開き、画面を先に出してからバトルサーバーに参加する(`JoinAsync`)。参加に失敗したら、ダイアログで知らせて Home へ戻る
2. `OnSelectionStart` で選出画面(`SelectionModal`)を開く
3. 「けってい」で `SubmitSelectionAsync` を送り、「相手の 選出を 待っています…」を出して選出画面のまま待つ。送信に失敗したら、
   選んだ内容を残したまま押し直せるようにする。選ばずに閉じられたら開き直す
4. `OnMatchStart` で選出画面を閉じ、対戦の画面を初期化する
5. ターンごとに行動を送り、`OnTurnResult` を演出してから次の入力を受け付ける
6. `OnBattleEnd` で結果の Modal(`BattleResultModal`)を出し、Home へ戻る

## 選出画面(`SelectionModal` / `SelectionPresenter`)

- 上に残り時間、左に自分のパーティ(タップで選出に入れる/外す、選んだ順番1〜3を表示)、中央に最後にタップしたパチモンの詳細
  (パーティ編成と同じ `PachimonInfoView`)、右に相手のパーティ、右下に「けってい」
- 「けってい」は、選出数(パーティが3体未満ならその体数)を選び終えていて、残り時間があるときだけ押せる
- 詳細の技は `OnSelectionStart` に含まれないため、手元の所持データ(`IPachimonMoveMappingService`)から出す(選出中はパーティ編成・
  技の付け替えができないため、バトルサーバーが使う技と食い違わない)
- 残り時間はクライアントで1秒ずつ減らして表示するだけで、時間切れの判定はバトルサーバーが行う
- `SelectionPresenter` は選んだ内容を `OnConfirmed` で流すだけで、送信は `BattlePresenter` が行う。自分からは閉じず、
  `BattlePresenter` が対戦の開始・決着で閉じる

## 状態の持ち方

- Payload は全体のスナップショットではなくターンごとの差分なので、`BattlePresenter` が自分と相手の選出各枠の状態(種族・HP%・瀕死・場の枠)を
  ローカルに持って更新する。`OnMatchStart` で初期化し(再接続時の再送もこれで復元)、`OnTurnResult` の各行動で更新する
- 相手の枠は、場に出て公開されるまで種族が分からない。一度公開された枠に交代で戻ったときは `RevealedPachimon` が送られないため、
  枠ごとに覚えておき `NewActiveIndex` で引き直す
- 技の表示・送信は、手元の所持データではなく `OnMatchStart` の `SelfMoves`(バトルサーバーが判定に使う技)を使う
- 自分の個体の能力(最大 HP・詳細の能力値)は、ステータスポイントが `OnMatchStart` に含まれないため、手元の所持データ(`IPachimonService`)の
  割り振りを足して計算する。対戦中は育成できないため、バトルサーバーが判定に使う値と食い違わない。手元に無い個体(Mock 対戦)は割り振り0として扱う
- 残り PP は `SelfMoves` の値から、自分が技を使うたびにローカルで減らす(命中・外れに関わらず1減る)。PP が0の技のボタンは押せなくする

## 入力と演出

- 行動を送るとコマンドを隠し、「相手の 行動を 待っています…」を出す。ターン結果が届いたら行動順に1文ずつメッセージを流し、HP・場の
  パチモンの表示もその文に合わせて更新する。流し終えてからコマンドを戻す
  - 1文は1.2秒で自動的に送る。タップで早送り
  - 技: 「(相手の)X の 技名!」→ 技の演出と HP ゲージ → 外れ/急所 → 効果(超ばつぐん・ばつぐん・いまひとつ・超いまひとつ・効果なし)→ 倒れた
  - 交代: 「ゆけっ! X!」/「相手は X を くりだした!」
  - 何もしなかった: 「X は 行動できなかった!」。強制交代ターンで待っていた側と、同じターンに先に倒れた側は出さない
  - 文言は `BattleMessageBuilder` にまとめる
- 演出中に次のターン結果や決着が届いても割り込ませず、届いた順に後ろへ並べる
- HP ゲージ(`HpGaugeView`): 技で HP が変わったときは少しずつ減らし(LitMotion、ゲージ全体で1秒・最短0.25秒)、減り終わってから次の文へ進む。
  交代で替わったときは即時に切り替える。残り HP で色を変える(半分より多い = 緑、半分以下 = 黄、2割以下 = 赤。`UiPalette` で管理)
- 技の演出(`BattleStageView`): 場のパチモンをサムネイルで表し、それを動かす。サムネイルが読み込めるまでと相手の未公開のパチモンは、
  主タイプの色の円(`PachimonTypePalette`。未公開は灰色)を出す
  - 物理技は攻撃側が相手へ体当たりし、特殊技は攻撃側から相手へ技のタイプの色のビームを伸ばす。相手に届いた瞬間から、攻撃側の戻り・
    攻撃を受けた側の反応・HP ゲージの減少を同時に流す
  - 攻撃を受けた側は揺れながら点滅する。揺れの大きさは相性(超いまひとつ < いまひとつ < 等倍 < ばつぐん < 超ばつぐん)で変え、急所は1.5倍。
    ばつぐん・超ばつぐん・急所のときは画面全体も揺らす。外れ・効果なしは反応を出さない
  - 倒れたときは沈みながら消え、繰り出したときは弾むように現れる

## 交代と投了

- 交代: 交代ボタン → `SwitchSelectModal` で選出3体から選ぶ。場に出ているパチモンと瀕死のパチモンは選べない
  (BattleCore は場のパチモン自身への交代を検証しないため、UI で防ぐ)
- 強制交代: 自分が対象なら同じ Modal を「もどる」無しで出す(生存が1体でも自動では選ばない)。相手が対象なら
  `OpponentSwitchingModal`(「相手がパチモンを選んでいます」)を出して入力を止め、次のターン結果か決着で閉じる
- 交代先を選んでいる間にターン結果(時間切れのスキップ等)や決着が届いたら、Modal を閉じる
- 投了は確認の Modal(`ForfeitConfirmModal`)を経て `ForfeitAsync`。決着は `OnBattleEnd` で届くので、結果の表示は投了と共通
- Modal は演出を流し終えてから出す(強制交代・相手の交代待ち・結果)

## ターンの残り時間

- 画面の上にターンの残り秒数を出す。`OnMatchStart` の `TurnTimeLimitSeconds` から1秒ずつ減らして表示するだけで、時間切れの判定は
  バトルサーバーが行う
- サーバーがターンの制限時間を張り直す時点に合わせて数え始める: 対戦の開始・再送(`OnMatchStart`)、ターン結果が届いたとき、
  相手が戻ったとき(`OnOpponentReconnected`)。ターン結果は演出を流し終えてからではなく、届いた時点から数える(サーバーはその時点から数えているため)
- どちらかが切断している間はサーバーがターンを止めるため、残り時間も隠す

## 切断と再接続

- 相手の切断中(`OnOpponentDisconnected` 〜 `OnOpponentReconnected`)は、画面上部の帯に「相手の 接続を 待っています…」を出し、
  入力を止める(猶予中はサーバーがどちらの行動も受け付けないため)
- 自分の接続が切れたら、`RealtimeBattleConnection` が同じ `battleToken`/`matchId` で接続と参加をやり直す(1・2・4・8秒…の間隔で、
  サーバーの猶予60秒より短い50秒まで)。その間は「再接続しています…」を出して入力を止める
  - 黙って途切れた通信にも気付けるよう、MagicOnion のクライアントのハートビート(5秒ごと、10秒で切断扱い)を使う
  - `AlreadyJoined` はサーバーがまだ前の接続の切断に気付いていないだけなので、待って送り直す。`MatchNotFound`・`InvalidToken` は諦める
- 参加し直すと、サーバーが今の状態を `OnSelectionStart`/`OnMatchStart` で送り直す。対戦中は、流している演出の後ろに並べてから、盤面と
  ターンの途中の状態(強制交代を求められているか・行動を送信済みか・相手が切断中か)を置き換え、開いていた交代先の選択は閉じて選び直させる。
  再送が届くまでは、手元の状態が古い可能性があるため入力を止めたままにする
- 諦めたら、ダイアログ(「通信が 切れました」)を出して Home へ戻る。決着後は参加し直さない

## 失敗の知らせ方

- バトルサーバーとの通信(MagicOnion)は、APIサーバーの通信の共通処理(`ApiCore`)を通らないため、`BattlePresenter` が自分で扱う。
  知らせるのは、Bootstrap に常駐する通信エラーのダイアログを `ISystemDialog` として使って出す([通信エラーのダイアログ](error-dialog.md))
  - 参加の失敗: 理由(トークンの期限切れ・対戦が終わっている・接続できない)を出して Home へ戻る
  - 選出・行動・投了の送信の失敗: 知らせて画面に留まり、選び直させる(強制交代は交代先の選択を開き直す)。自分の接続が切れたための失敗は、
    参加し直した後の再送で戻すため出さない
- マッチングの失敗は API の通信なので、通信の共通処理がダイアログを出す

## まだ無いもの

- 対戦結果(勝敗・報酬)を Home へ戻ったときにどう見せるか([未確定・保留](../open-questions.md))
