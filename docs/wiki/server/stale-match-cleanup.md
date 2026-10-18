---
title: "結果報告が届かない対戦の後始末"
status: current
area: server
related: [server/internal-api, battle-server/api-integration, db/index]
code: [Server/src/service/battle_service.rs, Server/src/main.rs]
updated: 2026-10-11
---

# 結果報告が届かない対戦の後始末

次の場合、`battle_matches` が `in_progress` のまま残る。

- バトルサーバーの再起動・クラッシュ(対戦の状態はメモリにしか無い)
- 両者とも一度もバトルサーバーに接続しなかった
- 結果報告の再送がすべて失敗した

APIサーバーは5分ごとに、開始(`started_at`)から1時間以上経った `in_progress` の対戦を `aborted` にする
(`battle_service::spawn_stale_match_cleanup`。APIサーバー本体だけが起動し、テストでは起動しない)。

対戦時間に上限は無いため、1時間を超える正当な対戦も打ち切られ得る。打ち切った後に届いた結果報告は受け付けて上書きする
([内部API](internal-api.md))ので、一時的に `aborted` と表示されるだけで済む。

## 採用しなかった案

- バトルサーバーの起動時に、進行中の対戦をすべて打ち切るよう通知する: 再起動のケースは早く片付くが、定期処理だけで全ケースを賄えるため見送った
