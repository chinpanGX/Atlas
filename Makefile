.PHONY: dev db server battle bot seed stop

# BattleServerはnet10.0。Git BashのPATHではUnity付属の.NET 8 SDKが先に見つかるため、.NET 10 SDKを明示する
# (別の場所にある場合は `make dev DOTNET=<dotnetのパス>` で上書き)
DOTNET ?= C:/Program Files/dotnet/dotnet.exe

# MySQL起動+マイグレーション → APIサーバーとBattleServerを並列起動(Ctrl+Cで両方停止)
dev: db
	@$(MAKE) --no-print-directory -j2 server battle

# MySQLコンテナ起動 + DB起動待ち + マイグレーション(適用済みなら何もしない)
db:
	@$(MAKE) --no-print-directory -C Server setup

# APIサーバー(http://127.0.0.1:3000)
server:
	cd Server && cargo run

# Server/.envから値を1つ取り出す(CRLFの.envでも値に\rが混ざらないようにする)
env_value = $$(grep '^$(1)=' Server/.env | cut -d= -f2- | tr -d '\r')

# BattleServer(http://127.0.0.1:5000)
# 共有シークレットはServer/.envの値を環境変数で渡す(user-secretsより優先され、Serverと必ず一致する)
battle:
	BATTLE_TOKEN_SECRET="$(call env_value,BATTLE_TOKEN_SECRET)" \
	INTERNAL_API_SECRET="$(call env_value,INTERNAL_API_SECRET)" \
	"$(DOTNET)" run --project BattleServer/BattleServer.csproj

# 対戦相手ボット(Server・BattleServer起動後に別ターミナルで実行)
bot:
	cd BattleServer && "$(DOTNET)" run --project BattleBot -- --loop

# マスタデータをDBへ投入(投入後はServerを再起動する)
seed:
	cd Server && cargo run --bin seed_master_data

# Ctrl+Cで止まりきらなかった場合に、APIサーバー・BattleServerのプロセスを強制終了する
stop:
	@powershell -NoProfile -Command "Stop-Process -Name Server,BattleServer -Force -ErrorAction SilentlyContinue"
