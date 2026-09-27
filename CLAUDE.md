# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Atlasは、デバイス認証(ゲスト型)で始めるリアルタイム対戦モンスター収集ゲーム。Unityクライアント・Rustの
APIサーバー・C#/MagicOnionのバトルサーバーで構成する。このファイルにはプロジェクト全体に共通する
アーキテクチャとコーディングルールを書き、各プロジェクト固有のものはそれぞれのCLAUDE.mdに書く。

- [Server/CLAUDE.md](Server/CLAUDE.md) — APIサーバー(Rust/axum)
- [BattleServer/CLAUDE.md](BattleServer/CLAUDE.md) — バトルサーバー(C#/MagicOnion)、`Shared/BattleCore`・`Shared/BattleContracts`
- [Client/AtlasUnityProject/CLAUDE.md](Client/AtlasUnityProject/CLAUDE.md) — Unityクライアント

環境構築・起動手順・コマンドは[DEVELOPMENT.md](DEVELOPMENT.md)、ゲームの仕様は[Shared/docs/game-spec.md](Shared/docs/game-spec.md)、
設計の詳細は[Shared/docs/design.md](Shared/docs/design.md)、実装状況は[Shared/docs/progress.md](Shared/docs/progress.md)。

## アーキテクチャ

```
Unity Client
   │ REST (HTTP/JSON)              │ MagicOnion (gRPC / StreamingHub)
   ▼                               ▼
APIサーバー(Rust/axum)        バトルサーバー(C#/MagicOnion)
 認証・プレイヤー・スカウト・      対戦の進行のみ(状態はメモリ上)
 チャット・マッチング・DB書き込み        │
   ▲─────────── 内部API(REST) ──────────┘  パーティ・選出個体の取得、結果報告
   │
 MySQL(唯一のデータストア。書き込みはAPIサーバーだけが行う)
```

- **DBへの書き込みはAPIサーバーに一本化する**。バトルサーバーはDBを持たず、必要なデータは内部API
  (`/internal/battle/*`、`X-Internal-Secret`で認証)で取得し、結果も内部APIで報告する
- **対戦の判定はバトルサーバーが権威を持つ**。クライアントは結果を受け取って表示するだけ。HPは%でしか送らない等、
  判定に使う生の値はクライアントに渡さない
- バトルサーバーの参加資格は、APIサーバーがマッチ成立時に発行する短命JWT(`battle_token`)で確かめる
- サーバーは1台構成(ローカルでの動作)を前提にする。マッチングの待機列や対戦状態はプロセスのメモリに持つ
- ダメージ計算・ターン処理(`Atlas.BattleCore`)と通信契約(`Atlas.BattleContracts`)は、`Shared/`に置いた同じ
  ソースをClient(Unityのローカルパッケージ)とBattleServer(csproj)の両方がコンパイルする

### リポジトリ構成

```
Atlas/
├── Client/AtlasUnityProject/  Unityクライアント
├── Server/                    APIサーバー(Rust/axum)
├── BattleServer/              バトルサーバー(C#/MagicOnion)、対戦相手ボット(BattleBot/)、テスト
├── Shared/
│   ├── BattleCore/            ダメージ計算・ターン処理(Unityパッケージ + BattleServerのcsprojから参照)
│   ├── BattleContracts/       Client⇔BattleServerの通信契約(同上)
│   ├── master-data/           マスターデータのスキーマとCSV(正本)
│   ├── api/openapi.yaml       APIサーバーのOpenAPI仕様書(生成物)
│   └── docs/                  仕様概要書・設計書・進捗・構想・メモ
├── master-data-pipeline/      マスターデータの生成ツール(submodule、自作)
├── api-codegen/               OpenAPI → Unity向けDTO・通信クライアントの生成ツール(submodule、自作)
├── Supplement/                Unity共通ユーティリティ(submodule、自作)
└── UnityScreenNavigator/      画面遷移ライブラリ(USN)のフォーク(submodule、developブランチ)
```

### 生成物とその正本

生成物は手で編集しない(再生成で上書きされる)。正本を変えたら生成し直す。

| 正本 | 生成物 | 生成方法 |
|---|---|---|
| `Shared/master-data/`(スキーマ・CSV) | Rustの型・JSON、C#の型・`masterdata.bytes`(Client・BattleServer) | `master-data-pipeline`スキル。Rust側はさらに`seed_master_data`でDBへ投入 |
| APIサーバーのハンドラ・DTO(`utoipa`の注釈) | `Shared/api/openapi.yaml` → Clientの`Infrastructure/Api/Generated/` | `api-codegen`スキル |
| Addressablesの登録 | Clientの`AddressDefinition.cs` | Unityのジェネレーター |

## 共通のコーディングルール

### コメント

- **コメントにドキュメントへの参照を書かない**(「design.md「〇〇」参照」「設計書の〇〇を実装した」など)。
  ドキュメントの構成を変えるたびにコードまで直すことになるため。コメントには、実装内容の概要と、
  コードから読み取れない注意点(なぜそうしているか)を、ドキュメントに依存せず実装に即して書く
- 仕様の根拠を示したいときも、ドキュメント名ではなく内容を書く(例: 「交代は技より先に処理されるため」)
- C#・Rust・SQL(マイグレーション)・YAML(マスターデータのスキーマ)のどれでも同じ
- クラス名・メソッド名を読めば分かることを繰り返すだけのコメントは書かない

### 命名

- DB・Rust: `snake_case`。マスタは修飾語なし、プレイヤーの所持データは`player_`を付ける(`pachimon` / `player_pachimon`)
- 「ポケモン」にあたる語は`pachimon`で統一する(商標と混同しないための独自名)
- REST APIのJSONは`camelCase`(Rustは`serde(rename_all = "camelCase")`で変換し、内部は`snake_case`)
- C#は型・メソッド・プロパティが`PascalCase`。private フィールドは`camelCase`(アンダースコアを付けない)、
  private の`const`・`static readonly`は`PascalCase`。Client・BattleServer・`Shared/`で共通
- C#の命名ルールはリポジトリ直下の`.editorconfig`に書いている(Supplementの`.editorconfig`と同じルール)。
  Riderで警告になり、BattleServerは`Directory.Build.props`の`EnforceCodeStyleInBuild`で`dotnet build`でも警告(IDE1006)になる

### データの表し方

- **APIのレスポンスに`nullable`を使わない**(`api-codegen`が対応していないため。ツール側に対応を足すのではなく、
  APIとスキーマの側で表現を工夫する)。「無い」ことは行が無いことで、値の無い文字列は空文字で表す。
  MagicOnion(MessagePack)の通信契約は`nullable`を使ってよい
- プレイヤーの操作で生成するデータのIDはULID(DBは`CHAR(26)`)。パーティの枠や覚えている技のような「割当」も、
  行ごとにULIDを持つ独立したエンティティにする
- 所持データを変えるAPIは、レスポンスに共通の形の`playerDiff`(リソース種別ごとの`upserted`/`removed`)を含める。
  クライアントは受け取った差分を手元のデータに反映する
- ジェムのような数量は`players`の列にせず、`items`マスタと`player_items`で持つ

## 作業時の注意

- マスターデータのスキーマ/CSVを変更したら `master-data-pipeline` スキルで生成物を再配置する
  (テーブル・Enumの追加は `master-data-schema-add` スキル)
- APIサーバーのAPI(handler/DTO)を追加・変更したら `api-codegen` スキルでUnity向け型を再生成する
- MySQLコンテナ・マイグレーション・DBのリセットは `server-dev-env` スキル
- 適用済みのマイグレーションのSQLを書き換えると、ローカルDBの`_sqlx_migrations`のチェックサムと合わなくなる。
  書き換えた場合は、ファイルのSHA-384でチェックサムを更新するか、DBを作り直す
- 機能実装・API変更・マスターデータ追加を行った後は `atlas-design-docs-sync` スキルでどのドキュメントを更新すべきか確認する
