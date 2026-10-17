---
title: "業務エラー"
status: current
area: conventions
related: [server/structure, client/error-dialog]
code: [Shared/errors.toml, Server/src/grpc/context.rs, Server/src/error.rs, Client/AtlasUnityProject/Assets/Scripts/Presentation/Common/ErrorMessages.cs]
updated: 2026-10-17
---

# 業務エラー

## 正本と生成

業務エラー(ジェム不足など、利用者の操作で起こり得るエラー)は `Shared/errors.toml`(domain `atlas`)に書く。
ここから次を生成する。

- APIサーバー: Rust の定数(`build.rs` が生成。`crate::proto::errors::XXX`)
- Unity クライアント: `Atlas.Api.V1.AtlasErrors`(ルートで `make errors-gen`)。各エラーの既定の action も含む

基盤の予約エラー(domain `voltech`。認証・メンテナンス・強制アップデート等)は基盤が持ち、Atlas には書かない。
消した reason は使い回さない。

## 一覧

| reason | 返す API | 意味 |
|---|---|---|
| `PLAYER_NOT_FOUND` | 認証が要る全 API(サインアップを除く) | プレイヤーがまだ作られていない。サインインで返ったらサインアップする |
| `INVALID_PARTY` | `SetParty` | 人数が1〜6体でない、枠が1〜6でない、枠か個体が重複している |
| `PACHIMON_NOT_OWNED` | `SetParty`・`SetPachimonMove`・`SetPachimonStatPoints` | 指定した個体が自分の所持個体でない |
| `INVALID_MOVE_SLOT` | `SetPachimonMove` | 技の枠が1〜4でない |
| `MOVE_NOT_LEARNABLE` | `SetPachimonMove` | 技がその個体の技グループの候補技でない |
| `MOVE_UNCHANGED` | `SetPachimonMove` | その枠に今と同じ技が入っている |
| `MOVE_ALREADY_LEARNED` | `SetPachimonMove` | 別の枠でその技を覚えている |
| `INVALID_STAT_POINTS` | `SetPachimonStatPoints` | 負の値がある、または合計が64を超えている |
| `INSUFFICIENT_TRAINING_TICKETS` | `SetPachimonMove`・`SetPachimonStatPoints` | 育成チケットが足りない |
| `SCOUT_BANNER_NOT_FOUND` | `Roll` | バナーが無い |
| `SCOUT_BANNER_CLOSED` | `Roll` | バナーが開催期間外 |
| `INSUFFICIENT_GEMS` | `Roll`・`Purchase` | ジェムが足りない |
| `SHOP_PRODUCT_NOT_FOUND` | `Purchase` | 商品が無い |
| `SCOUT_ROLL_NOT_FOUND` | `Select` | ロールが無い、または自分のものでない |
| `SCOUT_CANDIDATE_OUT_OF_RANGE` | `Select` | 候補の番号が範囲外 |
| `SCOUT_ROLL_ALREADY_SELECTED` | `Select` | 選択済み(1つのロールでは1回しか選べない) |
| `PARTY_EMPTY` | `JoinQueue` | パーティが1体も編成されていない |

action は `PLAYER_NOT_FOUND` が `title`、ほかはすべて `stay`(ダイアログの OK で画面に留まる)。

## 返し方

- APIサーバーは業務エラーを `FAILED_PRECONDITION` + `ErrorInfo`(domain `atlas`、reason)で返す。業務エラーを返すときは何も変えない
- 入力の形が不正なもの(ニックネームの文字数など)は `INVALID_ARGUMENT`。サービス層の内部エラーは `INTERNAL` にし、
  詳細はサーバーのログにだけ出す
- 内部 API(バトルサーバー向け)は業務エラーを使わず、gRPC の標準のステータスで返す([内部API](../server/internal-api.md))
- クライアントでの表示(ダイアログと文言)は [通信エラーのダイアログ](../client/error-dialog.md)
