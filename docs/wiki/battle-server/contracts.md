---
title: "通信契約(Atlas.BattleContracts)"
status: current
area: battle-server
related: [battle-server/battle-flow, client/battle-screen, features/battle]
code: [Shared/BattleContracts/Runtime/IBattleHub.cs, Shared/BattleContracts/Runtime/BattlePayloads.cs, Client/AtlasUnityProject/Assets/Scripts/Domain/IBattleConnection.cs]
updated: 2026-10-18
---

# 通信契約(Atlas.BattleContracts)

`Shared/BattleContracts/Runtime/` の `IBattleHub.cs` と `BattlePayloads.cs` を正とする(MessagePack でシリアライズ)。
Client 側の `IBattleConnection` はこれと対になる形で、`RealtimeBattleConnection` が型を詰め替えるだけ。

## Client → Server(`IBattleHub`)

| メソッド | 内容 |
|---|---|
| `JoinAsync(battleToken, matchId)` | 参加(再接続も同じ)。結果は `Success`/`InvalidToken`/`MatchNotFound`/`AlreadyJoined` |
| `SubmitSelectionAsync(playerPachimonIds)` | 選出(1〜3体、選んだ順) |
| `SubmitMoveAsync(move)` | 技を使う |
| `SwitchAsync(partySlot)` | 交代(選出の中の位置、0始まり) |
| `ForfeitAsync()` | 降参 |
| `ReadyForTurnAsync()` | ターン結果の演出を流し終え、行動を入力できる状態になった(対戦開始・再接続の後も送る) |

## Server → Client(`IBattleHubReceiver`)

| メソッド | 内容 |
|---|---|
| `OnSelectionStart` | 選出の開始。自分のパーティ(個体 ID・種族)、相手のパーティ(種族だけ)、選出数、残り秒数、送信済みか |
| `OnMatchStart` | 対戦の開始(再接続時の盤面の再送も兼ねる)。自分・相手の選出各枠、場の枠、ターンの制限時間、自分の技と残り PP。再送用に、強制交代を求められている側・行動を送信済みか・相手が切断中か |
| `OnTurnResult` | ターンの結果。行動順の各行動(`Move`/`Switch`/`Skip`)と、次のターンで強制交代が要るプレイヤー |
| `OnTurnStart` | ターンの制限時間を数え始めた(両者の準備がそろった、または準備待ちの上限を過ぎた)。制限時間の秒数 |
| `OnBattleEnd` | 決着。勝者の ID(勝者なしは空文字)と理由(`AllFainted`/`Forfeit`/`DisconnectTimeout`) |
| `OnOpponentDisconnected` / `OnOpponentReconnected` | 相手の切断・再接続 |

## 情報の見せ方

判定はサーバーだけが持ち、クライアントには結果だけを送る。

- HP は自分・相手とも0〜100の整数の%で送る(生きていれば最低1)。実数やダメージ量を送ると計算式の定数を逆算されやすいため
- 相手の選出は、場に出るまで種族を送らない(枠の数と瀕死の数は分かる)。一度公開した枠は公開のまま。初めて場に出た枠は、
  交代の結果に `RevealedPachimon` として入れる
- 技は自分の分だけ送る。Client は手元の所持データではなくこれを表示・送信し、サーバーが判定に使う技と常に一致させる
- 選出画面で見せる相手のパーティは種族だけ(個体 ID・技・ステータスポイントは送らない)

## 変えるとき

- Payload は `[MessagePackObject]` と `[property: Key(n)]` で番号を付ける。Client と同時に更新されないことを考え、既存のキーの番号は変えない
- 変えたら、Client 側の `IBattleConnection`・`RealtimeBattleConnection`、BattleBot、テストの `TestReceiver` も合わせて直す
  (`IBattleHubReceiver` のメソッドを足すと、実装していない側はコンパイルエラーになる)
