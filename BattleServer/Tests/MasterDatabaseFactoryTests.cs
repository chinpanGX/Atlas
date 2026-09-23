using Atlas.BattleServer.Battle;
using Microsoft.Extensions.Configuration;

namespace Atlas.BattleServer.Tests
{
    // 配置済みのmasterdata.bytesが、同じく配置済みのMasterDataLoader(ContentHash)で復号できることの確認。
    // master-data-pipelineの`run.sh realtime`でLoaderとbytesの片方だけが更新された場合に失敗する。
    public class MasterDatabaseFactoryTests
    {
        [Fact]
        public void Load_DefaultPath_DecryptsDeployedBytes()
        {
            var configuration = new ConfigurationBuilder().Build();

            var database = MasterDatabaseFactory.Load(configuration);

            Assert.NotEmpty(database.PachimonDataTable.All);
            Assert.NotEmpty(database.MovesDataTable.All);
            Assert.NotEmpty(database.TypeChartDataTable.All);
        }

        [Fact]
        public void Load_MissingFile_Throws()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [MasterDatabaseFactory.PathConfigKey] = "MasterData/not-exists.bytes",
                })
                .Build();

            Assert.Throws<InvalidOperationException>(() => MasterDatabaseFactory.Load(configuration));
        }
    }
}
