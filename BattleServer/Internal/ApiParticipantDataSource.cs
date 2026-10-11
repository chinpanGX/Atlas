using Atlas.BattleCore;
using Atlas.BattleServer.Battle;
using Atlas.Internal.V1;
using Atlas.MasterData;
using Grpc.Core;
using PartyPachimon = Atlas.BattleContracts.PartyPachimon;

namespace Atlas.BattleServer.Internal
{
    // 選出候補(パーティ編成)と、選出個体の所持データ(どのパチモンか・努力値・覚えている技)をRustの内部API
    // (BattleInternalService)から取得し、後者はマスタ(MemoryDatabase)と組み合わせてBattleCore用の型にする。
    // 所持チェックもRust側で行う(他プレイヤーの個体・存在しないIDはNOT_FOUND)。
    public sealed class ApiParticipantDataSource : IParticipantDataSource
    {
        private readonly BattleInternalService.BattleInternalServiceClient client;
        private readonly MemoryDatabase database;
        private readonly Metadata headers;

        public ITypeChart TypeChart { get; }

        public ApiParticipantDataSource(
            BattleInternalService.BattleInternalServiceClient client, MemoryDatabase database, IConfiguration configuration)
        {
            this.client = client;
            this.database = database;
            // 結果報告(BattleResultReporter)は未設定なら送信を諦めるだけだが、こちらは無いと対戦を
            // 始められないため、未設定なら起動時に失敗させる(Program.csで起動時に解決している)。
            var secret = configuration[BattleResultReporter.SecretConfigKey] is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{BattleResultReporter.SecretConfigKey} is not set");
            headers = new Metadata { { BattleResultReporter.SecretHeaderName, secret } };
            TypeChart = LoadoutBuilder.BuildTypeChart(database);
        }

        public async Task<IReadOnlyList<PartyPachimon>> GetPartyAsync(string playerId)
        {
            var response = await client.GetPartyAsync(new GetPartyRequest { PlayerId = playerId }, headers);
            return response.Pachimon
                .Select(p => new PartyPachimon(p.PlayerPachimonId, p.PachimonId))
                .ToList();
        }

        public async Task<IReadOnlyList<ParticipantLoadout>?> ResolveAsync(string playerId, IReadOnlyList<string> playerPachimonIds)
        {
            GetLoadoutsResponse response;
            try
            {
                response = await client.GetLoadoutsAsync(
                    new GetLoadoutsRequest { PlayerId = playerId, PlayerPachimonIds = { playerPachimonIds } }, headers);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
            {
                return null;
            }

            if (response.Loadouts.Count != playerPachimonIds.Count)
            {
                throw new InvalidOperationException(
                    $"GetLoadouts returned {response.Loadouts.Count} pachimon for {playerPachimonIds.Count} ids");
            }

            return response.Loadouts
                .Select(p => LoadoutBuilder.Build(database, new OwnedPachimon(p.PachimonId, ToEffortValues(p.EffortValues), p.MoveIds)))
                .ToList();
        }

        private static Battle.EffortValues ToEffortValues(Atlas.Internal.V1.EffortValues values)
        {
            return new Battle.EffortValues(values.Hp, values.Atk, values.Def, values.Spatk, values.Spdef, values.Speed);
        }
    }
}
