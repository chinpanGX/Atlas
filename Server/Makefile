.PHONY: up down ps logs mysql clean restart wait-db migrate migrate-revert setup db-reset fmt fmt-check

# rustfmtはcrateルート(lib.rs/各bin)から`mod`宣言を辿って対象ファイルを見つけるため、
# 個別の.rsファイルを列挙する必要はない(むしろ列挙すると同じファイルが複数回チェックされ、
# 出力が重複する)。src/master/generated/への再帰は、宣言側(src/master/mod.rs)の
# #[rustfmt::skip]で止めている(rustfmt.tomlの`ignore`はstableでは無効なため)。
FMT_ROOTS := src/lib.rs src/main.rs $(wildcard src/bin/*.rs)

fmt:
	@rustfmt --edition 2024 $(FMT_ROOTS)

fmt-check:
	@rustfmt --edition 2024 --check $(FMT_ROOTS)

# MySQLコンテナを起動
up:
	docker compose up -d

# MySQLコンテナを停止(データは保持)
down:
	docker compose down

# コンテナの状態を確認
ps:
	docker compose ps

# MySQLコンテナのログを表示
logs:
	docker compose logs -f mysql

# MySQLに接続(root, パスワードなし)
mysql:
	docker exec -it atlas_mysql mysql -u root atlas_dev

# コンテナ停止 + データも完全削除
clean:
	docker compose down -v

# 再起動
restart: down up

# MySQLが接続可能になるまで待機(起動直後はまだ接続を受け付けないため)
# -h 127.0.0.1 を明示してTCP経由でpingする(migrateもTCPで接続するため)。
# -hを省略するとUnixソケット経由になり、ボリューム初期化直後の一時サーバー
# (初期化スクリプト専用、port: 0でTCP未listen)にも"ready"と誤判定してしまい、
# 本サーバーがTCPをlistenする前にmigrateが接続してしまうことがある
# (sqlxが"expected to read 4 bytes, got 0 bytes at EOF"で失敗する原因)。
wait-db:
	@echo "Waiting for MySQL to be ready..."
	@until docker exec atlas_mysql mysqladmin ping -h 127.0.0.1 -u root --silent; do \
		sleep 1; \
	done
	@echo "MySQL is ready."

# マイグレーションを実行(DATABASE_URLは.envから読み込まれる)
migrate:
	sqlx migrate run

# 直近のマイグレーションを1つ取り消す
migrate-revert:
	sqlx migrate revert

# コンテナ起動 + DB起動待ち + マイグレーション実行(初回セットアップ・クローン後に使う)
setup: up wait-db migrate

# コンテナ停止+データ完全削除 -> 再起動 -> マイグレーションを最初から実行(DBを空の状態から作り直す)
db-reset: clean up wait-db migrate