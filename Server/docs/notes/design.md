# サーバー設計

## 全体構成

```
Unity Client
   │ REST (HTTP)              │ gRPC/StreamingHub (MagicOnion)
   ▼                          ▼
Rust/Axum API Server    C#/MagicOnion Server
(認証/スカウト/チャット/     (リアルタイム対戦のみ)
 マッチング/DB書き込み)          │
   ▲──────── 内部API(結果報告) ──┘
   │
 MySQL (唯一のデータストア、書き込みはRust経由に統一)
```

- サーバーは1台構成(ローカル動作を想定)
- Rust側がDBの唯一の書き込み口となる
- MagicOnionは対戦中の判定をメモリ上で行い、終了時にRust内部APIへ結果をPOSTする
- MagicOnionはユーザーDBを持たず、Rustが発行した短命JWT(`battle_token`)を共有シークレットで検証するだけ

## 命名規則

- DB/Rust: `snake_case`で統一。マスタデータは修飾語なし、プレイヤー所持データは必ず`player_`プレフィックスを付ける(`player_pachimon`, `player_pachimon_moves`等)
- 「ポケモン」を指す語はプロジェクト名にちなみ`pachimon`で統一(商標混同を避けるための独自名)
- REST APIのJSONペイロード: `serde(rename_all = "camelCase")`でDB/Rust内部は`snake_case`のままJSONのみ`camelCase`に変換
- MagicOnion Hubのリクエスト/レスポンスDTO(`JoinResult`等)はDBと無関係な独立したC#クラスなので、C#の規約通り`PascalCase`で定義してよい(命名規則の衝突は起きない)

## API設計

### 認証・プレイヤー(既存 + 追加)

| メソッド | パス | 説明 |
|---|---|---|
| POST | /devices | デバイス新規登録 |
| POST | /devices/authenticate | デバイス認証・アクセストークン取得 |
| GET | /auth/verify | アクセストークン検証 |
| POST | /players | プレイヤー作成(device認証後に紐付け) |
| GET | /players/me | 自分のプレイヤー情報取得 |

### チャット(既存、変更なし)

| メソッド | パス | 説明 |
|---|---|---|
| POST | /chat/send | メッセージ送信 |
| GET | /chat/poll | メッセージ受信/取得 |

### パチモン・スカウト

| メソッド | パス | 説明 |
|---|---|---|
| GET | /scout/banners | 開催中のスカウトバナー一覧 |
| POST | /scout/banners/{id}/pull | スカウト実行(1回/10連) |
| GET | /players/me/pachimon | 所持パチモン一覧 |
| PUT | /players/me/party | バトル用パーティ編成(6体まで) |
| PUT | /players/me/pachimon/{player_pachimon_id}/moves/{slot} | 技の付け替え(グループ内の候補技から選択) |

### マッチング(Rust側)

| メソッド | パス | 説明 |
|---|---|---|
| POST | /battle/queue | 待機列に参加 |
| DELETE | /battle/queue | 待機列から離脱 |
| GET | /battle/queue/status | マッチ成立確認(polling) |

マッチ成立時のレスポンス:

```json
{
  "status": "matched",
  "match_id": "01J...",
  "battle_server": "https://battle.example.com",
  "battle_token": "短命JWT(match_id, player_idを含む)"
}
```

- 通知方式は **polling で確定**(1秒程度の遅延はゲーム体験に影響しないため、WSを別途入れるコストに対してメリットが薄いと判断)

### 内部API(MagicOnion → Rust)

| メソッド | パス | 説明 |
|---|---|---|
| POST | /internal/battle/result | 対戦結果・ログをまとめて記録 |

- 内部ネットワークのみ疎通、サービス間シークレットで保護

## MagicOnion Hub設計(C#側)

- `JoinAsync`で`battleToken`を検証 → 対戦グループにアタッチ
- ダメージ計算等の判定は`ConcurrentDictionary<matchId, BattleState>`等のインメモリ状態で確定(クライアントには結果のみ送る、チート対策)
- 終了時に`OnBattleEnd`をブロードキャストしつつ、`/internal/battle/result`へ結果POST

