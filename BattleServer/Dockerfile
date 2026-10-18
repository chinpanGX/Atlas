# syntax=docker/dockerfile:1
# バトルサーバー(.NET / MagicOnion)のイメージ。リポジトリのルートを起点にビルドする
# (Shared/BattleCore・Shared/BattleContracts のソースと、内部APIの .proto(Shared/proto)を使うため)。
#   docker build -f BattleServer/Dockerfile -t atlas-battle .   (make images・make dev も同じことをする)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Shared Shared
COPY BattleServer BattleServer
# NuGet のパッケージをキャッシュに残し、2回目からはダウンロードし直さない
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish BattleServer/BattleServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# コンテナの外から届くよう、すべてのアドレスで待ち受ける
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "BattleServer.dll"]
