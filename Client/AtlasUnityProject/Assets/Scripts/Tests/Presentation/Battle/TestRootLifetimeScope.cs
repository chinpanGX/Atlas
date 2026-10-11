using Atlas.Application;
using Atlas.DI;
using Atlas.Infrastructure.Mock;
using Grpc.Core;
using Supplement.Core;
using VContainer;

namespace Atlas.Tests.Presentation.Battle
{
    // Battle PlayModeテスト専用のComposition Root。APIサーバーへの通信(CallInvoker)を偽のサーバー(FakeServer)に、
    // 対戦(IBattleMatchmaker/IBattleConnectionFactory)をMockに差し替え、それ以外(MasterDatabaseLoader等)は
    // RootLifetimeScopeと同じにすることで、ローカルAPIサーバー無しでBootstrap→Home→Battleの実起動経路を検証する。
    // 開発者本人のローカルセーブデータ(アカウント情報等)を汚さないよう、保存先ディレクトリも
    // 専用の名前に切り替える。
    public sealed class TestRootLifetimeScope : RootLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterBuildCallback(resolver =>
            {
                resolver.Resolve<IFileStorageService>().SetDirectoryName("BattlePlayModeTestSaveData");
            });
        }

        protected override CallInvoker CreateCallInvoker()
        {
            return new FakeServer().CreateCallInvoker();
        }

        // BootstrapTestシーンのuseRealBattleServerの設定に関わらず、対戦も常にMockにする。
        protected override void ConfigureBattleConnections(IContainerBuilder builder)
        {
            builder.Register<IBattleMatchMaker, MockBattleMatchMaker>(Lifetime.Singleton);
            builder.Register<IBattleConnectionFactory, MockBattleConnectionFactory>(Lifetime.Singleton);
        }
    }
}