## パチモン選出(自動選出)

マッチング成立後、実際にバトルへ出す3体を選ぶ「選出」フェーズを実装する。UI(手動選択画面)は作らず、クライアントが接続直後に**party_slot順で先頭3体のplayer_pachimon_idを自動送信**する形にする。選出という仕組み自体は本物として実装しておき、将来UIを追加する際は自動送信部分を差し替えるだけで済むようにする。

**フロー**
```
1. マッチ成立 → 両クライアントがMagicOnionに接続してJoinAsync
2. 各クライアントは接続直後、party_slot順で先頭3体のplayer_pachimon_idを自動的にSubmitSelectionAsyncで送信
3. サーバーは両プレイヤーの選出が揃うまで待機(BattlePhase.Selecting)
4. 揃ったらBattlePhase.InProgressへ遷移、OnMatchStartで両者に選出内容を通知してバトル開始
```

**Hubインターフェース**
```csharp
public interface IBattleHub : IStreamingHub<IBattleHub, IBattleHubReceiver>
{
    Task<JoinResult> JoinAsync(string battleToken, string matchId);
    Task SubmitSelectionAsync(string[] playerPachimonIds);  // 常に3体固定、クライアントが自動送信
    Task SubmitMoveAsync(MoveRequest move);
    Task SwitchAsync(int partySlot);
    Task ForfeitAsync();
}

public interface IBattleHubReceiver
{
    void OnMatchStart(BattleStartPayload payload);   // 選出が揃ってから発火
    void OnTurnResult(TurnResultPayload payload);
    void OnBattleEnd(BattleEndPayload payload);
}
```

```csharp
public enum BattlePhase
{
    WaitingForJoin,
    Selecting,      // 実際に使う、両者の選出待ち
    InProgress,
    Finished
}
```

- サーバー側の`BattleState`は選出結果(2プレイヤー分)を保持し、両者揃った時点で`InProgress`に遷移させる
- `battle_turns`の行動対象は選出済み3体の範囲内に限定される(ダメージ計算・交代ロジックもこの3体で完結)

## マスターデータ設計

本家ポケモンのバトルシステムから、天候・フィールド・持ち物・特性・技の追加効果を排除したミニマム構成。レベルアップによる技習得の仕組みは持たず、「技グループ」をパチモンに紐付ける形にしている。

**pachimon(マスタ)**
```
pachimon_id        PK
name
primary_type / secondary_type   -- secondary_typeはNULL可
base_hp / base_atk / base_def / base_spatk / base_spdef / base_speed
rarity              -- スカウトの排出率グルーピング用
move_group_id       FK -> move_groups
```

**move_groups(技グループマスタ)**
```
move_group_id    PK
name             -- 管理用ラベル
```
複数のパチモンが同じグループを共有できるため、新規パチモン追加は基本的に`pachimon`への1行追加(既存グループを指すだけ)で済む。

**move_group_moves(グループが含む技の対応表)**
```
move_group_id    FK -> move_groups
move_id          FK -> moves
is_initial       BOOLEAN DEFAULT FALSE   -- 初期習得技かどうか(グループ内で最大4件になるよう運用で担保)
UNIQUE(move_group_id, move_id)
```

**moves(技マスタ)**
```
move_id        PK
name
type
category       ENUM('physical','special','status')
base_power     NULL可(状態技の場合)
accuracy
max_pp
```
追加効果(状態異常付与・能力変化など)は排除。状態技自体は定義できるが効果の実装は対象外。

**type_chart(タイプ相性マスタ)**
```
attack_type    PK(複合)
defend_type    PK(複合)
multiplier     DECIMAL  -- 0 / 0.5 / 1 / 2
```

## 個体データ(プレイヤー所持)

