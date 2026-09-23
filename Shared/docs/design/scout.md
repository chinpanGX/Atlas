# スカウト(ガチャ)設計

gems(石)を消費し、候補10体の中から1体を選んで入手する抽選機能の詳細設計。全体構成・
命名規則は[architecture.md](architecture.md)を参照。

## 概要

『ポケモンチャンピオンズ』の「紹介(候補10体提示→1体選択)」という体験をベースに、
トライアルスカウト・VP・専用チケット・色違い演出といった周辺要素を省いたシンプルな設計。
通貨は新設せず、既存の`gems`をそのまま使う。

1回の紹介でgemsを消費し、サーバー側でランダム生成した候補10体を提示する。プレイヤーは
その中から1体を選んで恒久的に入手する(選ばなかった9体は破棄され、控えとして残らない)。
トライアル(期限付き仮所持)の概念は無く、選択した時点でそのまま本所持(育成・パーティ編成
可能)になる。

`scout_banners`(バナー情報・`rate_table`)は`pachimon`/`moves`等とは異なり
`master-data-pipeline`の対象外とし、Rustサーバーの DB にのみ保持する
(Unityクライアントには配布しない)。理由は2つ:
開催期間(`start_at`/`end_at`)を持つ運用寄りのデータであり、クライアント再ビルド無しで
切り替えられるようにしたいこと、そして排出率(`rate_table`)は`GET /scout/banners`の
レスポンスにも含めず、抽選確率の生値をクライアントに渡さないようにするため。
クライアントは`GET /scout/banners`をその都度呼び出し、開催中のバナー一覧(排出率を除く)
を取得する。

バナーは`master-data-pipeline`(CSV管理)ではなく、簡易な`seed`スクリプト
(`seed_master_data`とは別の専用バイナリ、例: `cargo run --bin seed_scout_banners`)で
Rust側からDBへ直接INSERT/UPSERTする運用とする。期間限定バナーの運用ツールは作らず、
`end_at`を十分先の未来日付にした「常設バナー」を最低1件、このseedスクリプトで投入する
(常設バナーが常に1件は開催中である状態を保証し、レギュラースカウトが常に実行できるように
する)。

## API仕様

| # | メソッド | パス | 説明 | 認証 |
|---|---|---|---|---|
| 1 | GET | `/scout/banners` | 開催中のスカウトバナー(セレクト)一覧 | 要 |
| 2 | POST | `/scout/rolls` | 紹介を受ける(gems消費・候補10体をロール) | 要 |
| 3 | POST | `/scout/rolls/{rollId}/select` | 候補から1体を選んで入手 | 要 |

### 1. スカウトバナー一覧

```
GET /scout/banners
```

開催中のスカウトバナー(排出率グループ)一覧を取得する。

レスポンス

```json
{
  "banners": [
    { "bannerId": "...", "name": "ピックアップスカウト", "costPerRoll": 150, "startAt": "...", "endAt": "..." }
  ]
}
```

### 2. 紹介を受ける

```
POST /scout/rolls
```

リクエスト

```json
{ "bannerId": "..." }
```

`bannerId`は`GET /scout/banners`で取得した開催中バナーのIDを指定する。呼び出すたびに1回分の
gemsを消費し、新しい候補10体をロールする。

`bannerId`をURLパス(`/scout/banners/{id}/rolls`)ではなくbodyに置いているのは、一般的なREST設計
(識別子はパスに置く)よりも、ソーシャルゲームでよく使われるRPCスタイル(パラメータをbodyに集約し、
仕様変更に強くする)を優先した判断。`/chat/send`等、他のAtlas APIの命名とも一貫性がある。

処理の流れ:

1. 対象バナーが開催期間内(`start_at <= now <= end_at`)か検証
2. `player_items`(`item_id: 1`、gems)の`quantity`から`cost_per_roll`を減算(不足していれば`400`)
3. 候補10体を独立に抽選(下記「抽選ロジック」参照)。パチモン・技はこの時点で確定する
4. `scout_rolls`に候補10体をまとめて保存(`selected_index`は`NULL`)
5. 候補一覧とgems消費後の`playerDiff`を返す

gemsを消費するのはこの`rolls`(紹介を受ける)時点であり、次の`select`(候補から選ぶ)では
gemsは変化しない。

レスポンス

```json
{
  "rollId": "...",
  "candidates": [
    {
      "index": 0,
      "pachimonId": 12,
      "rarity": "S",
      "moves": [3, 7, 12, 18]
    }
  ],
  "playerDiff": {
    "items": { "upserted": [ { "itemId": 1, "quantity": 700 } ], "removed": [] },
    "pachimon": { "upserted": [], "removed": [] },
    "pachimonMoveMap": { "upserted": [], "removed": [] },
    "partySlots": { "upserted": [], "removed": [] }
  }
}
```

### 3. 候補から1体を選んで入手

```
POST /scout/rolls/{rollId}/select
```

リクエスト

```json
{ "index": 3 }
```

処理の流れ:

