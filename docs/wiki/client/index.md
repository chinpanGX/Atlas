---
title: "クライアント"
status: index
area: client
updated: 2026-10-18
---

# クライアント

Unity 6(`6000.6.5f1`、IL2CPP 前提)。`Client/AtlasUnityProject/`。コーディング規約は
[Client/AtlasUnityProject/CLAUDE.md](../../../Client/AtlasUnityProject/CLAUDE.md)。

## ページ

- [採用ライブラリ](libraries.md) — DI・非同期・画面遷移・通信・マスターデータのライブラリと導入経路
- [アセンブリとレイヤー](assemblies.md) — オニオンアーキテクチャと asmdef の依存
- [シーンとスコープ](scenes-and-scopes.md) — Bootstrap/Home/Battle の3シーンと、VContainer の3段のスコープ
- [画面の構成(View / Presenter)](screens.md) — 一方向の流れ、命名、フォルダ、画面一覧
- [画面遷移](navigation.md) — USN フォークの Push/Pop、遷移の直列化、シーンの切り替え
- [サービスと所持データ](services-and-player-data.md) — Service・Repository・`player_diff` の反映、マッチング
- [サインイン](sign-in.md) — タイトルからのサインインとアカウント情報の保存
- [バトル画面](battle-screen.md) — 選出・開始の繰り出し・状態の持ち方・演出・交代と投了
- [通信エラーのダイアログ](error-dialog.md) — 基盤のエラー処理と、Atlas が持つ見た目・遷移・文言
- [テストとサーバー無しでの動作](testing.md) — 偽のサーバー、Mock 対戦、PlayMode テスト
