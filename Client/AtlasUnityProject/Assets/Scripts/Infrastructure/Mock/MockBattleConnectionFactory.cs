using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure.Mock
{
    public sealed class MockBattleConnectionFactory : IBattleConnectionFactory
    {
        private readonly IMasterDataService masterDataService;

        public MockBattleConnectionFactory(IMasterDataService masterDataService)
        {
            this.masterDataService = masterDataService;
        }

        public IBattleConnection Create(BattleMatch match)
        {
            return new MockBattleConnection(masterDataService.Database);
        }
    }
}