1. `rollId`が呼び出し元プレイヤー自身のものであること、`selected_index`が未確定である
   ことを検証(二重選択防止)
2. `index`の妥当性検証(0-9)
3. `candidates[index]`の内容(`pachimonId`・`moves`)をそのまま`player_pachimon` /
   `player_pachimon_moves`へコピーして登録(再抽選はしない。紹介時点で確定済みの個体を
   そのまま入手する)
4. `scout_rolls.selected_index` / `selected_at`を更新
5. 選ばなかった9体はどこにも永続化されない

レスポンス: `playerDiff`([architecture.md](architecture.md)参照)。gemsはこの時点では変化しない
(前述の通り`rolls`で消費済み)ため`items`は空配列。入手したパチモン1件を`pachimon.upserted`に、
その初期技を`pachimonMoveMap.upserted`に載せる。`rarity`はレスポンスに含めない
(`pachimonId`からクライアントのマスターデータを引けば分かるため)。

```json
{
  "playerDiff": {
    "items": { "upserted": [], "removed": [] },
    "pachimon": {
      "upserted": [ { "playerPachimonId": "...", "pachimonId": 12 } ],
      "removed": []
    },
    "pachimonMoveMap": {
      "upserted": [
        { "playerPachimonMoveId": "...", "playerPachimonId": "...", "slot": 1, "moveId": 3 }
      ],
      "removed": []
    },
    "partySlots": { "upserted": [], "removed": [] }
  }
}
```

## 確率設計

`scout_banners.rate_table`にレアリティごとの排出率をJSONで持たせる(合計1.0)。候補10体は
この`rate_table`を使って1体ずつ独立に抽選する(10体まとめて何らかの傾斜をかけたりはしない)。

```json
{
  "S": 0.03,
  "A": 0.12,
  "B": 0.35,
  "C": 0.50
}
```

## 抽選ロジック(Rust側)

候補10体それぞれについて、以下を独立に実行する。

1. `rate_table`から乱数でレアリティを1つ抽選(重み付き抽選)
2. 決まったレアリティに紐づく`pachimon`(`rarity`カラム一致)を全件取得し、その中から
   等確率で1件をランダム選出
3. `move_group_moves`の`is_initial = TRUE`の技を、その候補の確定技セットとして保持

個体値(IV)の概念は持たない(ポケモンチャンピオンズ準拠で廃止。詳細は[outgame.md](outgame.md)の
`player_pachimon`定義、[battle.md](battle.md)の「実効ステータス計算」参照)。個体差は
努力値(`effort_values`、育成要素)のみで表現する。

生成した10体分をまとめて`scout_rolls.candidates`(JSON)に保存する。選択(`select`)時は
再抽選を行わず、保存済みの内容をそのまま`player_pachimon`にコピーするだけでよい。

- 同レアリティ内は均等抽選(重み付けが必要になったら`pachimon`に`weight`カラムを追加で対応)
- 色違いなど個体の特殊カラーは初期スコープ外

## DB設計

### scout_banners(マスタ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `banner_id` | CHAR(26) | PRIMARY KEY | ULID |
| `name` | VARCHAR(100) | NOT NULL | |
| `rate_table` | JSON | NOT NULL | レアリティ別確率。合計1.0(例: `{"S":0.03,"A":0.12,"B":0.35,"C":0.50}`) |
| `cost_per_roll` | INT | NOT NULL | 紹介1回(候補10体ロール)あたりのgems消費量 |
| `start_at` | DATETIME(3) | NOT NULL | |
| `end_at` | DATETIME(3) | NOT NULL | |

### scout_rolls(紹介の記録)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `roll_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | |
| `banner_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `scout_banners.banner_id` | |
| `candidates` | JSON | NOT NULL | 候補10体の配列。各要素は`{"pachimonId":12,"rarity":"S","moves":[3,7,12,18]}` |
| `selected_index` | INT | NULL可 | 選択された候補のindex(0-9)。未選択は`NULL` |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 紹介を受けた日時(gems消費日時) |
| `selected_at` | DATETIME(3) | NULL可 | 選択(入手確定)した日時 |

`candidates`に選択結果も含めて記録するため、紹介履歴と入手履歴を兼ねる(旧`scout_pulls`
相当のテーブルは不要)。抽選結果として登録される個体データ(`player_pachimon`)の詳細は
[outgame.md](outgame.md)を参照。

## 未確定の論点

- gemsは紹介(`rolls`)作成時点で消費が確定する。選択(`select`)せず放置した場合も
  返却されない(本家のトライアル無料仕様とは異なるが、シンプル化のためこの挙動を許容する)。
  未選択のロールを取り直すAPIが無いため、クライアントは候補を確定するまでスカウト画面から
  戻れないようにしている(アプリの終了や通信エラーで離れた場合はそのロールは選べなくなる)
- 未選択のまま残った`scout_rolls`に有効期限・自動失効の仕組みは設けない
  (初期スコープ外。運用上問題になれば追加検討)
