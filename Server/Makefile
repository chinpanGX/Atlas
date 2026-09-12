.PHONY: up down ps logs mysql clean restart

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
#logs:
#	docker compose logs -f mysql

# MySQLに接続(root, パスワードなし)
mysql:
	docker exec -it atlas_mysql mysql -u root atlas_dev

# コンテナ停止 + データも完全削除
clean:
	docker compose down -v

# 再起動
restart: down up