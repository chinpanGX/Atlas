using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Domain;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Api;
using Atlas.Infrastructure.Mock;
using Atlas.Infrastructure.Realtime;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using Supplement.Core;
using Supplement.ZeroMessenger;
using UnityEngine;
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

        // オフで対戦をMock(MockBattleMatchmaker/MockBattleConnection、サーバー不要)、オンでAPIサーバーの
        // マッチング+BattleServer(MagicOnion)への実接続にする。BootstrapシーンのRootLifetimeScopeで切り替える。
        [SerializeField] private bool useBattleServer = true;

        protected override void Configure(IContainerBuilder builder)
        {
            // ApiRequestは生成コードの静的ヘルパーでDIを通らないため、ロガーはここで直接差し込む。
            // リリースビルドでは通信内容をログに残さない。
            // (Debug.isDebugBuildはEditorと開発ビルドでtrue)
            ApiRequest.Logger = UnityEngine.Debug.isDebugBuild ? new UnityApiRequestLogger() : null;
            builder.RegisterAddressablesLoader();
            builder.RegisterEncryptedFileStorage();
            if (SaveDataDirectory.Resolve() is { } saveDataDirectory)
            {
                builder.RegisterBuildCallback(resolver =>
                    resolver.Resolve<IFileStorageService>().SetDirectoryName(saveDataDirectory));
            }
            builder.Register<IMessageBroker, GlobalMessageBroker>(Lifetime.Singleton);
            builder.Register<IMasterDataService, MasterDataService>(Lifetime.Singleton);
            builder.Register<ISceneNavigator, SceneNavigator>(Lifetime.Singleton);
            builder.Register<BattleEntryStore>(Lifetime.Singleton);

            builder.Register<AccessTokenStore>(Lifetime.Singleton);
            builder.Register<AccessTokenRefresher>(Lifetime.Singleton);
            builder.Register<IDeviceCredentialsRepository, DeviceCredentialsRepository>(Lifetime.Singleton);
            builder.Register<IPlayerProfileRepository, ApiPlayerProfileRepository>(Lifetime.Singleton);
            builder.Register<IItemRepository, ApiItemRepository>(Lifetime.Singleton);
            builder.Register<ItemDiffApplier>(Lifetime.Singleton);
            builder.Register<IPachimonRepository, ApiPachimonRepository>(Lifetime.Singleton);
            builder.Register<PachimonDiffApplier>(Lifetime.Singleton);
            builder.Register<IPachimonMoveMapRepository, ApiPachimonMoveMapRepository>(Lifetime.Singleton);
            builder.Register<PachimonMoveMapDiffApplier>(Lifetime.Singleton);
            builder.Register<IPartyRepository, ApiPartyRepository>(Lifetime.Singleton);
            builder.Register<PartyDiffApplier>(Lifetime.Singleton);
            builder.Register<IPlayerDiffApplier, PlayerDiffApplier>(Lifetime.Singleton);
            ConfigureAuthConnections(builder);
            builder.Register<IPlayerAccountService, PlayerAccountService>(Lifetime.Singleton);
            builder.Register<IItemFetchService, ItemFetchService>(Lifetime.Singleton);
            builder.Register<IPartyService, PartyService>(Lifetime.Singleton);
            builder.Register<IPachimonService, PachimonService>(Lifetime.Singleton);
            builder.Register<IPachimonMoveMappingService, PachimonMoveMappingService>(Lifetime.Singleton);
            builder.Register<ISignInService, SignInService>(Lifetime.Singleton);
            builder.Register<IScoutConnection>(
                resolver => new ScoutConnection(
                    ApiBaseUrl,
                    resolver.Resolve<AccessTokenStore>(),
                    resolver.Resolve<AccessTokenRefresher>(),
                    resolver.Resolve<IPlayerDiffApplier>()),
                Lifetime.Singleton);
            builder.Register<IDebugConnection>(
                resolver => new DebugConnection(
                    ApiBaseUrl,
                    resolver.Resolve<AccessTokenStore>(),
                    resolver.Resolve<AccessTokenRefresher>(),
                    resolver.Resolve<IPlayerDiffApplier>()),
                Lifetime.Singleton);

            ConfigureBattleConnections(builder);
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }

        // 対戦のマッチングと接続(IBattleConnectionはBattleシーンのスコープがIBattleConnectionFactoryで都度生成する)。
        // Battle PlayModeテスト(TestRootLifetimeScope)はuseRealBattleServerに関わらず常にMockにする。
        protected virtual void ConfigureBattleConnections(IContainerBuilder builder)
        {
            if (useBattleServer)
            {
                builder.Register<IBattleMatchmaker>(
                    resolver => new ApiBattleMatchmaker(
                        ApiBaseUrl,
                        resolver.Resolve<AccessTokenStore>(),
                        resolver.Resolve<AccessTokenRefresher>(),
                        resolver.Resolve<IPartyService>()),
                    Lifetime.Singleton);
                builder.Register<IBattleConnectionFactory, RealtimeBattleConnectionFactory>(Lifetime.Singleton);
            }
            else
            {
                builder.Register<IBattleMatchmaker, MockBattleMatchmaker>(Lifetime.Singleton);
                builder.Register<IBattleConnectionFactory, MockBattleConnectionFactory>(Lifetime.Singleton);
            }
        }

        // IDeviceConnection/IPlayerConnectionの登録だけを差し替え可能にする。本番は常にApi実装
        // (実通信)。Battle PlayModeテスト(TestRootLifetimeScope)はここをMockに差し替え、
        // ローカルAPIサーバー無しでBootstrap→Home→Battleの起動経路を検証する。
        protected virtual void ConfigureAuthConnections(IContainerBuilder builder)
        {
            builder.Register<IDeviceConnection>(_ => new DeviceConnection(ApiBaseUrl), Lifetime.Singleton);
            builder.Register<IPlayerConnection>(
                resolver => new PlayerConnection(
                    ApiBaseUrl,
                    resolver.Resolve<AccessTokenStore>(),
                    resolver.Resolve<AccessTokenRefresher>(),
                    resolver.Resolve<IPlayerDiffApplier>()),
                Lifetime.Singleton);
        }
    }

    internal sealed class BootstrapEntryPoint : IAsyncStartable
    {
        private readonly ISceneNavigator sceneNavigator;
        private readonly IMasterDataService masterDataService;
        private readonly ISignInService signInService;

        public BootstrapEntryPoint(ISceneNavigator sceneNavigator, IMasterDataService masterDataService, ISignInService signInService)
        {
            this.sceneNavigator = sceneNavigator;
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
            await sceneNavigator.ChangeSceneAsync(AddressDefinition.Home, cancellation);
        }
    }
}
