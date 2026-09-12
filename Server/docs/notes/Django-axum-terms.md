# メモ: Django と axum(Rust)の用語対応表

Djangoの実務経験をもとに命名すると、Rust/axumのエコシステムでは意味が通じない・誤解されるケースがあるため、対応表としてまとめておく。

| Djangoでの呼び方 | 意味 | axum(Rust)での呼び方 | 備考 |
|---|---|---|---|
| `views.py` / View | リクエストを受けてレスポンスを返す関数 | `handler`(例: `signup_handler`) | axumでは一貫して`handler`という接尾辞を使うのが慣習 |
| `urls.py` | ルーティング定義(URLとViewの対応表) | `routes.rs` / `router.rs` | axumのサンプルやドキュメントで頻出。`urls.rs`にすると`url`クレート(URLパース用)と紛らわしくなるため非推奨 |
| Serializer | リクエスト/レスポンスの型・バリデーション | 明確な標準語はないが、DTOと呼ばれることが多い | `serde`の`Deserialize`/`Serialize`を実装した構造体として表現する |
| Model(DjangoのORM) | DBテーブルに対応するデータ構造 | `models`(意味は同じ) | ただしDjangoのModelはDB操作メソッドを持つが、Rustでは`models`は純粋なデータ構造のみで、DB操作は`service`層が担う |
| Django自体が提供するORM操作 | `User.objects.filter(...)`など | `sqlx`のクエリ(生SQLに近い) | axum自体はORMを持たず、`sqlx`/`sea-orm`/`diesel`などを別途選定する |

## 補足

- Djangoは「フルスタックフレームワーク」であり、ORM・ルーティング・シリアライズなどが標準搭載されている
- axumは「Webフレームワークの薄い層」のみを提供し、DBアクセス(`sqlx`など)は別クレートとして組み合わせる設計思想
- そのため、Djangoの1つの概念がRust側では複数のクレート/層に分かれることがある点に注意する