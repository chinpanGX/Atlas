# Atlas Unity Client — コーディング規約

## C#命名規約

- **メンバー変数(フィールド)にアンダースコア接頭辞(`_camelCase`)を付けない**。Rider標準の
  コードスタイルに合わせ、フィールドも通常のパラメータ・ローカル変数と同じ`camelCase`で書く
  (例: `private readonly IPlayerService playerService;`であって
  `private readonly IPlayerService _playerService;`ではない)
- コンストラクタ引数とフィールド名が同じになる場合は`this.`で明示的に区別する

  ```csharp
  public sealed class HomePresenter
  {
      private readonly IPlayerService playerService;

      public HomePresenter(IPlayerService playerService)
      {
          this.playerService = playerService;
      }
  }
  ```

- ラムダの未使用引数の`_`(discard)はこの規約の対象外(フィールドではないため、そのままでよい)

## ファイル名

- **ファイル名は、そのファイルで定義する主要な型(class/struct/interface/record/enum)の名前と
  一致させる**(例: `PachimonEntity`は`PachimonEntity.cs`であって`Pachimon.cs`ではない)
- 1ファイルに複数の型を置く場合(実装クラスとそのインターフェース等)も、主となる型の名前をファイル名にする
  (例: `PlayerDiffApplier.cs`に`IPlayerDiffApplier`と`PlayerDiffApplier`)
- Unityで型名を変更したときは、`.cs`と同名の`.meta`も一緒にリネームする(GUIDを維持するため)

これは[design/client-architecture.md](../../Shared/docs/design/client-architecture.md)の
コード例にも適用済み。

## コメント

- ドキュメントへの参照だけを書いた一言コメント(例: 「design/xxx.md「Yセクション」参照」だけで
  終わるもの)や、クラス名・メソッド名を読めばわかることをそのまま繰り返すだけのコメントは書かない
- コメントを書くなら、コードを読むだけでは伝わらない「概要」や「なぜそうなっているか」が
  一目でわかる内容にする。自明な実装(例: 固定値を返すだけのMock実装)にはコメント自体
  不要な場合が多い

  ```csharp
  // Bad: ドキュメント参照だけ、または自明な内容の繰り返し
  /// <summary>
  /// design/client-architecture.md「実装」参照。インメモリの擬似データを返すだけの実装。
  /// </summary>
  public sealed class MockPlayerRepository : IPlayerRepository { ... }

  // Good: コメント自体を書かない(クラス名・戻り値で自明なため)
  public sealed class MockPlayerRepository : IPlayerRepository { ... }
  ```

## 不要な`async`/`await`

- 実際には非同期で待つ理由が無いのに、`async`/`await`や`UniTask.Delay`等で人工的な待機を
  入れない。同期的に値を返せるなら`UniTask.FromResult`等をそのまま返す

  ```csharp
  // Bad: 待つ理由が無いのにawaitしている
  public async UniTask<PlayerData> GetMeAsync()
  {
      await UniTask.Delay(200);
      return new PlayerData("mock-player-id", "プレイヤー", 300);
  }

  // Good
  public UniTask<PlayerData> GetMeAsync()
  {
      return UniTask.FromResult(new PlayerData("mock-player-id", "プレイヤー", 300));
  }
  ```
