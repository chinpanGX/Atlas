using System.Threading.Channels;
using Atlas.BattleContracts;
using Grpc.Core;
using Grpc.Net.Client;
using MagicOnion.Client;

namespace Atlas.BattleBot
{
    // BattleServerに接続して1対戦を最後まで行う。技は使えるもの(PPが残っているもの)からランダム、
    // 強制交代(瀕死)の時は生存している先頭の控えに交代する(design/battle.mdの簡易AIと同じ方針)。
    //
    // 受信(IBattleHubReceiver)はMagicOnionの受信処理から呼ばれるため、その中でHubを呼ばず、
    // イベントをChannelに積んでRunAsyncのループで1つずつ処理する。
    public sealed class BotBattle : IBattleHubReceiver
    {
        private readonly Channel<object> events = Channel.CreateUnbounded<object>();
        private readonly string name;
        private readonly Random random = new();

        private IBattleHub hub = null!;
        private string selfId = "";
        private int activeIndex;
        private bool[] fainted = [];
        private string[][] moveIds = [];
        private int[][] currentPp = [];

        public BotBattle(string name)
        {
            this.name = name;
        }

        // 勝者のPlayerIdを返す。
        public async Task<string> RunAsync(string battleServerUrl, string battleToken, string matchId, string[] selection)
        {
            using var channel = GrpcChannel.ForAddress(battleServerUrl);
            hub = await StreamingHubClient.ConnectAsync<IBattleHub, IBattleHubReceiver>(channel, this);
            try
            {
                var join = await hub.JoinAsync(battleToken, matchId);
                if (join.Status != JoinResultStatus.Success)
                {
                    throw new InvalidOperationException($"JoinAsync failed: {join.Status}");
                }

                await hub.SubmitSelectionAsync(selection);

                await foreach (var e in events.Reader.ReadAllAsync())
                {
                    switch (e)
                    {
                        case BattleStartPayload start:
                            HandleStart(start);
                            await ActAsync(forcedSwitch: false);
                            break;
                        case TurnResultPayload turn:
                            HandleTurn(turn);
                            await ActAsync(forcedSwitch: turn.PlayersRequiringForcedSwitch.Contains(selfId));
                            break;
                        case BattleEndPayload end:
                            Log($"決着: {(end.WinnerId == selfId ? "勝ち" : "負け")} ({end.Reason})");
                            return end.WinnerId;
                    }
                }

                throw new InvalidOperationException("決着前に切断されました。");
            }
            finally
            {
                await hub.DisposeAsync();
            }
        }

        private void HandleStart(BattleStartPayload start)
        {
            selfId = start.Self.PlayerId;
            activeIndex = start.Self.ActivePachimonIndex;
            fainted = start.Self.SelectedPachimon.Select(s => s.State!.IsFainted).ToArray();
            moveIds = start.SelfMoves.Select(set => set.Moves.Select(m => m.MoveId).ToArray()).ToArray();
            currentPp = start.SelfMoves.Select(set => set.Moves.Select(m => m.CurrentPp).ToArray()).ToArray();
            Log($"対戦開始 vs {start.Opponent.PlayerId} (技: {string.Join(",", moveIds[activeIndex])})");
        }

        private void HandleTurn(TurnResultPayload turn)
        {
            foreach (var action in turn.Actions)
            {
                if (action.PlayerId == selfId)
                {
                    if (action.Type == ActionType.Switch && action.NewActiveIndex is { } newIndex)
                    {
                        activeIndex = newIndex;
                    }
                    else if (action.Type == ActionType.Move)
                    {
                        var moveIndex = Array.IndexOf(moveIds[activeIndex], action.MoveId);
                        if (moveIndex >= 0)
                        {
                            currentPp[activeIndex][moveIndex]--;
                        }
                    }
                }
                else if (action.Type == ActionType.Move && action.TargetFainted)
                {
                    fainted[activeIndex] = true;
                }
            }

            Log($"ターン{turn.TurnNumber}: {string.Join(" / ", turn.Actions.Select(a => $"{(a.PlayerId == selfId ? "自分" : "相手")}:{a.Type}"))}");
        }

        private async Task ActAsync(bool forcedSwitch)
        {
            try
            {
                if (forcedSwitch)
                {
                    var next = Enumerable.Range(0, fainted.Length).First(i => !fainted[i] && i != activeIndex);
                    await hub.SwitchAsync(next);
                    return;
                }

                var usable = Enumerable.Range(0, moveIds[activeIndex].Length).Where(i => currentPp[activeIndex][i] > 0).ToList();
                var moveId = moveIds[activeIndex][usable[random.Next(usable.Count)]];
                await hub.SubmitMoveAsync(new MoveRequest(moveId));
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.FailedPrecondition)
            {
                // 最終ターンの結果の直後(決着を受信する前)に行動を送った場合。直後にOnBattleEndが届く。
            }
        }

        private void Log(string message) => Console.WriteLine($"[{name}] {message}");

        void IBattleHubReceiver.OnMatchStart(BattleStartPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnTurnResult(TurnResultPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnBattleEnd(BattleEndPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnOpponentDisconnected() => Log("相手が切断しました(再接続待ち)");
        void IBattleHubReceiver.OnOpponentReconnected() => Log("相手が再接続しました");
    }
}
