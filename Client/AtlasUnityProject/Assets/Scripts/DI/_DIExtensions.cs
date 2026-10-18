using System;
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
            ConnectingIndicator connectingIndicator,
            Action<ServerClockResetter> onClockResetterCreated)
        {
            var options = new ApiCoreOptions(UnityAppVersion.Create(appVersion));
            var clock = new ServerClock();

            builder.RegisterInstance(callInvoker);
            builder.RegisterInstance(options);
            builder.RegisterInstance(clock).As<IServerClock>();
            builder.Register<IAccountStore, FileAccountStore>(Lifetime.Singleton)
                .WithParameter("serverAddress", grpcAddress);
            // サインインで、端末に保存したアカウント情報の有無を確かめるのにも使う
            builder.Register<DeviceTokenProvider>(Lifetime.Singleton)
                .WithParameter<Func<DateTime>>(() => DateTime.UtcNow);
            builder.RegisterComponent(errorDialog).As<IErrorDialogView, ISystemDialog>();
            // ApiCoreはコンストラクタが公開されておらずビルダーでしか作れないため、ここだけ生成処理で登録する
            builder.Register(resolver => ApiCore.CreateBuilder(
                        options,
                        resolver.Resolve<DeviceTokenProvider>(),
                        // 業務エラーの既定のactionは、Shared/errors.tomlから生成した一覧から引く
                        new ErrorPolicyProvider(AtlasErrors.All),
                        new DefaultCommonErrorHandler(errorDialog))
                    .WithPlayerDiffApplier(resolver.Resolve<PlayerDiffApplier>())
                    // リリースビルドでは通信内容をログに残さない(Debug.isDebugBuildはEditorと開発ビルドでtrue)
                    .WithLogger(Debug.isDebugBuild ? new UnityApiLogger() : null)
                    .WithServerClock(clock)
                    .WithErrorNavigator(resolver.Resolve<IErrorNavigator>())
                    .AddActivityHandler(connectingIndicator)
                    .Build(),
                Lifetime.Singleton);
            builder.Register<PlayerApi>(Lifetime.Singleton);
            builder.Register<PartyApi>(Lifetime.Singleton);
            builder.Register<ScoutApi>(Lifetime.Singleton);
            builder.Register<ShopApi>(Lifetime.Singleton);
            builder.Register<DebugApi>(Lifetime.Singleton);
            builder.Register<MatchmakingApi>(Lifetime.Singleton);

            builder.RegisterBuildCallback(_ => onClockResetterCreated(ServerClockResetter.Create(clock)));
        }
    }