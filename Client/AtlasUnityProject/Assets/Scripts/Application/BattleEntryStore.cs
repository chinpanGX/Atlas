namespace Atlas.Application
{
    // Home→Battleのシーン切り替えをまたいで、対戦開始に必要な情報を受け渡すための入れ物。
    // Page間の受け渡しはPush時のViewDtoで行うが、シーンをまたぐとHome側のスコープは破棄されるため、
    // RootLifetimeScope(Singleton)に置いたこのクラスを経由する。Home側がSetしてからBattleシーンへ
    // 切り替え、BattleシーンのEntryPointが読み出してBattlePageへのViewDtoに詰め替える。
    public sealed class BattleEntryStore
    {
        // 選出3体のID(暫定: player_pachimonを使ったパーティ編成が未実装のため、マスターデータの
        // PachimonIdを文字列化したもの。design/battle.md「Stage 1」参照)。
        public string[] SelfPachimonIds { get; private set; }

        public void Set(string[] selfPachimonIds)
        {
            SelfPachimonIds = selfPachimonIds;
        }
    }
}
