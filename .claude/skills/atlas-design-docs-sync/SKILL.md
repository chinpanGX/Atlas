---
name: atlas-design-docs-sync
description: Atlasプロジェクトで機能実装・API変更・マスターデータ追加を行った後、どの設計書・ステータスドキュメントを更新すべきか判断するときに使う。実装とドキュメントの乖離を防ぐためのチェックリスト。
---

# 設計書・ドキュメントの更新先マップ

このプロジェクトは「実装したら対応するドキュメントも同じタイミングで更新する」運用
(直近のコミット `設計書更新とプレイヤー/認証/チャットAPIの実装` 等が示すパターン)。
何をやったかによって更新すべきファイルが変わる。

## ドキュメント一覧と更新トリガー

| ファイル | 内容 | 更新するタイミング |
|---|---|---|
| `README.md`(リポジトリ直下) | ポートフォリオ的な概要(何を作ったか・コアループ・機能一覧・システム構成・技術スタック)。実装済み/未実装エンドポイント一覧などの実装状況は書かない(`progress.md`が正) | コアループや機能構成が変わったとき、技術スタックが変わったとき(いずれも頻度低) |
| `Shared/docs/design/architecture.md` | 横断的な共通設計(全体構成・命名規則・マスターデータ設計・所持リソース設計(`items`/`player_items`)・APIレスポンス設計(`playerDiff`)) | 新しいマスタテーブル/Enumを追加したとき、命名規則やアーキテクチャ方針を変えたとき、複数機能にまたがるテーブルや共通レスポンス形式を変えたとき |
| `Shared/docs/design/{battle,scout,outgame}.md` | 機能別の設計。各ファイルの「API仕様」節(エンドポイント・リクエスト/レスポンス形状)と「DB設計」節(その機能のテーブル定義)が、REST API仕様・DB設計の正 | その機能領域のAPI・DBテーブル・仕様を実装・変更・詳細化したとき |
| `Shared/docs/design/client-architecture.md` | Unity Clientの骨格設計(シーン構成・画面遷移・DI・Connection/Repository/Serviceの層構成・`playerDiff`の適用・asmdef構成) | Client側の層構成・DI登録・asmdef・クラスの配置方針を変えたとき、設計書が例として挙げているクラスを削除/改名したとき |
| `Shared/docs/progress.md` | 開発進捗・実装状況・残タスク一覧(生きたステータス文書) | 何か1つの作業単位を終えるたびに(ほぼ毎回) |
| `Shared/docs/feature-api-codegen.md` | 未実装機能(api-codegen)の構想メモ | api-codegenの設計方針自体を変えたときのみ。実装が進んだら状況は`progress.md`側に書く |

`Server/docs/notes/api-design.md` / `Server/docs/notes/design.md` は存在しない(REST API仕様・
DB設計は上記の機能別設計書に統合されている)。`CLAUDE.md`等に名前が残っていても更新先にはしない。

## 実装タスクを終えたときのチェックリスト

1. **API(エンドポイント)を追加/変更した**
   → 該当機能の `Shared/docs/design/{battle,scout,outgame}.md` の「API仕様」節を更新
     (エンドポイント一覧表・リクエスト/レスポンス例。旧エンドポイントを廃止/改名した場合は
     「旧`XXX`は廃止」と明記する)
   → 共通レスポンス形式(`playerDiff`等)に関わる変更なら `architecture.md`「APIレスポンス設計」も更新
   → 実装状況(実装済み/未実装エンドポイント一覧)は`README.md`ではなく`Shared/docs/progress.md`を更新
2. **マスターデータのテーブル/Enumを追加した**
   → `master-data-schema-add` スキル完了後、`Shared/docs/design/architecture.md` の
     「マスターデータ設計」節にテーブル定義を追記
3. **DBスキーマ(マイグレーション)を変更した**
   → そのテーブルを持つ機能の `Shared/docs/design/{battle,scout,outgame}.md` の「DB設計」節を更新
     (複数機能から参照されるテーブル(`player_items`等)は`architecture.md`側に定義を集約し、
     機能別設計書からは参照リンクのみにする)
   → マイグレーション本数・内容の一覧は`Shared/docs/progress.md`「3. Server API実装状況」を更新
4. **Client側の層構成・クラス構成を変更した**
   → `Shared/docs/design/client-architecture.md` を更新。特に削除/改名したクラス名(例:
     `IPlayerRepository`→`IPlayerConnection`)が設計書の説明文・コード例・asmdef表に残っていないか
     `grep`で確認する
5. **どの作業でも共通**
   → `Shared/docs/progress.md` の該当セクション(実装済み表・残タスク一覧・進捗チェックボックス)
     を更新する。特に「実装したつもりが別の層(DB投入やキャッシュ)が追いついていない」ような
     ギャップを見つけたら、`progress.md`内の既存パターン(「今回発見したギャップ」節)に倣って
     明記しておく — これは実装漏れの再発見コストを下げるための、このプロジェクト固有の運用

## 書き方の流儀(既存ドキュメントから読み取れるスタイル)

- 実装状況は表(メソッド/パス、または `[x]`/`[ ]` チェックボックス)で管理する
- 「検討したが採用しなかった案」がある場合は理由と共に残す(`feature-api-codegen.md`の書き方を踏襲)
- 用語・命名規則は `architecture.md` が正なので、他ドキュメントで再定義せず参照リンクで済ませる
