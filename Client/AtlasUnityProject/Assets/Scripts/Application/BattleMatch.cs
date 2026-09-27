namespace Atlas.Application
{
    // マッチング成立結果。BattleServerへの接続(IBattleConnection)に必要な情報をまとめたもの。
    public sealed class BattleMatch
    {
        public readonly string MatchId;
        public readonly string BattleServerUrl;
        // BattleServer接続用の短命トークン(有効期限30秒)。受け取ったらすぐ接続する。
        public readonly string BattleToken;

        public BattleMatch(string matchId, string battleServerUrl, string battleToken)
        {
            MatchId = matchId;
            BattleServerUrl = battleServerUrl;
            BattleToken = battleToken;
        }
    }
}
