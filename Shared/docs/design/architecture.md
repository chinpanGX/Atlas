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

## クライアント利用ライブラリ

画面遷移・DI・コア進行ロジックのMock/Real切り替えといったクライアント側の骨格設計は
[client-architecture.md](client-architecture.md)を参照。

UnityClientのビルド設定はIL2CPP前提。パッケージ導入経路が3種類あり、ライブラリごとに異なる。

- **OpenUPMスコープレジストリ**(`com.cysharp`/`jp.hadashikick`/`com.github.messagepack-csharp`スコープ、`manifest.json`の`scopedRegistries`経由):
  - DI: VContainer(`jp.hadashikick.vcontainer`)
  - 非同期: UniTask(`com.cysharp.unitask`)
  - リアルタイム対戦通信: MagicOnion Client(`com.cysharp.magiconion.client.unity`)。トランスポートは`Grpc.Net.Client`標準の`SocketsHttpHandler`ではなく`YetAnotherHttpHandler`(`com.cysharp.yetanotherhttphandler`)を使う。IL2CPP環境では標準ハンドラのHTTP/2(ALPN)ネゴシエーションが不安定なため、Editor/IL2CPP問わず同一のネイティブHTTP/2実装に統一する
  - MessagePack for C#のUnity拡張(`com.github.messagepack-csharp`、実体は「MessagePack.Unity」。Vector2/Vector3等のUnity型フォーマッタのみを提供する薄いレイヤーで、MessagePack本体は含まない)
- **UnityNuGetレジストリ**(`org.nuget`スコープ)+ **NuGetForUnity**(`Assets/packages.config`、`Assets/Packages/`配下に実体を配置):
  MagicOnion.Client本体やMasterMemory/MessagePackの本体・Source Generatorは、UPM版が薄いラッパー(abstractionsのみ)や実行時ライブラリのみでSource Generator DLLを含まないため、NuGet版(本体+Source Generator)を別途導入している。
  - シリアライズ: MessagePack for C#本体(NuGetForUnity、`MessagePack`/`MessagePack.Annotations`)
  - マスターデータ: MasterMemory本体(NuGetForUnity、`MasterMemory`/`MasterMemory.Annotations`) — `master-data-pipeline`生成物のランタイム
  - MagicOnion.Client本体(NuGetForUnity、`MagicOnion.Client`/`MagicOnion.Abstractions`/`MagicOnion.Serialization.MessagePack`/`MagicOnion.Shared`)
  - Grpc.Net.Client関連・System.IO.Pipelines等の依存(`org.nuget.*`、YetAnotherHttpHandler/MagicOnion.Clientの前提ライブラリ)
- **git submodule**(Atlasリポジトリ直下にclone、`file:`ローカルパッケージ参照):
  - 補助ユーティリティ: Supplement(`chinpanGX/Supplement`、`com.chinpangx.supplement`) — Addressables経由のAssetLoader/SceneLoader抽象、暗号化付きローカルセーブデータ永続化(`ISaveDataRepository`/`IFileStorageService`)、VContainer登録拡張、`IMessageBroker`(画面をまたぐ通知、client-architecture.md参照)等。UniTask/VContainer/Addressablesに依存
- REST通信(認証/スカウト/チャット): 上記とは独立して`UnityWebRequest`のまま。`api-codegen`が生成する`Atlas.Infrastructure.Api`(`UnityWebRequest`を`UniTask`でラップ、VContainer非依存)を利用する。gRPC側への統一は行わない(RustサーバーをOpenAPI/RESTからprotobuf/gRPCへ作り直すコストに見合わないため)
- アセット管理: Addressables(`com.unity.addressables`、導入済み)
- 画面遷移: UnityScreenNavigator(`com.harumak.unityscreennavigator`、Gitパッケージ、MIT License、導入済み。client-architecture.md参照)
- View↔Presenter間のリアクティブ購読: R3(`com.cysharp.r3`、Gitパッケージ、導入済み、design/battle.md・client-architecture.md参照)
- 画面をまたぐ通知: ZeroMessenger(NuGetForUnity経由、`Assets/Packages/ZeroMessenger.1.0.4/`。SupplementがNuGet版1.0.4を要求するため、Supplement側の配置をそのままAtlas Client側にもコピーする形で導入。Supplementの`IMessageBroker`(`Supplement.ZeroMessenger.GlobalMessageBroker`)経由でのみ使い、`MessageBroker<T>`を直接扱わない)
- テスト: Unity Test Framework(導入済み)。モンキーテスト(Anjin等)は実装が一定進んでから改めて検討する

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
クライアントコードは`pachimon.move_group_id`から`move_group_moves`を直接引けるため、
`targets: [server]`とし、クライアント向けMemoryTable/masterdata.bytesへの出力対象からは
除外している(DB保存・`check_relation`検証対象としては残る)。

