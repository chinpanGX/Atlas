---
title: "マスターデータの生成・配布・運用"
status: current
area: master-data
related: [master-data/tables, server/structure, client/scenes-and-scopes, battle-server/structure]
code: [Shared/master-data, master-data-pipeline, Server/src/master, Server/src/bin/seed_master_data.rs, Client/AtlasUnityProject/Assets/Scripts/MasterData, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/MasterData, BattleServer/MasterData, BattleServer/Battle/MasterDatabaseFactory.cs]
updated: 2026-10-16
---

# マスターデータの生成・配布・運用

## 配布先

| 配布先 | 生成物 | 読み込み方 |
|---|---|---|
| Client | C# の型(Models/Enums、MasterMemory + MessagePack)、ローダー、`masterdata.bytes`(暗号化) | 起動時の初期化(`MasterDataInitializationTask`)で `MasterDatabaseLoader` が Addressables から読み込む。`MemoryDatabase` は Root のスコープに、読み込み済みのものを返すシングルトンとして登録してあり、Presenter 等はコンストラクタで受け取る(初期化より前に要求すると例外) |
| バトルサーバー | Client と同じ C# の型・ローダー・`masterdata.bytes`(`BattleServer/MasterData/`) | 起動時にファイル(`MasterData:Path` で変更可)から1回読み込み、`MemoryDatabase` をシングルトンで持つ。読み込めなければ起動しない。DB や APIサーバーには問い合わせない |
| APIサーバー | Rust の型(`src/master/generated/`)と JSON(`master_data/`) | `seed_master_data` で DB へ UPSERT し、サーバーの起動時に DB から全件読み込んで `Arc<MasterData>` としてメモリに持つ |

- Client と BattleServer には同じ生成物をそれぞれにコピーする。共有パッケージは作らない。どちらも正本から毎回生成されるため
  食い違わず、BattleServer が Client のフォルダを参照すると、サーバーからクライアントへの依存になるため
- C# の型・ローダーは1つのアセンブリにまとめる(Client は `Atlas.MasterData.asmdef`、BattleServer は `Atlas.MasterData.csproj`)。
  MasterMemory・MessagePack の Source Generator が同じアセンブリの中でしか働かないため
- 生成される型は「テーブル名の PascalCase + `Data`」(`pachimon` → `PachimonData`)で、`partial`。ゲームロジックはこの型を
  直接使わず、BattleCore の型(`ParticipantStats`/`MoveData`)に変換して使う([バトルの計算](../battle-server/battle-core.md))
- APIサーバーが読み込むのは `pachimon`・`move_group_moves`・`starter_party_slots`・`items` だけ。`moves`・`move_groups` は技の ID しか
  扱わないロジックからは参照しないので、DB にあれば足りる。JSON はバイナリに埋め込み、`seed_master_data` だけが使う

## 配布先の絞り込み(targets)

テーブルごとに配布先を決められる。サーバーだけで使うものは Client の `masterdata.bytes` に含めない。

| テーブル | 配布先 | 理由 |
|---|---|---|
| `move_groups` | サーバーだけ | Client は `pachimon.move_group_id` から `move_group_moves` を直接引くので、グループ自体は要らない |
| `starter_party_slots` | サーバーだけ | 新規プレイヤーの作成はサーバーだけで行う |
| `shop_products` | サーバーだけ | 購入の可否と支払う数はサーバーが判定する。購入できる商品は通信で返す(追加予定) |
| `scout_banners`・`scout_banner_rates` | サーバーだけ | 開催中のバナーは通信で返し、排出率(抽選の生の値)はクライアントに配らない |
| `type_chart` | Client・バトルサーバー(DB には入れない) | ダメージを計算するのは BattleCore だけで、APIサーバーには不要 |
| ほか | すべて | |

## パイプラインの制約

型は `int`/`long`/`string`/`bool`/`enum` だけで、小数・nullable・複合主キー・日時を持てない。スキーマはこれに合わせる。
パイプラインの `validate` は一意と参照(`check_relation`)だけを確かめるので、それ以外の決まり(日時の形・合計値など)は `seed_master_data` が投入の前に確かめ、合わなければ何も投入しない。

| 制約 | 合わせ方 |
|---|---|
| 小数が無い | タイプ相性は倍率ではなく区分(`type_effectiveness`)で持ち、倍率への変換は BattleCore で行う |
| nullable が無い | 2つ目のタイプが無いときは `NONE`、変化技の威力は0 |
| 複合主キーが無い | 代理キーを置く(`move_group_moves.unique_id`・`type_chart.type_chart_id`) |
| 日時が無い | `string` で持ち、形式を UTC の `YYYY-MM-DD HH:MM:SS` に固定する。日時として読めることと前後関係は `seed_master_data` が確かめる(スカウトのバナーの開催期間) |

## 運用

| 配布先 | 更新の手順 |
|---|---|
| APIサーバー | `cargo run --bin seed_master_data` で DB へ UPSERT し、サーバーを再起動する |
| Client・バトルサーバー | パイプラインで生成して配置し、再起動する(Client はビルドし直す) |

- 生成と配置の手順は `master-data-pipeline` スキル、テーブル・Enum の追加は `master-data-schema-add` スキル
- `seed_master_data` は外部キーを満たす順(`move_groups` → `moves` → `move_group_moves` → `pachimon` → `starter_party_slots` → `items` → `scout_banners`・`scout_banner_rates`)に投入する
- 無停止での反映は、1台構成の規模では過剰なため入れない
- Client への配布を CDN 経由にする案は保留([未確定・保留](../open-questions.md))
