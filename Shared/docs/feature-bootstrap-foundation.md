# BootLoader(起動時初期化) 設計書(ドラフト)

> 本書は Supplement の機能として作る、起動時の初期化処理をタスクとして順序立てて実行する共通基盤(BootLoader)の設計ドラフト。
> 作成後は Supplement リポジトリへ移管する。
> [feature-screen-navigation-package.md](feature-screen-navigation-package.md)(画面遷移パッケージ)、
> [feature-grpc-foundation.md](feature-grpc-foundation.md)(gRPC API基盤)とは別の提案として扱う。

- ステータス: ドラフト(確定事項と未確定事項が混在。各項目に明記)
- 参考: 別プロジェクト(Rise)の `Bootstrap` フォルダ(`BootLoader` / `IBootInitializationTask` / `SystemLifetimeScope`)

---

## 1. 目的

起動時の初期化処理(AssetLoader の準備、マスターデータ読み込み等)を、**タスクとして登録し、優先度に基づいて
順序立てて実行する**共通基盤を、Supplement の機能として提供する。
各初期化タスクの具体的な内容(何を・どう初期化するか)はアプリ側が定義し、Supplement 側は実行の仕組みだけを持つ。

## 2. 仕組み(参考実装をもとにした案) 【確定】

```csharp
public interface IBootInitializationTask
{
    InitializationPriority Priority { get; }
    UniTask InitializeAsync(CancellationToken ct);
}

public enum InitializationPriority
{
    Highest = 0, High = 100, AboveNormal = 200, Normal = 300,
    BelowNormal = 400, Low = 500, Lowest = 600,
}
```

- `IBootInitializationTask` の実装を複数登録し(DIで `IEnumerable<IBootInitializationTask>` として受け取る)、
  `Priority` でグループ化して**小さい順に実行**する。**同じ Priority のものは並列**に実行する
- `BootLoader` を EntryPoint として登録し、起動時に実行する
- 完了を待つ側は、`WaitForInitializedAsync(ct)` のような非同期の待機メソッドを使う
  (ポーリングではなく、失敗時は例外がそのまま伝わる形にする。3章の参考実装の反省点)
- **具象の初期化タスク(AssetLoader、マスターデータ読み込み等)はアプリ側で定義する**。
  Supplement 側が持つのは `BootLoader` 本体、`IBootInitializationTask`、`InitializationPriority` のみ

```
Supplement: BootLoader、IBootInitializationTask、InitializationPriority
アプリ側:   AssetLoaderInitializer、MasterDataInitializer などの具体的な実装
```

## 3. 参考実装(Rise)からの改善点 【確定】

参考にした実装の `ValueTask`/`bool IsInitialized` によるポーリング方式には、次の課題があった。
Supplement の実装ではこれらを解消する。

1. **非同期処理は UniTask にする**(画面遷移パッケージと同じ方針)
2. **初期化の失敗を握りつぶさない**。`Start()` 内で `Initialize().Forget()` すると、
   タスクが例外を投げた場合に `IsInitialized` が true にならず、`WaitUntil` で待つ側が**永久に待ち続ける**
   (エラーはログに出るだけ)。`WaitForInitializedAsync(ct)` として、失敗したら例外がそのまま伝わる形にする
3. **二重実行を防ぐ**。`Initialize` を誰でも呼べる状態にしない
4. Enum 名のつづりを直す(参考実装の `InitilizationPriority` → `InitializationPriority`)

## 4. IReinitializable(解放と再初期化) 【保留】

- 通信エラーでタイトルに戻す等の場面で必要になる可能性があるが、**現時点では必要性を感じていない**ため保留する
- 参考実装には `Cleanup(ct)` / `Reinitialize(ct)` を持つ `IReinitializable` があり、
  BootLoader から「Cleanupは逆順、Reinitializeは正順」で呼ぶ想定だったが、参考実装内でも実際には呼ばれていなかった
- 必要になった時点で設計する

---

## 5. 確定事項・未確定事項一覧

### 確定

| # | 項目 | 内容 | 節 |
|---|---|---|---|
| 1 | BootLoader | Supplement の機能にする。具象の初期化タスクはアプリ側で定義する | 2 |
| 2 | 実行順 | Priority でグループ化し、小さい順に実行(同一Priorityは並列) | 2 |
| 3 | 待機方法 | ポーリングではなく非同期の待機メソッドにし、失敗は例外で伝える | 2, 3 |
| 4 | IReinitializable | 保留(必要になった時点で設計する) | 4 |

### 未確定

なし(現時点)
