using System;
using System.Threading;
using Atlas.Api.V1;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.DI.Boot;
using Atlas.Domain;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Grpc;
using Atlas.Infrastructure.Mock;
using Atlas.Infrastructure.Realtime;
using Atlas.Navigation;
using Atlas.Presentation.Common;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Supplement.Core;
using Supplement.Unity;
using Supplement.ZeroMessenger;
using UnityEngine;
using UnityScreenNavigator;
using VContainer;
using VContainer.Unity;
using VolTech.Client;
using VolTech.Client.Auth;
using VolTech.Client.Unity;

namespace Atlas.DI
{
    /// <summary>
    /// Bootstrapシーンの常駐スコープ。Bootstrapシーンは一度もアンロードしないため、
    /// アプリ生存期間中ずっと存在し、Home/BattleシーンのLifetimeScopeの親になる。
    /// 各IXxxConnectionのMock/Real登録、各IXxxRepository/IXxxServiceの登録もここで行う構成ルート
    /// (Composition Root)。Atlas.Presentationとは別アセンブリに分離し、Presenter自身は
    /// Atlas.Infrastructureを直接参照しない。
    /// </summary>
    public class RootLifetimeScope : LifetimeScope
    {
        // APIサーバーのgRPCの待ち受け先(Server/.envのGRPC_ADDRの既定値)。環境切り替え(本番/ステージング等)の
        // 仕組みは未導入のためハードコードしている。
        private const string GrpcAddress = "http://127.0.0.1:50051";

        // リリースのたびに増やす整数のバージョン。サーバーはこれで強制アップデートを判定する
        // (エディタからの通信は判定しない)。
        private const int AppVersion = 1;

        // オフで対戦をMock(MockBattleMatchmaker/MockBattleConnection、サーバー不要)、オンでAPIサーバーの
        // マッチング+BattleServer(MagicOnion)への実接続にする。BootstrapシーンのRootLifetimeScopeで切り替える。
        [SerializeField] private bool useBattleServer = true;

        // 通信エラーのダイアログ。シーンの切り替え中でも出せるよう、Bootstrapシーンに置く。
        [SerializeField] private ErrorDialog errorDialog;

        private GrpcChannel channel;
        private ServerClockResetter clockResetter;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<Supplement.Loader.AddressablesLoader.AddressablesAssetLoader>(Lifetime.Singleton)
                .As<Supplement.Loader.Abstractions.IAssetLoader, Supplement.Loader.Abstractions.ISceneLoader>();
            builder.RegisterEncryptedFileStorage();
            if (SaveDataDirectory.Resolve() is { } saveDataDirectory)
            {
                builder.RegisterBuildCallback(resolver =>
                    resolver.Resolve<IFileStorageService>().SetDirectoryName(saveDataDirectory));
            }
            builder.Register<IMessageBroker, GlobalMessageBroker>(Lifetime.Singleton);
            builder.Register<MasterDatabaseLoader>(Lifetime.Singleton);
            // マスタは起動時の初期化(MasterDataInitializationTask)で読み込むため、Build時には登録できない。
            // 最初に要求されたときに読み込み済みのものを返す。Home・Battleの画面は初期化の後に作られるので、
            // コンストラクタでMemoryDatabaseを受け取れる。初期化より前に要求したら例外にして気づけるようにする。
            builder.Register(resolver => resolver.Resolve<MasterDatabaseLoader>().Database
                                         ?? throw new InvalidOperationException(
                                             "MemoryDatabase was requested before the master data was loaded."),
                Lifetime.Singleton);
            builder.Register<SceneNavigator>(Lifetime.Singleton);
            builder.Register<ISceneNavigator, TransitionAwareSceneNavigator>(Lifetime.Singleton);
            builder.Register<BattleEntryStore>(Lifetime.Singleton);

            builder.Register<IPlayerProfileRepository, PlayerProfileRepository>(Lifetime.Singleton);
            builder.Register<IItemRepository, ItemRepository>(Lifetime.Singleton);
            builder.Register<IPachimonRepository, PachimonRepository>(Lifetime.Singleton);
            builder.Register<IPachimonMoveMapRepository, PachimonMoveMapRepository>(Lifetime.Singleton);
            builder.Register<IPartyRepository, PartyRepository>(Lifetime.Singleton);
            builder.Register<PlayerSnapshotApplier>(Lifetime.Singleton);
            builder.Register<ItemDiffApplier>(Lifetime.Singleton);
            builder.Register<PachimonDiffApplier>(Lifetime.Singleton);
            builder.Register<PachimonMoveMapDiffApplier>(Lifetime.Singleton);
            builder.Register<PartyDiffApplier>(Lifetime.Singleton);
            builder.Register<PlayerDiffApplier>(Lifetime.Singleton);

            ConfigureGrpc(builder);
            builder.Register<IPlayerConnection, PlayerConnection>(Lifetime.Singleton);
            builder.Register<IPartyConnection, PartyConnection>(Lifetime.Singleton);
            builder.Register<IScoutConnection, ScoutConnection>(Lifetime.Singleton);
            builder.Register<IDebugConnection, DebugConnection>(Lifetime.Singleton);

            builder.Register<IPlayerAccountService, PlayerAccountService>(Lifetime.Singleton);
            builder.Register<IItemFetchService, ItemFetchService>(Lifetime.Singleton);
            builder.Register<IPartyService, Infrastructure.PartyService>(Lifetime.Singleton);
            builder.Register<IPachimonService, PachimonService>(Lifetime.Singleton);
            builder.Register<IPachimonMoveMappingService, PachimonMoveMappingService>(Lifetime.Singleton);
            builder.Register<IPlayerSession, PlayerSession>(Lifetime.Singleton);
            builder.Register<ISignInService, SignInService>(Lifetime.Singleton);
            builder.Register<IErrorNavigator, ErrorNavigator>(Lifetime.Singleton);

