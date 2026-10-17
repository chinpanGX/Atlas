---
title: "バトルの計算(Atlas.BattleCore)"
status: current
area: battle-server
related: [game/battle-rules, battle-server/battle-flow, master-data/tables, client/testing]
code: [Shared/BattleCore/Runtime, BattleServer/Battle/LoadoutBuilder.cs, Client/AtlasUnityProject/Assets/Scripts/MasterData/PachimonStatCalculator.cs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Mock/TestPartyFactory.cs]
updated: 2026-10-18
---

# バトルの計算(Atlas.BattleCore)

Client の Mock 対戦とバトルサーバーの両方で、同じダメージ計算・ターン処理を使うための共通モジュール。

- `UnityEngine`・MagicOnion・通信の型・マスターデータの生成型のどれにも依存しない。自前の最小の型(`ElementType`、`MoveCategory`、
  `ParticipantStats`、`MoveData` 等)だけを持つ
- マスタ + 所持データ(ステータスポイント)から BattleCore の型への変換は、呼び出し側が持つ(バトルサーバーは `LoadoutBuilder`、Client の Mock は `TestPartyFactory`)
- 乱数は `IRandomSource`、タイプ相性は `ITypeChart` で外から渡す(テストで固定値に差し替えるため)
- 入口は `BattleEngine.ProcessTurn(state, player1 の行動, player2 の行動, typeChart, random)` だけ。行動が無い(時間切れ等)側は `null` を渡す

## 内部の構造

CEDEC 2026 のポケモン・バトルシステムの講演にある考え方を採る。処理を再利用できる単位(Section)の階層に分け、技ごとの個別の効果は、
Section が発火するイベントに反応するハンドラとして足す(Section を変えずに追加できるようにする)。

```
ターン処理
├─ 強制交代の確認         前のターンに瀕死になった側に交代を求める
├─ 行動順の決定           交代 > 技。技どうしはすばやさ、同じならランダム(TurnResolver)
└─ 行動の実行(行動順に)
    ├─ 交代
    └─ 技
        ├─ 命中判定
        ├─ ダメージ(攻撃力・防御力の決定 → 算出。DamageCalculator)
        ├─ 瀕死の確認       選出が全員瀕死なら決着
        └─ 技の後処理       「技が当たった」イベント(MoveHitEvent)を発火。現状ハンドラは無い
```

天候・特性・持ち物は恒久的に対象外のため、それらのイベントは作らない。拡張点は「技が当たった後」の1か所(将来の追加効果用)だけ。

## 計算

```
命中:     1〜100 の乱数 <= 技の命中率
ダメージ: floor(floor(floor(2 * 50 / 5 + 2) * 威力 * A / D) / 50 + 2) * タイプ一致 * タイプ相性 * 急所 * 乱数
能力:     HP     = floor(2 * 種族値 * 50 / 100) + 50 + 10 + ステータスポイント
          HP 以外 = floor(2 * 種族値 * 50 / 100) + 5 + ステータスポイント
```

- A/D は、物理技ならこうげき/ぼうぎょ、特殊技ならとくこう/とくぼう
- タイプ一致1.5倍、タイプ相性0/0.5/1/2倍(2タイプなら2回かけ合わせる)、急所は1/16で1.5倍、乱数は0.85〜1.00
- 性格の補正は無い(マスタに性格が無いため)
- PP: 技を選んだ時点で1減る(外れても減る)。PP が0の技を渡されると `ArgumentException` にする(呼び出し側が送らせない前提。Client は押せなくし、
  バトルサーバーは受け付けない)
- 変化技は現状効果なし
- レベル50の固定値と能力の式は、バトルサーバーの `LoadoutBuilder` と、Client の `PachimonStatCalculator`(画面表示・Mock 用。ポイントは省略でき、
  今の呼び出し元は渡していない)の2か所にある
- ステータスポイント(合計64)は、種族値から計算した値にそのまま足す([パーティ編成・育成](../game/party-training.md))。本家の努力値の
  `floor(努力値 / 4)` の項は、最大252を前提にした式なので外した。内部 API の型名(`EffortValues`)は努力値のころのまま

## テスト

Unity の EditMode テスト(`Shared/BattleCore/Tests/`)で、乱数(`FixedRandomSource`)とタイプ相性(`StaticTypeChart`)を固定して、
ダメージ計算・行動順・ターン処理を確かめる。
