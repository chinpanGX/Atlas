# 共通設計(アーキテクチャ・命名規則・マスターデータ)

バトル/スカウト/アウトゲームの各設計書に共通する、横断的な内容をまとめる。

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

- DB/Rust: `snake_case`で統一。マスタデータは修飾語なし、プレイヤー所持データは必ず
  `player_`プレフィックスを付ける(`player_pachimon`, `player_pachimon_moves`等)
- 「ポケモン」を指す語はプロジェクト名にちなみ`pachimon`で統一(商標混同を避けるための独自名)
- REST APIのJSONペイロード: `serde(rename_all = "camelCase")`でDB/Rust内部は`snake_case`の
  ままJSONのみ`camelCase`に変換
- MagicOnion Hubのリクエスト/レスポンスDTO(`JoinResult`等)はDBと無関係な独立したC#クラス
  なので、C#の規約通り`PascalCase`で定義してよい(命名規則の衝突は起きない)

## マスターデータ設計

本家ポケモンのバトルシステムから、天候・フィールド・持ち物・特性・技の追加効果を排除した
ミニマム構成。レベルアップによる技習得の仕組みは持たず、「技グループ」をパチモンに紐付ける
形にしている。スキーマ本体は`Shared/master-data/schema/`(`master-data-pipeline`が読み込む
正本)、投入データは`Shared/master-data/csv/`。

### pachimon(マスタ)

```
pachimon_id        PK(4桁、1001〜)
name
primary_type / secondary_type   -- secondary_typeが無い個体はPachimonType::NONE(id:0)
base_hp / base_atk / base_def / base_spatk / base_spdef / base_speed
rarity              -- スカウトの排出率グルーピング用(S/A/B/C)
move_group_id       FK -> move_groups
```

### move_groups(技グループマスタ)

```
move_group_id    PK
name             -- 管理用ラベル
```

複数のパチモンが同じグループを共有できる設計(FKとして独立)。現状の投入データでは
`pachimon_id`と同値(1パチモン=1グループ)にしているだけで、スキーマ上の制約ではない。

### move_group_master(技グループが含む技の対応表、旧称move_group_moves)

```
unique_id        PK   -- 代理キー。パイプラインが複合PK/複合UNIQUEに非対応のため追加
group_id         FK -> move_groups
move_id          FK -> moves
is_initial       BOOLEAN   -- 初期習得技かどうか(グループ内で最大4件になるよう運用で担保)
```

### moves(技マスタ)

```
move_id        PK
name
move_type      -- 技のタイプ(列名は`type`ではなく`move_type`。Rustの予約語`type`と
                  衝突するため。詳細はmaster-data-pipelineのREADME参照)
category       ENUM('physical','special','status')
base_power     状態技の場合は0(パイプラインがnullable型に非対応のため)
accuracy
max_pp
```

追加効果(状態異常付与・能力変化など)は排除。状態技自体は定義できるが効果の実装は対象外。

### type_chart(タイプ相性マスタ)

```
attack_type      PK(複合) FK -> pachimon_type
defend_type      PK(複合) FK -> pachimon_type
effectiveness    ENUM(type_effectiveness.yaml)  -- IMMUNE / NOT_VERY_EFFECTIVE / NORMAL / SUPER_EFFECTIVE
```

`master-data-pipeline`の型システムがint/string/bool/enumのみで小数非対応のため、`multiplier`
(DECIMAL)ではなく`effectiveness`(ENUM)として持つ。マスタが持つのは`(attack_type,
defend_type) → effectiveness`という関係(マップ)のみで、`effectiveness`(ENUM)→倍率
(0 / 0.5 / 1 / 2)への変換テーブルはデータ化せず`Atlas.BattleCore`(C#)側にハードコードする。
ダメージ計算そのものを行うのは`Atlas.BattleCore`だけであり、Rust APIサーバーはこの変換
ロジックを持つ必要がない。単体タイプ同士の組み合わせのみをマスタに持ち、複合タイプ
(secondary_type)の掛け合わせは`Atlas.BattleCore`が実行時に2回引いて乗算する(この乗算
ロジックもハードコード)。「該当行が無ければ1倍」という暗黙のデフォルトは避け、全タイプ
×全タイプの組み合わせを明示的にCSVへ埋める。

## マスターデータ運用

- 正(source of truth)は`Shared/master-data/`のスキーマ・CSV。
  `master-data-pipeline`(別リポジトリ、Atlas配下にclone)がこれを読み込み、Unity/realtime_server
  共有のC#型(`Shared/MasterData/`、後述)・Unity向け`masterdata.bytes`・realtime_server向け
  `masterdata.bytes`相当のバイナリ・Rust向け`*.rs`/`*.json`を生成する
- C#の型(Models/Enums)は`Shared/MasterData/`にUnity Package(UPM)+薄い`.csproj`として配置し、
  ClientとrealtimeServerの両方が同じ型を参照する(`Shared/BattleCore`と同じ構成。詳細は
  [battle.md](battle.md)参照)
- 実データ(値)の読み込み方法は対象ごとに異なる:
  - Client: `Assets/StreamingAssets/masterdata.bytes`をアプリ起動時に読み込む
  - Rust/Axum: 起動時にDBから全マスタを1回読み込み、`Arc<MasterData>`としてメモリに保持する
    (`Server/src/master/cache.rs`)。各リクエストハンドラはこのキャッシュを参照するだけ
  - realtime_server: Clientと同様に`masterdata.bytes`相当のバイナリを`RealtimeServer/`配下に
    直接配置し、起動時に読み込んでメモリに保持する。DBへの接続やRust内部APIへの問い合わせは
    行わない(Rustの「DBキャッシュ」方式より単純な「ファイル配置のみ」で済ませる)
- マスタ更新の反映は「`cargo run --bin seed_master_data`でJSON→DBへUPSERT → Rustサーバー再起動」
  (Rust)、「pipelineで生成→配置→再起動」(Client/realtime_server)で行う。無停止反映の仕組みは、
  1台構成の現状の規模では過剰と判断し採用しない
- Clientへのmasterdata配布をCDN(S3+CloudFront)経由にする案を検討中だが、バージョニング・
  差分検知等の詳細は現時点では対象外として保留する。realtime_server/Rustの読み込み方式には
  影響しない
- 実装状況(`pachimon`のみDB投入・キャッシュ済み、`moves`/`move_groups`/`move_group_master`は
  ファイル生成のみでDB未投入 等)は[progress.md](../progress.md)を参照
