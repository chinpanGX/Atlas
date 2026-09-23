using Atlas.Application;
using Atlas.DI;
using Atlas.Infrastructure.Mock;
using Supplement.Core;
using VContainer;

namespace Atlas.Tests.Presentation.Battle
{
    // Battle PlayModeテスト専用のComposition Root。IDeviceConnection/IPlayerConnectionと対戦
    // (IBattleMatchmaker/IBattleConnectionFactory)だけMockに差し替え、それ以外(MasterDataService等)は
    // RootLifetimeScopeと同じにすることで、ローカルAPIサーバー無しでBootstrap→Home→Battleの実起動経路を検証する。
    // 開発者本人のローカルセーブデータ(deviceCredentials等)を汚さないよう、保存先ディレクトリも
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

        protected override void ConfigureAuthConnections(IContainerBuilder builder)
        {
            builder.Register<IDeviceConnection, MockDeviceConnection>(Lifetime.Singleton);
            builder.Register<IPlayerConnection, MockPlayerConnection>(Lifetime.Singleton);
        }

        // BootstrapTestシーンのuseRealBattleServerの設定に関わらず、対戦も常にMockにする。
        protected override void ConfigureBattleConnections(IContainerBuilder builder)
        {
            builder.Register<IBattleMatchmaker, MockBattleMatchmaker>(Lifetime.Singleton);
            builder.Register<IBattleConnectionFactory, MockBattleConnectionFactory>(Lifetime.Singleton);
        }
    }
}
