# メモ: `cargo run --example` とフォルダ名の関係

## つまずいた内容

`cargo run --example setup_check` を実行したところ、以下のエラーが出た。

```
error: no example target named `setup_check` in default-run packages
```

原因は、`setup_check.rs` を `example/`(単数形)フォルダに置いていたこと。

## 整理

| 種類 | 綴り | 何を指すか |
|---|---|---|
| CLIオプション | `--example`(単数形) | `cargo run`コマンドの書き方そのもの。常にこの通り書く(固定) |
| フォルダ名 | `examples`(複数形) | Cargoがexample用ファイルを探しに行く場所(規約により複数形固定) |
| ファイル名 | `setup_check.rs` | `--example`の後ろに書く名前と一致させる(拡張子なし) |

- `--example`はコマンドオプションの綴り。変更不可。
- `examples/`はCargoが自動的に認識するディレクトリ名の規約。単数形にすると認識されない。
- 配置場所は`src/`の外、`Cargo.toml`と同じ階層。

## 正しい配置

```
Server/
├── Cargo.toml
├── src/
│   └── main.rs
└── examples/          ← 複数形、Cargo.tomlと同じ階層
    └── setup_check.rs
```

## 実行コマンド

```bash
cargo run --example setup_check
```
