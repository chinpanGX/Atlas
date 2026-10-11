.PHONY: dev db server battle bot seed stop proto-gen errors-gen

# BattleServerはnet10.0。Git BashのPATHではUnity付属の.NET 8 SDKが先に見つかるため、.NET 10 SDKを明示する
# (別の場所にある場合は `make dev DOTNET=<dotnetのパス>` で上書き)
DOTNET ?= C:/Program Files/dotnet/dotnet.exe

# gRPC基盤(VolTech gRPC)のタグ。Client/AtlasUnityProject/Packages/manifest.json・Server/Cargo.toml のタグと同じにする
VOLTECH_TAG := v0.2.0
VOLTECH_REPO := https://github.com/chinpanGX/VolTech-gRPC
VOLTECH_ROOT := .tools/voltech-grpc-src/$(VOLTECH_TAG)
ATLAS_ERRORS_CS := Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Grpc/Generated/Errors/AtlasErrors.cs

# 生成のツールの準備(buf・protoc・protocプラグイン)は基盤のmk/codegen.mkにある。
# 読み込むと基盤を取ってくるので、生成するときだけ読み込む(make dev などでは取ってこない)
ifneq ($(filter proto-gen errors-gen,$(MAKECMDGOALS)),)
include $(VOLTECH_ROOT)/mk/codegen.mk
endif

# 基盤が無ければ、生成に使うフォルダだけを取ってくる(sandboxのUnityプロジェクトはパスが長くWindowsで展開できないことがあるため取らない)
$(VOLTECH_ROOT)/mk/codegen.mk:
	git clone --quiet --depth 1 --filter=blob:none --sparse --branch $(VOLTECH_TAG) $(VOLTECH_REPO) $(VOLTECH_ROOT)
	git -C $(VOLTECH_ROOT) sparse-checkout set mk scripts tools/protoc-gen-voltech-grpc

# Shared/proto の .proto から、Unity向けのC#(メッセージ型・gRPCクライアント・ラッパー)を生成する。
# バトルサーバー向けの内部API(atlas/internal)はクライアントから呼ばないので、atlas/v1 だけを生成する
proto-gen: voltech-tools
	$(PROTO_ENV) buf lint
	$(call voltech_proto_gen,--path Shared/proto/atlas/v1)

# Shared/errors.toml の業務エラーから、Unity向けのC#のエラーコードを生成する
errors-gen: voltech-tools
	$(call voltech_errors_gen,Shared/errors.toml,$(ATLAS_ERRORS_CS),Atlas.Api.V1,AtlasErrors)

# MySQL起動+マイグレーション → APIサーバーとBattleServerを並列起動(Ctrl+Cで両方停止)
dev: db
	@$(MAKE) --no-print-directory -j2 server battle

# MySQLコンテナ起動 + DB起動待ち + マイグレーション(適用済みなら何もしない)
db:
	@$(MAKE) --no-print-directory -C Server setup

# APIサーバー(gRPC http://127.0.0.1:50051 / 内部API http://127.0.0.1:3000)
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
