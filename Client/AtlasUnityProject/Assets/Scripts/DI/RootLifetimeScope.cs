using System.Threading;
using Atlas.Application;
using Atlas.Domain;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Mock;
using Cysharp.Threading.Tasks;
using Supplement.Core;
using Supplement.Loader.Abstractions;
using Supplement.ZeroMessenger;
using VContainer;
using VContainer.Unity;

namespace Atlas.DI
{
    /// <summary>
    /// Bootstrapシーンの常駐スコープ。design/client-architecture.md「シーン構成」参照。
    /// アプリ生存期間中ずっと存在し、Home/BattleシーンのLifetimeScopeの親になる。
    /// 各IXxxRepositoryのMock/Real登録、各IXxxServiceの登録もここで行う構成ルート
    /// (Composition Root)。Atlas.Presentationとは別アセンブリに分離し、Presenter自身は
    /// Atlas.Infrastructureを直接参照しない。
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterAddressablesLoader();
            builder.Register<IMessageBroker, GlobalMessageBroker>(Lifetime.Singleton);
            builder.Register<IMasterDataService, MasterDataService>(Lifetime.Singleton);
            builder.Register<IPlayerRepository, MockPlayerRepository>(Lifetime.Singleton);
            builder.Register<IPlayerService, PlayerService>(Lifetime.Singleton);
            // MasterDataServiceのDatabaseはBootstrapEntryPoint.StartAsync内のLoadAsync完了後に
            // 確定するが、IBattleConnectionはBattle画面へ遷移するまで実際には解決されない
            // (Lifetime.Singletonの遅延生成)ため、ここでは問題ない。
            builder.Register<IBattleConnection>(
                resolver => new MockBattleConnection(resolver.Resolve<IMasterDataService>().Database),
                Lifetime.Singleton);
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }
    }

    internal sealed class BootstrapEntryPoint : IAsyncStartable
    {
        private readonly ISceneLoader sceneLoader;
        private readonly IMasterDataService masterDataService;

        public BootstrapEntryPoint(ISceneLoader sceneLoader, IMasterDataService masterDataService)
        {
            this.sceneLoader = sceneLoader;
            this.masterDataService = masterDataService;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await masterDataService.LoadAsync();

            // HomeLifetimeScope.FindParentが直接RootLifetimeScopeを探すため、ここで
            // LifetimeScope.EnqueueParentを使う必要は無い(むしろHomeシーンの
            // HomeEntryPointが同期的に開始するPushPageAsync<HomePage>のネストした
            // EnqueueParentと競合し、シーン切り替え完了(ChangeSceneのawait解除)の
            // タイミングでスタックを壊す不具合があったため使わない)。
            await sceneLoader.ChangeScene("Home", true, cancellation);
        }
    }
}
