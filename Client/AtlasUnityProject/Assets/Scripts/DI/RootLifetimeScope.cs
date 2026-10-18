using Atlas.Api.V1;
using Atlas.Application;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Grpc;
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
    /// 通信の実装、各IXxxRepository/IXxxServiceの登録もここで行う構成ルート
    /// (Composition Root)。Atlas.Presentationとは別アセンブリに分離し、Presenter自身は
    /// Atlas.Infrastructureを直接参照しない。
    /// </summary>
    public class RootLifetimeScope : LifetimeScope
    {
        // APIサーバーのgRPCの待ち受け先(Server/.envのGRPC_ADDRの既定値)。環境切り替え(本番/ステージング等)の
        // 仕組みは未導入のためハードコードしている。
        private const string GrpcAddress = "http://127.0.0.1:50052";

        // リリースのたびに増やす整数のバージョン。サーバーはこれで強制アップデートを判定する
        // (エディタからの通信は判定しない)。
        private const int AppVersion = 1;

        // 通信エラーのダイアログ。シーンの切り替え中でも出せるよう、Bootstrapシーンに置く。
        [SerializeField] private ErrorDialog errorDialog;

        // 通信中に操作を塞いで「Connecting...」を出す。エラーのダイアログより奥に置く。
        [SerializeField] private ConnectingIndicator connectingIndicator;

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

            builder.RegisterGrpc(GrpcAddress, AppVersion, CreateCallInvoker(), errorDialog, connectingIndicator, x => clockResetter = x);
            // 対戦のマッチングと接続(IBattleConnectionはBattleシーンのスコープがIBattleConnectionFactoryで都度生成する)。
            builder.Register<IBattleMatchMaker, BattleMatchMaker>(Lifetime.Singleton);
            builder.Register<IBattleConnectionFactory, RealtimeBattleConnectionFactory>(Lifetime.Singleton);

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

        // チャンネルは1つを使い回す。エディタと開発ビルドでは、gRPC Monitorに通信が記録される
        private CallInvoker CreateCallInvoker()
        {
            channel = ApiChannel.Create(GrpcAddress);
            return ApiChannel.CreateCallInvoker(channel);
        }
    }

    // 起動時の初期化(IBootInitializationTaskとして登録したもの)を済ませてからHomeへ進む。
}