**player_pachimon**
```
player_pachimon_id    PK
player_id             FK -> players
pachimon_id            FK -> pachimon
level
ivs            JSON
party_slot     INT NULL   -- 1-6、NULL=ボックス。UNIQUE(player_id, party_slot)
effort_values  JSON       -- 例: {"hp":0,"atk":0,"def":0,"spatk":0,"spdef":0,"speed":0}、デフォルト全0(将来の努力値64ポイント配分用、現状はステータス計算に加算するだけで未使用)
obtained_at    DATETIME(3)
```

**player_pachimon_moves(現在覚えている技)**
```
player_pachimon_id    FK -> player_pachimon
slot           INT (1-4)
move_id        FK -> moves
UNIQUE(player_pachimon_id, slot)
```
スカウトで個体が生成される際、対応する`move_group_moves`の`is_initial = TRUE`の行をそのまま複製して初期セットする。技の付け替えは、このテーブルの対象slotをUPDATEするだけで実現でき、マスタ側の変更は不要。

## その他DB設計

```sql
-- 既存
devices
  device_id       CHAR(26) PRIMARY KEY   -- ULID
  device_secret   ...
  created_at      DATETIME(3)

access_tokens
  device_id       CHAR(26) PRIMARY KEY   -- 1デバイス1トークン、再認証時に上書き
  access_token    ...
  expires_at      DATETIME(3)

messages
  message_id    CHAR(26) PRIMARY KEY
  player_id     CHAR(26)               -- FK -> players
  content       TEXT
  created_at    DATETIME(3)

-- 追加
players
  player_id     CHAR(26) PRIMARY KEY
  device_id     CHAR(26)               -- FK -> devices
  nickname      VARCHAR(...)
  gems          INT                    -- スカウト用課金石
  created_at    DATETIME(3)

scout_banners  -- マスタ
  banner_id     ... PRIMARY KEY
  name          VARCHAR(...)
  rate_table    JSON                   -- レアリティ別確率
  cost_per_pull INT
  start_at / end_at  DATETIME(3)

scout_pulls  -- 履歴
  pull_id       CHAR(26) PRIMARY KEY
  player_id     CHAR(26)               -- FK -> players
  banner_id     ...                    -- FK -> scout_banners
  pachimon_id   ...                    -- FK -> pachimon (結果)
  created_at    DATETIME(3)

battle_matches
  match_id      CHAR(26) PRIMARY KEY
  player1_id / player2_id  CHAR(26)    -- FK -> players
  status        ENUM('matching','in_progress','finished')
  winner_id     CHAR(26) NULL
  player1_selected_pachimon   JSON      -- 選出されたplayer_pachimon_id 3体分(自動選出でも実データとして記録)
  player2_selected_pachimon   JSON
  started_at / ended_at  DATETIME(3)

battle_turns  -- 対戦ログ
  turn_id       CHAR(26) PRIMARY KEY
  match_id      CHAR(26)               -- FK -> battle_matches
  turn_number   INT
  player_id     CHAR(26)               -- FK -> players (行動者)
  action_data   JSON
  result_data   JSON
  created_at    DATETIME(3)
```

補足:
- ULIDは`CHAR(26)`で文字列のまま保持(速度重視なら`BINARY(16)`エンコードも選択肢)
- `rate_table` / `ivs` / `effort_values` / `action_data` / `result_data` / `player1_selected_pachimon` / `player2_selected_pachimon`はMySQLの`JSON`型を使用
- `battle_server_id`はサーバー1台構成のため不要と判断し省略
- マッチング待機列(`matchmaking_queue`)はDB永続化せず、Rustプロセスのメモリ(チャネル等)で管理(1台構成のため問題なし)

## スカウトの確率設計

`scout_banners.rate_table`にレアリティごとの排出率をJSONで持たせる(合計1.0)。

```json
{
  "S": 0.03,
  "A": 0.12,
  "B": 0.35,
  "C": 0.50
}
```

