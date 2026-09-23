# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

Atlasは、クライアント(Unity)とサーバー(Rust)を組み合わせた、デバイス認証・チャット・スカウト・
対戦マッチング・リアルタイムバトル(C#/MagicOnion)を持つオンラインゲームバックエンドのプロジェクト。
デバイス認証(ゲスト型)で始める、リアルタイム対戦モンスター収集ゲーム。詳細は [README.md](README.md) を参照。

### システム構成

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

MySQLが唯一のデータストア。書き込みはRust/Axumサーバー経由に統一されており、
C#/MagicOnionサーバー(リアルタイム対戦)はRust側への内部APIでバトル結果を報告する。

## リポジトリ構成

```
Atlas/
├── Client/                 # Unityクライアント(これから実装、まだ画面/通信/ゲームロジック未着手)
├── Server/                 # Rustバックエンド(REST API、実装中) — 詳細は Server/CLAUDE.md 参照
├── BattleServer/           # C#/MagicOnionのリアルタイム対戦サーバー(Atlas.BattleCoreをDLLとして参照)
├── Shared/                 # クライアント/サーバー間の共有定義(マスターデータスキーマ、設計書)
├── master-data-pipeline/   # マスターデータ生成パイプライン(サブモジュール、自作)
├── api-codegen/            # OpenAPI仕様書→Unity向けDTO・通信APIクライアント生成ツール(サブモジュール、自作)
└── Supplement/              # Unity共通ユーティリティパッケージ(サブモジュール、自作)
```

`master-data-pipeline` / `api-codegen` / `Supplement` はgit submodule([.gitmodules](.gitmodules))。

ローカル環境の設定(Server/BattleServerで共有するシークレット等)・起動順・よく使うコマンドは
[DEVELOPMENT.md](DEVELOPMENT.md)にまとめている。

## 設計書

- [Shared/docs/design/architecture.md](Shared/docs/design/architecture.md) — 全体構成・命名規則・マスターデータ設計などの横断的な内容
- [Shared/docs/design/client-architecture.md](Shared/docs/design/client-architecture.md) — Unity Client側の画面遷移・DI・Mock/Real切り替えの枠組み
- [Shared/docs/design/outgame.md](Shared/docs/design/outgame.md) / [scout.md](Shared/docs/design/scout.md) / [battle.md](Shared/docs/design/battle.md) — 機能別設計書
- `Server/docs/notes/design.md` — サーバー全体構成(MagicOnion含む)
- `Server/docs/notes/api-design.md` — REST API仕様・DB設計

## 設計上のポイント

- **デバイス認証**: 会員登録なしのゲスト型認証。`secret_key`はArgon2でハッシュ化して保存し、1つの`device_id`につき有効なアクセストークンは常に1つ(再認証時は上書き)
- **マスターデータ管理**: 正はMySQL。`master-data-pipeline`(スプレッドシート→CI→DB seed)で投入し、APIサーバーは起動時にDBから全マスタを読み込んでメモリキャッシュする方針(常時ポーリングでの無停止反映は現時点で導入しない判断)
- **コード生成ツールの自作**: `master-data-pipeline`と`api-codegen`は、既存OSS(`api-codegen`なら`openapi-generator`/`NSwag`)を検討した上で自作した、特定プロジェクトに依存しない汎用ツール

## 作業時の注意

- Serverディレクトリで作業する場合は [Server/CLAUDE.md](Server/CLAUDE.md) を参照
- マスターデータのスキーマ/CSVを変更したら `master-data-pipeline` スキルで生成物を再配置する
- Server側のAPI(handler/DTO)を追加・変更したら `api-codegen` スキルでUnity向け型を再生成する
- 機能実装・API変更・マスターデータ追加を行った後は `atlas-design-docs-sync` スキルでどの設計書を更新すべきか確認する
