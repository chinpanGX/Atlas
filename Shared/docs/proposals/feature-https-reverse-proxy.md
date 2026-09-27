# メモ: 将来構想 — HTTPS対応 (リバースプロキシ)

## 背景

現在の`axum`サーバーはHTTP通信のみに対応している。将来Unityクライアントが実装され、実際に通信するタイミングでHTTPS化が必要になる。

## 基本的な考え方

クライアントから見た通信は常にHTTPSのままにする。「HTTPS通信をどこで終端 (復号)するか」がサーバー側内部の設計の選択肢になる。

```
[Unityクライアント] --HTTPS--> [リバースプロキシ] --HTTP--> [axumサーバー]
                    (常にHTTPS)      (この区間だけHTTPにすることが多い)
```

## 実装方式の選択肢

**① リバースプロキシを手前に立てる (実務で主流)**

axumサーバー自体はHTTPのままにし、手前にCaddyやNginxを配置してHTTPSを終端させる。証明書更新をアプリサーバーと切り離せる、複数台構成でも証明書管理が1箇所で済む、などのメリットがある。

**おすすめ: Caddy**

設定ファイルを書くだけで自動的にHTTPS化してくれる。学習・個人開発規模ならこちらが手軽。

ローカル開発用 (自己署名証明書を自動生成):

```
# Caddyfile
localhost:3443 {
    reverse_proxy 127.0.0.1:3000
    tls internal
}
```

本番相当のドメインを取得した場合 (Let's Encryptの取得・自動更新も自動化):

```
# Caddyfile
api.example.com {
    reverse_proxy 127.0.0.1:3000
}
```

起動:

```bash
caddy run
```

docker-compose.ymlに組み込む場合、MySQLと同様にサービスとして追加できる。

**Nginx (参考)**

より細かい制御 (キャッシュ、レートリミットなど)が必要な場合の選択肢。設定はCaddyより複雑になり、証明書は`mkcert`
などで別途用意する必要がある。

**② `axum-server`クレートで直接TLS対応する**

`axum`自体はTLSを持たないため、`axum-server`(`tls-rustls`機能)を使うと証明書ファイルを指定するだけで直接HTTPS対応できる。

```toml
axum-server = { version = "0.6", features = ["tls-rustls"] }
```

`tokio::net::TcpListener` + `axum::serve`ではなく、`axum_server::bind_rustls`に置き換える形になる。ローカル用の自己署名証明書は
`mkcert`で用意できる。

## 結論・方針

個人学習用途・将来的な拡張性を考えると、Caddyでのリバースプロキシ方式を採用する方針。設定量が少なく証明書管理も自動化されるため、Unityクライアント実装のタイミングで着手する。