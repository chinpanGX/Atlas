# メモ: 将来構想 — handlerからAPI仕様書・Unity側の型を自動生成する仕組み

## やりたいこと

Rust(axum)側のhandlerから`api.yaml`(OpenAPI仕様書)を自動生成し、そこからUnityクライアント側のリクエスト/レスポンス型を自動生成する仕組みを将来的に導入したい。API仕様書とクライアントコードの乖離を防ぐのが目的。

## 全体の流れ(構想)

```
Rustのhandler(api層)
    ↓ ①OpenAPIスキーマを自動生成
api.yaml (OpenAPI仕様書)
    ↓ ②コード生成ツールでC#型を生成
Unity側のRequest/Responseクラス
```

## ① Rust側:handlerからOpenAPIを生成する

`utoipa`クレートを使う。`ToSchema`をDTO(リクエスト/レスポンス型)に付与し、`#[utoipa::path(...)]`マクロをhandlerに付けることで、コンパイル時にOpenAPIスキーマを生成できる。既存の`api`/`service`/`models`の3層構造を変えずに導入できる見込み。`utoipa-swagger-ui`と組み合わせればブラウザ上でSwagger UIとしても確認できる。

## ② OpenAPIからUnity(C#)向けの型を生成する

候補は3つ。

- **`openapi-generator`**:汎用的で対応言語が多いが、UniTask/VContainerに合わせるにはテンプレートのカスタマイズが必要になりそう
- **`NSwag`**:.NET向けで型生成に強いが、Unity向けにそのまま使うにはひと手間かかりそう
- **自作のコード生成スクリプト**:`api.yaml`をパースして必要な型だけをC#として出力する簡易スクリプト。UniTask/VContainerとの統合を考えると、既存ツールより扱いやすい可能性がある

## 進め方の方針

`api`層の実装を進める段階で`utoipa`を組み込みながら実装していき、`api.yaml`が実際に生成できるようになった時点で、Unity側のコード生成ツールをどれにするか判断する。