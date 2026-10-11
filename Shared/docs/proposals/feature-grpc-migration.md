# gRPC 基盤への乗せ換えの指示

Atlas(C:\Work\Atlas)の API サーバーとクライアントの通信を、REST から gRPC 基盤 VolTech gRPC に乗せ換えたい。
まず調べて計画を立て、私に確認してから実装を始めること。

## 基盤
- リポジトリ: https://github.com/chinpanGX/VolTech-gRPC(手元には C:\Work\VolTech-gRPC がある。読むのに使ってよい)
- タグ v0.1.0 で参照する。Cargo は git + tag、Unity は manifest.json の git URL(?path=/Unity/com.chinpangx.voltech.grpc#v0.1.0)。
  path・file: での参照、submodule、コードのコピーはしない
- 使い方は基盤の wiki の docs/wiki/guide/ を読む(index.md から。始め方 getting-started・つなぐ connect・API を足す add-api・役割分担 roles・
  クライアント/サーバーのリファレンス・通信の調べ方 debugging・基盤の版を上げる upgrade)。設計の理由は docs/wiki/api・client・server
- 組み込みの実例は基盤の sandbox/(サーバー sandbox/server、Unity sandbox/UnityProject、生成の設定 buf.gen.sandbox.yaml)
- Atlas の中で基盤のコードを直さない。基盤に足りないもの・合わないものが見つかったら、回避策を作らずに一覧にして私に報告する
  (基盤のほうを直して、新しいタグを打つ)

## 範囲
- 移す: APIサーバー(Rust/axum)の REST の API と、Unity クライアントの API 通信層(Infrastructure/Api)
- 移さない:
  - BattleServer(C#/MagicOnion)との対戦の通信。MagicOnion のまま
  - BattleServer からの内部API(/internal/battle/*、X-Internal-Secret)。REST のまま残す
  - master-data-pipeline。変えない
  - マッチ成立時に発行する battle_token(JWT)の仕組み。そのまま
- /debug/* は gRPC に移すか、REST のまま残すか、計画で提案する
- 全部移し終えたら、api-codegen(submodule)と openapi.yaml の生成(utoipa・Swagger)を Atlas から外す。外すのは最後に、私に確認してから

## 始める前に
- Atlas の develop には未コミットの変更がある(ProjectSettings.asset の変更、Shared/docs/proposals/feature-grpc-foundation.md の削除のステージ)。
  これには触らない。作業を始めるブランチは私に確認する
- 乗せ換えを始める前の状態を、REST 版として残すブランチを作る(名前は私に確認する)
- Shared/docs/proposals/feature-grpc-foundation.md は古い案(テンプレートを clone する形)なので、読まない・従わない

## 先に調べて、計画に書くこと
1. 依存の版: Atlas のサーバーは axum 0.7・sqlx 0.8、基盤は tonic 0.14・sqlx 0.9。sqlx を上げる範囲と、
   内部API(axum の REST)と gRPC を同じプロセス・同じポートで出すか、分けるか
2. テーブル: Atlas にも devices・access_tokens があり、基盤のテーブルと名前がぶつかる。スキーマの違いと、
   既存のデバイス・プレイヤーを引き継ぐデータ移行の方法(基盤は players を持たず、PlayerResolver でつなぐ)
3. 認証: Atlas の今の流れ(/devices・/devices/authenticate・/auth/verify・/signup・/sign-in、AccessTokenRefresher)と、
   基盤のデバイス登録・トークン・再認証(ApiCore)の対応。「プレイヤー未作成(今は 404 でサインアップへ)」をどう表すか
4. エラー: AppError(HTTP ステータス)を、Atlas の domain の errors.toml の業務エラーに置き換える一覧。予約エラーは書かない
5. 差分: PlayerDiffDto・build_full_player_diff などの差分を、基盤の共通レスポンス項目・コレクター・クライアントの反映処理に置き換える方法
6. API ごとの .proto の案(サービスの分け方、冪等性の対象か NO_SIDE_EFFECTS か IDEMPOTENT か)。対象は今の routes.rs の REST の API すべて
7. クライアント: Presenter → Service → Connection → ApiClient の層を、生成したラッパーと ApiCore に置き換えたときの形。
   VContainer への登録、Fake(FakeCallInvoker)への差し替え、エラーダイアログの見た目(prefab)をどうするか
8. Unity: MagicOnion クライアントと基盤のパッケージが同じ gRPC の DLL(Grpc.Net.Client 2.84.0 など)を使えるか。
   YetAnotherHttpHandler の版の違い。Atlas は Unity 6000.6.2f1、基盤は 6000.6.5f1 で確かめている
9. 移す順番。サーバーとクライアントを、動く状態を保ったまま API のまとまりごとに移す案(認証とサインイン → 編成 → スカウト → マッチング → チャット など)

計画は Atlas の Shared/docs/ に文書として書き、私に確認してから実装する。

## 進め方の決まり
- Atlas の CLAUDE.md(全体と Server/・Client/ のもの)の決まりに従う
- まとまりごとに、cargo test と Unity の EditMode テストを通してからコミットする(コミットとプッシュは私に確認する)
- 生成したコードは手で直さない。C# は基盤の mk/codegen.mk を読み込む Makefile で生成する(getting-started の「C# を生成する」)
- 移し終えたまとまりの REST の API とクライアントのコードは、その時点で消す(両方を残さない)
- Atlas のドキュメント(CLAUDE.md・DEVELOPMENT.md・Shared/docs/design.md・progress.md)の、通信の説明・構成図・生成物の表を、移したところから直す
- 基盤に足りないものは、上に書いたとおり報告する。基盤を一緒に直す手順は基盤の wiki の upgrade.md にある