### move_group_moves(技グループが含む技の対応表)

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
  `master-data-pipeline`(別リポジトリ、Atlas配下にclone)がこれを読み込み、C#型(Models/Enums、
  `MasterMemory`+`MessagePack`ベース)・`masterdata.bytes`(常に暗号化)・Rust向け`*.rs`/`*.json`
  を生成する
- C#の型(Models/Enums)とローダー(`MasterDataLoader.cs`/`AesCrypto.cs`)はUnityEngineに依存しない
  Pure C#で生成される。**専用の共有パッケージ(UPM等)は作らない**。`master-data-pipeline`自体が
  同一の生成物をClientとバトルサーバーの両方へ個別にコピーする仕組み(`copy-models` /
  `copy-loader` / `copy-client-bytes` / `copy-realtime-bytes`)を既に持っているため、
  `config.yaml`の`copy_destinations`(`realtime_loader_dest_dir`/`realtime_bytes_dest_dir`は
  `BattleServer/`配下を指すよう設定済み、プロジェクト作成後に有効化する)を書き換えるだけで
  両方に同じ型・同じ実データが配置される(2箇所にコピーされるだけで、実体は常にスキーマ+CSV
  から再生成されるため乖離しない)
- **Models/Enums/Loader/AesCryptoは全て同一アセンブリにまとめる**(Client側は
  `Assets/Scripts/MasterData/`配下、単一の`Atlas.MasterData.asmdef`)。Domain/Infrastructureの
  ようなレイヤー分割はしない。理由はMasterMemory/MessagePackのSource
  Generator(`[MemoryTable]`/`[MessagePackObject]`からMemoryDatabase/DatabaseBuilder等を生成する
  仕組み)が同一アセンブリ内でのみ有効なコードを生成する制約があるため([MasterMemory公式
  README](https://github.com/Cysharp/MasterMemory)のco-location方針)。`InternalsVisibleTo`等で
  アセンブリを分割して回避することはしない(誤った使い方と判断し不採用)。生成されるクラス名は
  テーブル名PascalCase + `Data`サフィックス(例: `pachimon` → `PachimonData`)で統一し、
  いずれも`partial`にしてある(MasterMemoryのSource Generatorが自動生成する
  テーブルアクセサは`○○DataTable`という名前になる)。ゲームロジック側でこれらの型を直接
  ドメインモデルとして使うのではなく、`Atlas.BattleCore`の`ParticipantStats`/`MoveData`のような
  依存ゼロの型へマッピングして使う(下記実装状況・battle.md参照)
- 実データ(値)の読み込み方法は対象ごとに異なる:
  - Client: `Assets/Addressables/MasterData/masterdata.bytes`をAddressables経由でアプリ起動時に読み込む
  - Rust/Axum: 起動時にDBから全マスタを1回読み込み、`Arc<MasterData>`としてメモリに保持する
    (`Server/src/master/cache.rs`)。各リクエストハンドラはこのキャッシュを参照するだけ
  - バトルサーバー: Clientと同様に`masterdata.bytes`相当のバイナリを`BattleServer/`配下に
    直接配置し、起動時に読み込んでメモリに保持する。DBへの接続やRust内部APIへの問い合わせは
    行わない(Rustの「DBキャッシュ」方式より単純な「ファイル配置のみ」で済ませる)
- マスタ更新の反映は「`cargo run --bin seed_master_data`でJSON→DBへUPSERT → Rustサーバー再起動」
  (Rust)、「pipelineで生成→配置→再起動」(Client/バトルサーバー)で行う。無停止反映の仕組みは、
  1台構成の現状の規模では過剰と判断し採用しない
- Clientへのmasterdata配布をCDN(S3+CloudFront)経由にする案を検討中だが、バージョニング・
  差分検知等の詳細は現時点では対象外として保留する。バトルサーバー/Rustの読み込み方式には
  影響しない
- 実装状況(`pachimon`のみDB投入・キャッシュ済み、`moves`/`move_groups`/`move_group_moves`は
  ファイル生成のみでDB未投入 等)は[progress.md](../progress.md)を参照
