# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Rust製のAPIサーバー。クライアント向けのAPIも、BattleServer向けの内部APIも gRPC(tonic + gRPC基盤VolTech gRPCのクレート
`voltech-grpc-*`)で、同じプロセスの別のポートで出す。プロジェクト全体のルールは[ルートのCLAUDE.md](../CLAUDE.md)。

| 知りたいこと | 読む場所 |
|---|---|
| ポート・Layerの順番・ディレクトリの役割・API一覧 | [APIサーバーの構成](../docs/wiki/server/structure.md) |
| 各APIの振る舞い・内部API・マッチングの待機列 | [APIサーバー](../docs/wiki/server/index.md) |
| テーブル | [DB](../docs/wiki/db/index.md) |
| 環境構築・コマンド | [開発環境ガイド](../docs/wiki/development.md)と`server-dev-env`スキル |
| 基盤の使い方 | 基盤のwiki(`docs/wiki/guide/`、特に`add-api.md`・`server-reference.md`) |

## コーディングルール

### サーバーの組み立て

- `grpc.rs`のLayerの順番には意味がある(`RpcLogLayer` → `ServerStatusLayer` → `RpcMethodLayer` → サービスごとに`AuthLayer` → `DateChangeLayer`)。
  **クライアント向けのアプリのサービスには必ず`AuthLayer`と`DateChangeLayer`を付ける**
- 内部API(`grpc/internal_battle.rs`)には基盤のLayerのうち`RpcLogLayer`だけを付ける(メンテナンス中でも結果の報告を受けるため)。認証は
  `x-internal-secret`のinterceptorで行う

### API(gRPC)

- APIの定義は`Shared/proto/atlas/v1/*.proto`(内部APIは`Shared/proto/atlas/internal/v1/`)が唯一の正本。RPC・メッセージ・
  フィールドの直前のコメントがAPIの仕様になる(値の形・どのエラーをいつ返すかまで書く)。サービスを持つ`.proto`を足したら
  `build.rs`の`PROTO_FILES`にも足す
- 冪等性: 書き込むRPCは`idempotency_level`を書かない(冪等性の対象)。読むだけなら`NO_SIDE_EFFECTS`、
  何回実行しても同じ結果になるなら`IDEMPOTENT`
- クライアント向けのハンドラは業務処理を`Transactions::run`の中で動かす。冪等性の対象のRPCは`player_id`(`Ulid`)を渡す。
  サービス層の関数は、渡されたトランザクション(`&mut Transaction<'_, MySql>`)か`impl MySqlExecutor`で読み書きし、
  自分でトランザクションを始めない(内部APIの`ReportResult`は基盤を通らないので、`battle_service::record_result`が自分で張る)
- ハンドラはプレイヤーIDを`grpc::context::player_id`で取り出す(リクエストから受け取らない)
- 所持データを変えるRPCは、`PlayerDiffCollector`に記録し、レスポンスに`PlayerDiff player_diff`を持たせる(変わらない種別は`None`)
- 所持アイテムを増減する関数は、`GameLogCollector`に増減と理由(`ItemReason`)を記録する。ハンドラーは処理の最後に`logs.write_to(ctx)?`で
  基盤のゲームログに書く(コミットしたときだけ残る)。アイテムの増減は`player_item_service`の関数を通し、直接`player_items`を書き換えない
- 業務エラーは`Shared/errors.toml`に足し、`voltech_grpc_error::failed_precondition(proto::errors::XXX, "...")`で返す。
  内部エラーは`voltech_grpc_error::internal(err)`か`.or_internal()`で、原因ごと渡す(原因はログにだけ出る)。消したreasonは使い回さない
- 内部APIは業務エラーを使わず、`AppError`をgRPCの標準のステータスに変える(`BadRequest`→`INVALID_ARGUMENT`、
  `NotFound`→`NOT_FOUND`、`Conflict`→`ALREADY_EXISTS`)
- `.proto`・`errors.toml`を変えたら、ルートで`make proto-gen`・`make errors-gen`を実行してUnity向けのC#も生成し直す
- フィールド番号・名前は一度使ったら使い回さない(消したら`reserved`)
- DBなどのエラーは`?`で`AppError::InternalError`(機能ごとのエラーなら`PartyError::Internal`など)にする。どれも基盤の`Internal`を持ち、
  原因と`?`を書いた場所をログまで運ぶ。`.map_err(|_| ...)`で原因を捨てない。元のエラーが無いときは`AppError::internal("...")`
