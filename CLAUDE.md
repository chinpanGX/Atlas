# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Atlasは、デバイス認証(ゲスト型)で始めるリアルタイム対戦モンスター収集ゲーム。Unityクライアント・Rustの
APIサーバー・C#/MagicOnionのバトルサーバーで構成する。このファイルにはプロジェクト全体に共通するルールを書き、
各プロジェクト固有のルールはそれぞれのCLAUDE.mdに書く。構成と設計(なぜそうしたか)はwikiに書く。

- [Server/CLAUDE.md](Server/CLAUDE.md) — APIサーバー(Rust/tonic)
- [BattleServer/CLAUDE.md](BattleServer/CLAUDE.md) — バトルサーバー(C#/MagicOnion)、`Shared/BattleCore`・`Shared/BattleContracts`
- [Client/AtlasUnityProject/CLAUDE.md](Client/AtlasUnityProject/CLAUDE.md) — Unityクライアント

| 知りたいこと | 読む場所 |
|---|---|
| 全体の構成・通信の経路・リポジトリ構成 | [docs/wiki/overview.md](docs/wiki/overview.md) |
| 仕様と設計(実装に合わせたもの) | [docs/wiki/index.md](docs/wiki/index.md)(更新のルールは[docs/wiki/CLAUDE.md](docs/wiki/CLAUDE.md)) |
| 環境構築・起動手順・コマンド | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| 実装状況と残タスク | [docs/progress.md](docs/progress.md) |

## 構成の上で守ること

構成の説明と理由は[概要](docs/wiki/overview.md)。ここには、作業するときに外してはいけない決まりだけを書く。

- **ClientとAPIサーバーの通信は自作のgRPC基盤[VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)に乗せる**
  (タグ`v0.1.0`で参照。Cargoはgit+tag、Unityは`manifest.json`のgit URL、C#の生成はルートの`Makefile`)。
  **Atlasの中で基盤のコードを直さない**。足りないものは基盤のほうを直してタグを打つ
- **DBへの書き込みはAPIサーバーに一本化する**。バトルサーバーはDBを持たず、内部API(gRPCの`atlas.internal.v1.BattleInternalService`、
  別ポート、`x-internal-secret`で認証)で取得・報告する
- **対戦の判定はバトルサーバーが権威を持つ**。判定に使う生の値(HPの実数など)はクライアントに渡さない
- サーバーは1台構成を前提にする。マッチングの待機列や対戦の状態はプロセスのメモリに持つ
- `Atlas.BattleCore`・`Atlas.BattleContracts`は`Shared/`の同じソースをClientとBattleServerの両方がコンパイルする。
  どちらか一方の都合だけで変えない

### 生成物とその正本

生成物は手で編集しない(再生成で上書きされる)。正本を変えたら生成し直す。

| 正本 | 生成物 | 生成方法 |
|---|---|---|
| `Shared/master-data/`(スキーマ・CSV) | Rustの型・JSON、C#の型・`masterdata.bytes`(Client・BattleServer) | `master-data-pipeline`スキル。Rust側はさらに`seed_master_data`でDBへ投入 |
| `Shared/proto/`(`.proto`)・`Shared/errors.toml`(業務エラー) | Rustのサーバーの型・エラーコード(`build.rs`が生成。コミットしない)、Clientの`Infrastructure/Grpc/Generated/`、BattleServer・BattleBotのクライアント(ビルド時) | Rust・.NETはビルドで自動。Unityはルートで`make proto-gen`・`make errors-gen` |
| Addressablesの登録 | Clientの`AddressDefinition.cs` | Unityのジェネレーター |

## 共通のコーディングルール

### コメント

- **コメントにドキュメントへの参照を書かない**(「design.md「〇〇」参照」「wikiの〇〇を実装した」など)。
  ドキュメントの構成を変えるたびにコードまで直すことになるため。コメントには、実装内容の概要と、
  コードから読み取れない注意点(なぜそうしているか)を、ドキュメントに依存せず実装に即して書く
- 仕様の根拠を示したいときも、ドキュメント名ではなく内容を書く(例: 「交代は技より先に処理されるため」)
- C#・Rust・SQL(マイグレーション)・YAML(マスターデータのスキーマ)のどれでも同じ
- クラス名・メソッド名を読めば分かることを繰り返すだけのコメントは書かない

### 命名

詳しくは[命名とID](docs/wiki/conventions/naming-and-ids.md)。

- DB・Rust: `snake_case`。マスタは修飾語なし、プレイヤーの所持データは`player_`を付ける(`pachimon` / `player_pachimon`)
- 「ポケモン」にあたる語は`pachimon`で統一する(商標と混同しないための独自名)
- `.proto`は`buf lint`(STANDARD)の規約どおり
- C#は型・メソッド・プロパティが`PascalCase`。private フィールドは`camelCase`(アンダースコアを付けない)、
  private の`const`・`static readonly`は`PascalCase`。Client・BattleServer・`Shared/`で共通
- C#の命名ルールはリポジトリ直下の`.editorconfig`に書いている(Supplementの`.editorconfig`と同じルール)。
  Riderで警告になり、BattleServerは`Directory.Build.props`の`EnforceCodeStyleInBuild`で`dotnet build`でも警告(IDE1006)になる

### データの表し方

理由は[値なしの表し方](docs/wiki/conventions/nullable.md)・[所持データの差分](docs/wiki/conventions/player-diff.md)。

- gRPCのAPI(`.proto`。クライアント向け・内部API)は基盤の規約に従う。スカラーは`optional`を使わず、「値なし」は番兵値(空文字など)で表す。
  メッセージ型のフィールドは`null`を「変更なし / 該当なし」として使ってよい。MagicOnion(MessagePack)の通信契約は`nullable`を使ってよい
- DBの「無い」は行が無いことで表す(パーティの未編成の枠など)
- プレイヤーの操作で生成するデータのIDはULID(DBは`CHAR(26)`)。パーティの枠や覚えている技のような「割当」も、
  行ごとにULIDを持つ独立したエンティティにする
- 所持データを変えるAPIは、レスポンスに共通の形の`player_diff`(リソース種別ごとの`upserted`/`removed`)を含める。
  サインインの全件は`player_diff`と名前を分けて(`snapshot`)返し、クライアントは置き換える
- 業務エラーは`Shared/errors.toml`(domain `atlas`)に書き、サーバーは`FAILED_PRECONDITION` + `ErrorInfo`で返す
- ジェムのような数量は`players`の列にせず、`items`マスタと`player_items`で持つ

## 作業時の注意

- マスターデータのスキーマ/CSVを変更したら `master-data-pipeline` スキルで生成物を再配置する
  (テーブル・Enumの追加は `master-data-schema-add` スキル)
- pachimonマスタにパチモンを追加したら `pachimon-thumbnail` スキルで仮のサムネイルを生成し、Addressablesに登録する
- `.proto`・`Shared/errors.toml`を変えたら、ルートで`make proto-gen`・`make errors-gen`を実行してUnity向けのC#を生成し直す
  (サーバーの`build.rs`の`PROTO_FILES`にも、サービスを持つ`.proto`を足す)
- MySQLコンテナ・マイグレーション・DBのリセットは `server-dev-env` スキル
- 適用済みのマイグレーションのSQLを書き換えると、ローカルDBの`_sqlx_migrations`のチェックサムと合わなくなる。
  書き換えた場合は、ファイルのSHA-384でチェックサムを更新するか、DBを作り直す
- 機能実装・API変更・マスターデータ追加を行った後は `atlas-design-docs-sync` スキルでどのドキュメントを更新すべきか確認する
