---
title: "開発用API"
status: current
area: server
related: [battle-server/testing-and-bot, features/index]
proto: [Shared/proto/atlas/v1/debug.proto]
code: [Server/src/grpc/debug.rs, Server/src/main.rs, Server/src/service/player_pachimon_service.rs]
updated: 2026-10-18
---

# 開発用API

正式なゲーム内機能ではなく、動作確認のためのもの(`DebugService`)。

- 環境変数 `ENABLE_DEBUG_API` が `true`・`1` のときだけ登録する。それ以外の値なら登録しない。未設定ならデバッグビルドだけ登録する
  (`make dev` でそのまま使えるように)
- 登録しなければ、呼ぶと `UNIMPLEMENTED` になる
- クライアント向けの API と同じく、認証と冪等性の対象になる

## GrantGems

ジェム450(スカウト3回分)を付与する。回数の上限は無い。スカウトの動作確認を、ジェム切れせずに繰り返すため。
Unity のショップ画面の開発用のジェム付与から呼ぶ(開発ビルドとEditorだけ表示)。

## RandomizeParty

- マスタから重複なしで6体を選んで新しく付与し、技は技グループの候補から重複なしでランダムに最大4つ選ぶ
- 既存の編成をすべて解除し、付与した6体で枠1〜6を編成し直す。以前の所持個体は残る
- 自動対戦のクライアント(`BattleBot`)が毎回違うパーティで対戦するために使う(サーバーのボットのパーティを作るときも同じ処理を使う)
