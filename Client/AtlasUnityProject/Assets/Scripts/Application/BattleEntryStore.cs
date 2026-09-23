namespace Atlas.Application
{
    // Home→Battleのシーン切り替えをまたいで、対戦開始に必要な情報を受け渡すための入れ物。
    // Page間の受け渡しはPush時のViewDtoで行うが、シーンをまたぐとHome側のスコープは破棄されるため、
    // RootLifetimeScope(Singleton)に置いたこのクラスを経由する。Home側がマッチング成立後にSetしてから
    // Battleシーンへ切り替え、BattleシーンのLifetimeScopeが読み出してIBattleConnectionの生成と
    // BattlePageへのViewDtoに使う。
    public sealed class BattleEntryStore
    {
        public BattleMatch Match { get; private set; }

        public void Set(BattleMatch match)
        {
            Match = match;
        }
    }
}
