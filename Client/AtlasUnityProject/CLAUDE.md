# Atlas Unity Client — コーディング規約

## C#命名規約

- **メンバー変数(フィールド)にアンダースコア接頭辞(`_camelCase`)を付けない**。Rider標準の
  コードスタイルに合わせ、フィールドも通常のパラメータ・ローカル変数と同じ`camelCase`で書く
  (例: `private readonly IPlayerConnection connection;`であって
  `private readonly IPlayerConnection _connection;`ではない)
- コンストラクタ引数とフィールド名が同じになる場合は`this.`で明示的に区別する

  ```csharp
  public sealed class HomePresenter
  {
      private readonly IPlayerConnection connection;

      public HomePresenter(IPlayerConnection connection)
      {
          this.connection = connection;
      }
  }
  ```

- ラムダの未使用引数の`_`(discard)はこの規約の対象外(フィールドではないため、そのままでよい)

これは[design/client-architecture.md](../../Shared/docs/design/client-architecture.md)の
コード例にも適用済み。
