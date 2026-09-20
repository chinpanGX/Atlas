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
- [x] クライアント向け生成・配置(`Client/Assets/Scripts/Domain.MasterData/*`, `masterdata.bytes`)
- [x] サーバー向け生成・配置(`Server/src/master/generated/*.rs`, `Server/master_data/*.json`)
- [x] `cargo check`によるコンパイル確認
- [ ] realtime_server(MagicOnion)プロジェクトが未作成のため、`copy_destinations`の該当パスが仮のまま
  (`__REALTIME_SERVER_NOT_YET_CREATED__`)

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
| `move_group_master` | 108 | 技グループ所属技の対応表(旧`move_group_moves`)。代理キー`unique_id`を追加 |

### 残タスク

- [x] `rarity`がS以外(A/B/C)のパチモンが無い問題 → 使用率ランキング19-36位を元にA/B/C各6体を追加し解消(`scout_banners.rate_table`が機能する状態になった)
- [ ] `type_chart`(タイプ相性表)未実装。`multiplier`がDECIMAL前提だが、パイプラインの型システムが
      int/string/bool/enumのみで小数非対応のため設計要検討(int化 or 倍率enum化)
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

- 認証は`argon2`でdevice_secretをハッシュ化、IDは`ulid`
- テスト: `tests/{auth,chat,device,player,master_data}_api_test.rs`
- マイグレーション6本(devices/access_tokens/messages/players再構成/pachimonテーブル/型サイズ最適化)
- `pachimon`マスタはDBに保存し、起動時にメモリキャッシュへ読み込む設計(`src/master/cache.rs`)。
  更新時は`cargo run --bin seed_master_data`でJSON→DBへUPSERTしてから再起動する運用

### 未実装

| セクション | 内容 |
|---|---|
| パチモン・スカウト | `/scout/*`, `/players/me/pachimon`, `/players/me/party`, 技の付け替え等 |
| マッチング | `/battle/queue*` |
| 内部API | `/internal/battle/result` |
| `player_pachimon` | プレイヤー所持データのモデル/テーブル自体が未着手 |

### 今回発見したギャップ

`moves` / `move_groups` / `move_group_master` は`master_data/*.json`とRust struct
(`src/master/generated/`)までは生成・配置済みだが、**`cache.rs`と`seed_master_data.rs`は
まだ`pachimon`しか扱っていない**。DBテーブル自体も未作成で、マイグレーションのコメントにも
「move_groupsテーブルが未実装のため外部キー制約を付けない」という古い記述が残ったまま。
技データはファイルとしては存在するが、DBに投入されておらずAPIサーバーからは参照できない。

## 4. クライアント / リアルタイムサーバー / API連携

design/architecture.mdの全体構成(`Unity Client ←REST→ Rust/Axum`, `Unity Client ←gRPC/StreamingHub→ C#/MagicOnion`)
に対して、現状は以下の状態。

### Unity Client

`Client/`配下には、master-data-pipelineが生成した`Domain.MasterData`(マスターデータの
Models/Enums/`masterdata.bytes`)以外に実体が無い。Unityプロジェクトとしての体裁
(`ProjectSettings/`, `Packages/`等)自体が未作成で、通信・UI・ゲームロジックは何も無い状態。

### リアルタイムサーバー(C#/MagicOnion)

design/battle.mdで「対戦中の判定をメモリ上で行う」役割として設計されているが、プロジェクト自体が
まだ存在しない(`realtime_server`に相当するディレクトリが無い)。`IBattleHub`等のHub定義、
選出フェーズ、ダメージ計算ロジックはすべて未着手。

### APIサーバー ⇔ Unity Client 間のコード生成(API codegen)

`Shared/docs/feature-api-codegen.md`に構想メモがあるが未実装。Rustのhandlerから
`utoipa`で`api.yaml`(OpenAPI仕様書)を生成し、そこからUnity側のRequest/Response型を
自動生成する仕組み(候補: `openapi-generator` / `NSwag` / 自作スクリプト)。現状はREST APIの
型がRust側にしか存在せず、Unity側で使う型は手書きするか未定義の状態。

## 5. 残タスク一覧(統合)

| # | 内容 | 領域 |
|---|---|---|
| 1 | realtime_server(MagicOnion)プロジェクトの新規作成・`IBattleHub`等の実装一式 | realtime_server |
| 2 | `moves`/`move_groups`/`move_group_master`のDBテーブル作成・`cache.rs`/`seed_master_data.rs`対応 | server/master-data |
| 3 | `player_pachimon`(所持データ)のモデル・テーブル・API実装 | server |
| 4 | スカウトAPI(`/scout/*`)実装 | server |
| 5 | マッチングAPI(`/battle/queue*`)実装 | server |
| 6 | 内部API(`/internal/battle/result`)実装 | server |
| 7 | `type_chart`(タイプ相性)の設計・実装 | master-data/pipeline |
| 8 | 技の拡充(状態技、候補技の追加) | master-data |
| 9 | Unityクライアント側の実装一式(プロジェクト構築、通信、UI、ゲームロジック) | client |
| 10 | API codegen(Rust handler→OpenAPI→Unity C#型)の導入 | server/client連携 |
| 11 | `scout_banners`用seedスクリプト(`seed_scout_banners`)の実装・常設バナー1件の投入 | server |
