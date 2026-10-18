using System;
using Atlas.Application;
using Atlas.Domain;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Grpc;
using Atlas.Infrastructure.Realtime;
using Supplement.Core;
using VContainer;

public static class _DIExtensions
{
    public static void RegisterInfrastructure(this IContainerBuilder builder)
    {
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

        builder.Register<IScoutService, ScoutService>(Lifetime.Singleton);
        builder.Register<IShopService, ShopService>(Lifetime.Singleton);
        builder.Register<IDebugConnectService, DebugConnectService>(Lifetime.Singleton);

        builder.Register<IPlayerAccountService, PlayerAccountService>(Lifetime.Singleton);
        builder.Register<IItemService, ItemService>(Lifetime.Singleton);
        builder.Register<IPartyService, PartyService>(Lifetime.Singleton);
        builder.Register<IPachimonMoveEditService, PachimonMoveEditService>(Lifetime.Singleton);
        builder.Register<IPachimonStatPointsEditService, PachimonStatPointsEditService>(Lifetime.Singleton);
        builder.Register<IPachimonService, PachimonService>(Lifetime.Singleton);
        builder.Register<IPachimonMoveMappingService, PachimonMoveMappingService>(Lifetime.Singleton);
        builder.Register<IPlayerSession, PlayerSession>(Lifetime.Singleton);
        builder.Register<ISignInService, SignInService>(Lifetime.Singleton);

        builder.RegisterMasterData();
        
    }

    private static void RegisterMasterData(this IContainerBuilder builder)
    {
        builder.Register<MasterDatabaseLoader>(Lifetime.Singleton);
        // マスタは起動時の初期化(MasterDataInitializationTask)で読み込むため、Build時には登録できない。
        // 最初に要求されたときに読み込み済みのものを返す。Home・Battleの画面は初期化の後に作られるので、
        // コンストラクタでMemoryDatabaseを受け取れる。初期化より前に要求したら例外にして気づけるようにする。
        builder.Register(resolver => resolver.Resolve<MasterDatabaseLoader>().Database
                                     ?? throw new InvalidOperationException(
                                         "MemoryDatabase was requested before the master data was loaded."),
            Lifetime.Singleton);

        builder.Register<IBootInitializationTask, MasterDataInitializationTask>(Lifetime.Singleton);
    }
}