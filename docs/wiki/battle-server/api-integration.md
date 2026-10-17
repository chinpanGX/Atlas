---
title: "APIサーバーとの連携"
status: current
area: battle-server
related: [server/internal-api, server/stale-match-cleanup, battle-server/battle-flow, master-data/pipeline]
proto: [Shared/proto/atlas/internal/v1/battle.proto]
code: [BattleServer/Internal/ApiParticipantDataSource.cs, BattleServer/Internal/BattleResultReporter.cs, BattleServer/Program.cs, BattleServer/BattleServer.csproj, BattleServer/Battle/MasterDatabaseFactory.cs]
updated: 2026-10-18
---

# APIサーバーとの連携

## 内部 API の呼び出し

- APIサーバーの内部 API(`atlas.internal.v1.BattleInternalService`)を gRPC で呼ぶ。クライアントは `Shared/proto/atlas/internal/v1/battle.proto` から
  `Grpc.Tools` でビルド時に生成する
- 接続先は `API_SERVER_URL`(既定 `http://127.0.0.1:3000`)。`GrpcChannel` を1つ使い回し、HTTP/2 の接続を共有する
- すべての呼び出しにメタデータ `x-internal-secret`(`INTERNAL_API_SECRET`)を付ける。無いと対戦を始められないため、未設定なら起動しない

| 呼ぶとき | RPC | 使い方 |
|---|---|---|
| 参加 | `GetParty` | 選出画面で見せるパーティ |
| 選出 | `GetLoadouts` | 選出個体のステータスポイントと技。`NOT_FOUND` は「所持していない個体がある」として選出を拒否する |
| 決着 | `ReportResult` | 結果と行動ログ |

## 結果報告(`BattleResultReporter`)

- 報告の完了は待たずに進める
- 一時的な失敗(`Unavailable`・`DeadlineExceeded`・`Internal`・`Unknown`・`ResourceExhausted`・`Aborted`)は、1秒・5秒・15秒の間隔で再送する
- `ALREADY_EXISTS`(記録済み)は成功扱い。それ以外の失敗は再送しない
- 再送し切っても失敗した場合の保存はしていない(APIサーバーの [後始末](../server/stale-match-cleanup.md) で `aborted` になる)
- 報告するターンのログ(`action_data`・`result_data`)には、クライアントに送らない HP の実数やダメージ量も含める。JSON(camelCase)にして送る

## マスターデータ

起動時に `MasterData/masterdata.bytes`(ビルド時に出力先へコピー、パスは `MasterData:Path` で変更可)を1回読み込み、`MemoryDatabase` を
シングルトンで持つ。読み込めなければ起動しない。DB や APIサーバーには問い合わせない。種族値・技の性能・タイプ相性はここから引く
([マスターデータの生成・配布・運用](../master-data/pipeline.md))。

## 採用しなかった案

- バトルサーバーが MySQL を直接読む: DB へのアクセスを APIサーバーにまとめる方針から外れるため
- マッチ成立時に `battle_token` へパーティ全員分の所持データを入れる: 使わない個体の分までトークンに載り、トークンの役割(参加資格の証明)とも混ざるため
