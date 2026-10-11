---
title: "命名とID"
status: current
area: conventions
related: [db/index, conventions/player-diff]
code: [.editorconfig, BattleServer/Directory.Build.props]
updated: 2026-10-11
---

# 命名とID

## 命名

| 対象 | 規則 |
|---|---|
| DB・Rust | `snake_case`。マスタは修飾語なし、プレイヤーの所持データは `player_` を付ける(`pachimon` / `player_pachimon`) |
| `.proto` | `buf lint`(STANDARD)の規約どおり。フィールドは `snake_case`(C# では `PascalCase` になる) |
| C#(Client・BattleServer・`Shared/`) | 型・メソッド・プロパティは `PascalCase`。private フィールドは `camelCase`(アンダースコアを付けない)。private の `const`・`static readonly` は `PascalCase` |
| JSON の列(`battle_turns` の `action_data`・`result_data` など) | `camelCase` |

- 「ポケモン」にあたる語は `pachimon` で統一する(商標と混同しないための独自名)
- C# の命名規則はリポジトリ直下の `.editorconfig` にある。Rider で警告になり、BattleServer は `Directory.Build.props` の
  `EnforceCodeStyleInBuild` で `dotnet build` でも警告(IDE1006)になる
- Client の画面ごとの型は画面名をプレフィックスにそろえる([画面の構成](../client/screens.md))

## ID

| 種類 | 形 | 例 |
|---|---|---|
| プレイヤーの操作で生成するデータ | ULID(26文字、生成順に並ぶ)。DB は `CHAR(26)`。Rust は `ulid` クレートで生成 | `player_pachimon_id`・`roll_id`・`match_id` |
| 所持データの「割当」 | 行ごとに ULID を持つ独立したエンティティ | パーティの枠(`party_slot_id`)・覚えている技(`player_pachimon_move_id`) |
| マスタ | 整数 | `pachimon_id` は1001〜の4桁 |
| `player_id` | gRPC 基盤のデバイス登録が発行する ULID | 基盤のテーブルでは `BINARY(16)`、Atlas のテーブルでは `CHAR(26)` |

- 割当にも ID を持たせるのは、[所持データの差分](player-diff.md) の `removed` で行を ID で指せるようにするため
- 例外は所持アイテム(`player_items`)で、`(player_id, item_id)` の複合主キーにしている。個々の行を独立したエンティティとして
  指す必要が無いため
