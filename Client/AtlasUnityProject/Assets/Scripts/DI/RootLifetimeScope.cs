using Atlas.Api.V1;
using Atlas.Application;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Grpc;
using Atlas.Infrastructure.Mock;
using Atlas.Infrastructure.Realtime;
using Atlas.Navigation;
using Atlas.Presentation.Common;
using Grpc.Core;
using Grpc.Net.Client;
using Supplement.Core;
using Supplement.MessagePack;
using Supplement.Unity;
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
            builder.RegisterMessagePackFileStorage();
            if (SaveDataDirectory.Resolve() is { } saveDataDirectory)
            {
                builder.RegisterBuildCallback(resolver =>
                    resolver.Resolve<IFileStorageService>().SetDirectoryName(saveDataDirectory));
            }
            builder.RegisterMessageBroker();

            builder.Register<SceneNavigator>(Lifetime.Singleton);
            builder.Register<ISceneNavigator, TransitionAwareSceneNavigator>(Lifetime.Singleton);
            builder.Register<IErrorNavigator, ErrorNavigator>(Lifetime.Singleton);
            builder.Register<BattleEntryStore>(Lifetime.Singleton);

            builder.RegisterInfrastructure();

            builder.RegisterGrpc(GrpcAddress, AppVersion, CreateCallInvoker(), errorDialog, x => clockResetter = x);
            ConfigureBattleConnections(builder);

            builder.RegisterBootInitializer();
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
                builder.Register<IBattleMatchMaker, BattleMatchMaker>(Lifetime.Singleton);
                builder.Register<IBattleConnectionFactory, RealtimeBattleConnectionFactory>(Lifetime.Singleton);
            }
            else
            {
                builder.Register<IBattleMatchMaker, MockBattleMatchMaker>(Lifetime.Singleton);
                builder.Register<IBattleConnectionFactory, MockBattleConnectionFactory>(Lifetime.Singleton);
            }
        }
    }

    // 起動時の初期化(IBootInitializationTaskとして登録したもの)を済ませてからHomeへ進む。
}