            ConfigureBattleConnections(builder);

            builder.RegisterBootInitializer();
            builder.Register<MasterDataInitializationTask>(Lifetime.Singleton).As<IBootInitializationTask>();
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (clockResetter != null)
            {
                Destroy(clockResetter.gameObject);
            }

            channel?.Dispose();
        }

        // 通信の送り先。Battle PlayModeテスト(TestRootLifetimeScope)は、サーバーの代わりに偽のサーバーを呼ぶ
        // FakeCallInvokerに差し替え、ローカルAPIサーバー無しでBootstrap→Home→Battleの起動経路を検証する。
        protected virtual CallInvoker CreateCallInvoker()
        {
            // チャンネルは1つを使い回す。エディタと開発ビルドでは、gRPC Monitorに通信が記録される
            channel = ApiChannel.Create(GrpcAddress);
            return ApiChannel.CreateCallInvoker(channel);
        }

        // 対戦のマッチングと接続(IBattleConnectionはBattleシーンのスコープがIBattleConnectionFactoryで都度生成する)。
        // Battle PlayModeテスト(TestRootLifetimeScope)はuseRealBattleServerに関わらず常にMockにする。
        protected virtual void ConfigureBattleConnections(IContainerBuilder builder)
        {
            if (useBattleServer)
            {
                builder.Register<IBattleMatchmaker, GrpcBattleMatchmaker>(Lifetime.Singleton);
                builder.Register<IBattleConnectionFactory, RealtimeBattleConnectionFactory>(Lifetime.Singleton);
            }
            else
            {
                builder.Register<IBattleMatchmaker, MockBattleMatchmaker>(Lifetime.Singleton);
                builder.Register<IBattleConnectionFactory, MockBattleConnectionFactory>(Lifetime.Singleton);
            }
        }

        // gRPCの通信の共通処理(ApiCore)と、サービスごとの呼び出し(生成したラッパー)。
        // デバイス登録とトークンの発行は、最初の通信でApiCoreが自動で行う。
        private void ConfigureGrpc(IContainerBuilder builder)
        {
            var options = new ApiCoreOptions(UnityAppVersion.Create(AppVersion));
            var callInvoker = CreateCallInvoker();
            // ApiCoreとServerClockResetterに同じ時計を渡す(前面に戻ったら合わせ直す)
            var clock = new ServerClock();

            builder.RegisterInstance(callInvoker).As<CallInvoker>();
            builder.RegisterInstance(clock).As<IServerClock>();
            builder.Register<IAccountStore>(
                resolver => new FileAccountStore(resolver.Resolve<IFileStorageService>(), GrpcAddress),
                Lifetime.Singleton);
            builder.RegisterComponent(errorDialog).As<IErrorDialogView>();
            builder.Register(resolver => new ApiCore(
                    options,
                    new DeviceTokenProvider(callInvoker, resolver.Resolve<IAccountStore>(), options),
                    // 業務エラーの既定のactionは、Shared/errors.tomlから生成した一覧から引く
                    new ErrorPolicyProvider(AtlasErrors.All),
                    new DefaultCommonErrorHandler(resolver.Resolve<IErrorDialogView>()),
                    resolver.Resolve<PlayerDiffApplier>(),
                    // リリースビルドでは通信内容をログに残さない(Debug.isDebugBuildはEditorと開発ビルドでtrue)
                    logger: Debug.isDebugBuild ? new UnityApiLogger() : null,
                    serverClock: clock,
                    navigator: resolver.Resolve<IErrorNavigator>()),
                Lifetime.Singleton);
            builder.Register(resolver => new PlayerApi(resolver.Resolve<ApiCore>(), callInvoker), Lifetime.Singleton);
            builder.Register(resolver => new PartyApi(resolver.Resolve<ApiCore>(), callInvoker), Lifetime.Singleton);
            builder.Register(resolver => new ScoutApi(resolver.Resolve<ApiCore>(), callInvoker), Lifetime.Singleton);
            builder.Register(resolver => new DebugApi(resolver.Resolve<ApiCore>(), callInvoker), Lifetime.Singleton);
            builder.Register(resolver => new MatchmakingApi(resolver.Resolve<ApiCore>(), callInvoker), Lifetime.Singleton);

            builder.RegisterBuildCallback(_ => clockResetter = ServerClockResetter.Create(clock));
        }
    }

    // 起動時の初期化(IBootInitializationTaskとして登録したもの)を済ませてからHomeへ進む。
    internal sealed class BootstrapEntryPoint : IAsyncStartable
    {
        private readonly ISceneNavigator sceneNavigator;
        private readonly BootInitializer bootInitializer;

        public BootstrapEntryPoint(ISceneNavigator sceneNavigator, BootInitializer bootInitializer)
        {
            this.sceneNavigator = sceneNavigator;
            this.bootInitializer = bootInitializer;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await bootInitializer.RunAsync(cancellation);

            // HomeLifetimeScope.FindParentが直接RootLifetimeScopeを探すため、ここで
            // LifetimeScope.EnqueueParentを使う必要は無い。
            await sceneNavigator.ChangeSceneAsync(AddressDefinition.Home, cancellation);
        }
    }
}
