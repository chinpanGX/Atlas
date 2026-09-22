using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Domain;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Api;
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
    public class RootLifetimeScope : LifetimeScope
    {
        // Server/.envのSERVER_ADDR(開発用固定値)。環境切り替え(本番/ステージング等)の
        // 仕組みは未導入のためハードコードしている。
        private const string ApiBaseUrl = "http://127.0.0.1:3000";

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterAddressablesLoader();
            builder.RegisterEncryptedFileStorage();
            builder.Register<IMessageBroker, GlobalMessageBroker>(Lifetime.Singleton);
            builder.Register<IMasterDataService, MasterDataService>(Lifetime.Singleton);

            builder.Register<AccessTokenStore>(Lifetime.Singleton);
            builder.Register<IDeviceCredentialsRepository, DeviceCredentialsRepository>(Lifetime.Singleton);
            builder.Register<IPlayerProfileRepository, ApiPlayerProfileRepository>(Lifetime.Singleton);
            builder.Register<IItemRepository, ApiItemRepository>(Lifetime.Singleton);
            builder.Register<ItemDiffApplier>(Lifetime.Singleton);
            builder.Register<IPlayerDiffApplier, PlayerDiffApplier>(Lifetime.Singleton);
            ConfigureAuthConnections(builder);
            builder.Register<IPlayerAccountService, PlayerAccountService>(Lifetime.Singleton);
            builder.Register<ISignInService, SignInService>(Lifetime.Singleton);

            // MasterDataServiceのDatabaseはBootstrapEntryPoint.StartAsync内のLoadAsync完了後に
            // 確定するが、IBattleConnectionはBattle画面へ遷移するまで実際には解決されない
            // (Lifetime.Singletonの遅延生成)ため、ここでは問題ない。
            builder.Register<IBattleConnection>(
                resolver => new MockBattleConnection(resolver.Resolve<IMasterDataService>().Database),
                Lifetime.Singleton);
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }

        // IDeviceConnection/IPlayerConnectionの登録だけを差し替え可能にする。本番は常にApi実装
        // (実通信)。Battle PlayModeテスト(TestRootLifetimeScope)はここをMockに差し替え、
        // ローカルAPIサーバー無しでBootstrap→Home→Battleの起動経路を検証する。
        protected virtual void ConfigureAuthConnections(IContainerBuilder builder)
        {
            builder.Register<IDeviceConnection>(_ => new DeviceConnection(ApiBaseUrl), Lifetime.Singleton);
            builder.Register<IPlayerConnection>(
                resolver => new PlayerConnection(
                    ApiBaseUrl, resolver.Resolve<AccessTokenStore>(), resolver.Resolve<IPlayerDiffApplier>()),
                Lifetime.Singleton);
        }
    }

    internal sealed class BootstrapEntryPoint : IAsyncStartable
    {
        private readonly ISceneLoader sceneLoader;
        private readonly IMasterDataService masterDataService;
        private readonly ISignInService signInService;

        public BootstrapEntryPoint(ISceneLoader sceneLoader, IMasterDataService masterDataService, ISignInService signInService)
        {
            this.sceneLoader = sceneLoader;
            this.masterDataService = masterDataService;
            this.signInService = signInService;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await masterDataService.LoadAsync();
            await signInService.SignInAsync();

            // HomeLifetimeScope.FindParentが直接RootLifetimeScopeを探すため、ここで
            // LifetimeScope.EnqueueParentを使う必要は無い(むしろHomeシーンの
            // HomeEntryPointが同期的に開始するPushPageAsync<HomePage>のネストした
            // EnqueueParentと競合し、シーン切り替え完了(ChangeSceneのawait解除)の
            // タイミングでスタックを壊す不具合があったため使わない)。
            await sceneLoader.ChangeScene(AddressDefinition.Home, true, cancellation);
        }
    }
}
