using MasterMemory;

namespace Atlas.MasterData
{
    public sealed class MasterDataStore
    {
        public MemoryDatabase Database { get; private set; }

        public void SetDatabase(MemoryDatabase database)
        {
            Database = database;
        }
    }
}
