namespace Atlas.Application
{
    // マッチング成立結果。BattleServerへの接続(IBattleConnection)に必要な情報をまとめたもの。
    public sealed class BattleMatch
    {
        public readonly string MatchId;
        public readonly string BattleServerUrl;
        // BattleServer接続用の短命トークン(有効期限30秒)。受け取ったらすぐ接続する。
        public readonly string BattleToken;
        // 選出(パーティの枠番号順に先頭から最大3体)のPlayerPachimonId。
        public readonly string[] SelectedPlayerPachimonIds;

        public BattleMatch(string matchId, string battleServerUrl, string battleToken, string[] selectedPlayerPachimonIds)
        {
            MatchId = matchId;
            BattleServerUrl = battleServerUrl;
            BattleToken = battleToken;
            SelectedPlayerPachimonIds = selectedPlayerPachimonIds;
        }
    }
}
