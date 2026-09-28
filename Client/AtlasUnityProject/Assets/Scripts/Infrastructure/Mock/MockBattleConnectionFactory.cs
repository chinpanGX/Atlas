using Atlas.Application;
using Atlas.Domain;
using MasterMemory;

namespace Atlas.Infrastructure.Mock
{
    public sealed class MockBattleConnectionFactory : IBattleConnectionFactory
    {
        private readonly MemoryDatabase database;

        public MockBattleConnectionFactory(MemoryDatabase database)
        {
            this.database = database;
        }

        public IBattleConnection Create(BattleMatch match)
        {
            return new MockBattleConnection(database);
        }
    }
}
