using Atlas.Api.V1;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Grpc;
using Atlas.Presentation.Common;
using Grpc.Core;
using Supplement.Core;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using VolTech.Client;
using VolTech.Client.Auth;
using VolTech.Client.Unity;

public static class DIExtensions
    {
        public static void RegisterGrpc(
            this IContainerBuilder builder,
            string grpcAddress,
            int appVersion,
            CallInvoker callInvoker,
            ErrorDialog errorDialog,
            System.Action<ServerClockResetter> onClockResetterCreated)
        {
            var options = new ApiCoreOptions(UnityAppVersion.Create(appVersion));
            var clock = new ServerClock();

            builder.RegisterInstance(callInvoker).As<CallInvoker>();
            builder.RegisterInstance(clock).As<IServerClock>();
            builder.Register<IAccountStore>(
                resolver => new FileAccountStore(resolver.Resolve<IFileStorageService>(), grpcAddress),
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

            builder.RegisterBuildCallback(_ => onClockResetterCreated(ServerClockResetter.Create(clock)));
        }
    }