**抽選ロジック(Rust側)**
1. `rate_table`から乱数でレアリティを1つ抽選(重み付き抽選)
2. 決まったレアリティに紐づく`pachimon`(`rarity`カラム一致)を全件取得
3. その中から等確率で1件をランダム選出
4. `player_pachimon`に登録、`move_group_moves`の`is_initial`技を`player_pachimon_moves`にコピー
5. `scout_pulls`に結果を記録

- 同レアリティ内は均等抽選(重み付けが必要になったら`pachimon`に`weight`カラムを追加で対応)
- 10連は「1連を10回呼ぶ」実装で十分(レア確定枠・ピックアップ演出は初期スコープ外)

## パーティ編成バリデーション(PUT /players/me/party)

1. 人数は1〜6体(0体では`/battle/queue`への参加不可)
2. 同一`player_pachimon_id`を複数slotに設定不可
3. 同一パチモンの重複は許可(本家の「同種族1体まで」制約は入れない)
4. 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体であることをサーバー側で検証
5. レベル制限はなし(将来拡張)

## バトルコアロジック(ダメージ計算・命中率)

本家ポケモンの基本式から、天候・フィールド・持ち物・特性・急所ランク変動・命中/回避ランク変動・優先度技・性格補正を除いたミニマム版。

**1ターンの処理順**
```
1. 両プレイヤーの行動(技 or 交代)をサーバーが受信
2. 行動優先度を決定(交代は技より先、技同士は素早さ比較)
3. 行動順に処理(命中判定 → ダメージ計算 → HP減算 → 瀕死チェック)
4. 両者の行動が終わったら turn_result をブロードキャスト
```

**行動順序**
- 交代は必ず技より先に処理
- 技同士は実効`speed`が高い方が先、同値ならランダム
- 優先度技(でんこうせっか等)は実装しない(純粋に素早さ比較のみ)

**命中判定**
- 技マスタの`accuracy`(固定値)を使った判定は実装する
```
乱数(1〜100) <= move.accuracy なら命中
```
- 命中/回避ランクを変動させる効果(追加効果に該当)は実装しない

**ダメージ計算式**
```
damage = floor(floor(floor(2 * level / 5 + 2) * base_power * A / D) / 50 + 2)
         * STAB * TypeEffectiveness * Critical * Random
```
- `A` = 攻撃側の実効ステータス(物理技ならatk、特殊技ならspatk)
- `D` = 防御側の実効ステータス(物理技ならdef、特殊技ならspdef)
- `STAB` = 技のtypeが攻撃側の`primary_type`または`secondary_type`と一致するなら`1.5`、それ以外`1.0`
- `TypeEffectiveness` = `type_chart`から取得(複合タイプは掛け合わせ)
- `Critical` = 発生率固定(1/16程度)、発生時ダメージ`1.5`倍。ランク変動要素はなし
- `Random` = `0.85〜1.00`の乱数

`category = 'status'`(状態技)は追加効果を実装しない都合上、現状は効果なし。初期習得技(`is_initial`)は物理/特殊技のみで構成する運用とする。

**実効ステータス計算**
```
HP以外 = floor((2 * base + IV + floor(EV/4)) * level / 100) + 5
HP     = floor((2 * base + IV + floor(EV/4)) * level / 100) + level + 10
```
`effort_values`は現状全て0。性格(nature)補正は本家にあるがマスタ設計に存在しないためなし。

**瀕死・交代**
- HPが0になったら瀕死、行動不能
- 選出済み3体全員が瀕死になった時点で敗北、`battle_end`
- 瀕死時は次ターン開始前に強制交代を要求(未交代ならターンスキップ)

## 将来拡張(現時点では未実装)

1. 技の入れ替えUI・選択ロジックの高度化(候補が5件以上ある場合の絞り込み等) — スキーマは対応済み
2. 努力値(64ポイント)の自由配分機能とその上限チェック — `effort_values`カラムは用意済み、配分ロジック・UIは未実装

## 未確定の論点

- パーティ編成のレベル制限は将来拡張として保留
