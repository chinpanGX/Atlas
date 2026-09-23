namespace Atlas.BattleServer.Battle
{
    // 対戦進行・結果報告に関わる時間設定。既定値はdocs/design/battle.mdの仮値。
    // 自動テストで待ち時間を短くするために差し替えられるようにしている(運用時は既定値のまま使う想定)。
    public sealed class BattleTimingOptions
    {
        // 「パチモン選出」: 両者の参加がそろってからこの時間内に選出しなかった側の敗北(Forfeit)。
        public TimeSpan SelectionTimeLimit { get; set; } = TimeSpan.FromMinutes(2);

        // 「ターンタイムアウト」: 制限時間内に行動しなかった側は非行動(Skip)。
        public TimeSpan TurnTimeLimit { get; set; } = TimeSpan.FromSeconds(30);

        // 「放置」: ターンタイムアウトによる非行動がこの回数連続した側の敗北(Forfeit)。
        public int MaxConsecutiveIdleTurns { get; set; } = 3;

        // 「切断・再接続」: 猶予時間内に再接続が無ければ切断側の敗北。
        public TimeSpan ReconnectGracePeriod { get; set; } = TimeSpan.FromSeconds(60);

        // 決着後もしばらく残し、遅れて来たJoinAsyncにMatchNotFoundを返せるようにする
        // (battle_tokenの有効期限30秒+ClockSkewより長ければよい)。
        public TimeSpan FinishedSessionRetention { get; set; } = TimeSpan.FromSeconds(60);

        // /internal/battle/resultへの報告が一時的な失敗(通信エラー・5xx)だった場合の再送間隔。
        // 要素数が再送回数になる。
        public TimeSpan[] ResultReportRetryDelays { get; set; } =
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    }
}