- ログは`tracing`のマクロで出す(出力の準備とRPCごとのアクセスログは基盤が行う)。業務の出来事は`info`で、名前は書かずIDだけを書く

### 状態とマスターデータ

- マスターデータは起動時にDBから読み込んだ`Arc<MasterData>`を使い、リクエストのたびにマスタのテーブルへ問い合わせない
- マスターデータの生成物(`src/master/generated/`、`master_data/*.json`)は手で編集しない。`seed_master_data`が投入するテーブルを
  増やすときは`master-data-schema-add`スキルに従い、`seed_master_data.rs`(必要なら`cache.rs`も)を拡張する
- スカウトのバナー(`scout_banners`・`scout_banner_rates`)もマスターデータで、`seed_master_data`が投入する。パイプラインの`validate`で
  確かめられない決まり(日時の形・合計値など)は`seed_master_data`が投入の前に確かめる
- マッチングの待機列(`AppState.matchmaking`)は`std::sync::Mutex`。ロックを持ったまま`await`しない
- 基盤の`player_id`は`ulid::Ulid`(DBは`BINARY(16)`)、Atlasのテーブルは`CHAR(26)`の文字列なので、つなぎ目で
  `to_string()`/`from_string()`で変える

### トランザクション

- 複数のテーブルを書き換える処理は1つのトランザクションにまとめる(プレイヤー作成と初期付与、スカウトの消費と
  候補の保存、結果報告と報酬付与など)
- 同時に届くと二重に処理されるもの(結果報告、サインアップ、スカウトの選択)は、対象の行を`SELECT ... FOR UPDATE`でロックしてから確かめる
- SQLは実行時に組み立てる`sqlx::query`/`query_as`を使う(コンパイル時に検査するマクロ`query!`は使っていない)

### ドキュメントコメント

- 公開する関数・型には`///`で概要を書き、失敗する関数には`# Errors`節でどのエラーをいつ返すかを書く

### Lint・フォーマット

- `Cargo.toml`の`[lints.clippy]`: `unwrap_used`・`expect_used`は警告(起動処理・seedでは意図して使う)、
  `dbg_macro`・`todo`・`unimplemented`は禁止
- フォーマットは`make fmt`(`make fmt-check`で確認)。**生の`cargo fmt`は使わない**。生成物
  (`src/master/generated/`)の並びが変わり、次に生成したときとの差分が出続けるため(`rustfmt`の除外指定は
  stableでは効かないので、`make fmt`が生成物を除いたファイルに対して実行する)

### マイグレーション

- `migrations/`に追加する。サーバーの起動時に、基盤のマイグレーションの後に流れる(`db::migrate`)。`make migrate`
  (sqlx-cli)でもAtlasの分だけを流せるが、基盤のテーブルはサーバーを起動するまで作られない
- 適用済みのマイグレーションの中身を変えると(コメントも含む)、DBに記録されたチェックサムと合わなくなり
  起動時のマイグレーションが失敗する。スキーマの変更は新しいマイグレーションで行い、コメントだけを直した場合は
  ローカルDBの`_sqlx_migrations.checksum`をファイルのSHA-384で更新する(またはDBを作り直す)
- 基盤のテーブル名(`devices`・`access_tokens`・`last_responses`)はAtlasのマイグレーションで使わない

## テスト

- `tests/*_grpc_test.rs`: 空いているポートで起動したgRPCサーバーに、tonicの生成クライアントで呼ぶ結合テスト。
  起動・デバイス登録・トークン・ヘッダー(`authorization`・`x-request-id`)の部品は`tests/common/mod.rs`
- `tests/internal_battle_grpc_test.rs`: 内部APIのテスト。内部APIのサーバーに`x-internal-secret`を付けて呼ぶ。対戦はサービスの関数で
  待機列に入れて成立させる。結果報告が届かない対戦の後始末もここで確かめる
- 冪等性の対象のRPCは、同じRequestIDで2回送っても業務データが1回分しか変わらないことを確かめる。`IDEMPOTENT`のRPCは、
  2回呼んでも状態が同じになることを確かめる
- `#[sqlx::test(migrations = false)]`でテストごとに独立したDBを作り、`common::prepare_db`で基盤とAtlasのマイグレーションと
  最小限のマスタを入れる(`atlas_dev`を汚さない)。MySQLコンテナが起動している必要がある
- DB不要な単体テストは`src/`内の`#[cfg(test)] mod tests`に置く
