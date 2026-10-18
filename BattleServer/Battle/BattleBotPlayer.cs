using System.Threading.Channels;
using Atlas.BattleContracts;
using Grpc.Core;
using MagicOnion;

namespace Atlas.BattleServer.Battle
{
    // マッチングで相手が見つからなかったプレイヤーの対戦相手。人の接続の代わりに参加者の送り先(Receiver)になり、
    // BattleHubと同じようにBattleCoordinatorのメソッドで選出・行動する(検証・制限時間も人と同じに通る)。
    // 判断には人のクライアントと同じ通知の内容だけを使い、サーバーの盤面(HPの実数・相手の未公開の個体)は見ない。
    // 選出はパーティからランダム、技はPPが残っているものからランダム、強制交代は生存している先頭の控え。
    //
    // すぐに送るとボットだと気付かれやすいため、人と同じように間を置いて送る。技・交代は、人が選び始めた時点
    // (両者の準備がそろって制限時間を数え始めた、OnTurnStart)から間を置く。相手の再接続の後もOnTurnStartが届くので、
    // 相手の切断中に送って弾かれた行動はそこで送り直す。
    //
    // 通知はBattleCoordinatorがsession.Gateを取ったまま呼ぶ。行動のメソッドもGateを取るため、通知の中では
    // 行動せず、Channelに積んでRunAsyncのループで1つずつ処理する。
    public sealed class BattleBotPlayer(
        BattleCoordinator coordinator,
        BattleSession session,
        int slot,
        BattleTimingOptions timing,
        ILogger logger) : IBattleHubReceiver
    {
        private sealed record TurnStarted;

        private readonly Channel<object> events = Channel.CreateUnbounded<object>();

        private string selfId = "";
        private int activeIndex;
        private bool[] fainted = [];
        private string[][] moveIds = [];
        private int[][] currentPp = [];

        // このターンに、まだ行動を受け付けてもらっていないか。
        private bool actionPending;
        private bool forcedSwitch;

        // 決着(OnBattleEnd)を受けたら終わる。
        public async Task RunAsync()
        {
            try
            {
                await foreach (var e in events.Reader.ReadAllAsync())
                {
                    switch (e)
                    {
                        case SelectionStartPayload selection when !selection.SelectionSubmitted:
                            await SelectAsync(selection);
                            break;
                        case BattleStartPayload start:
                            HandleStart(start);
                            // 演出が無いので、すぐに準備を送る(送らないと、相手は準備待ちの上限まで待たされる)。
                            await coordinator.ReadyForTurnAsync(session, slot);
                            actionPending = true;
                            forcedSwitch = false;
                            break;
                        case TurnResultPayload turn:
                            HandleTurn(turn);
                            await coordinator.ReadyForTurnAsync(session, slot);
                            // 相手の強制交代ターンは、相手が交代するまで何も送れない(次のターン結果を待つ)。
                            forcedSwitch = turn.PlayersRequiringForcedSwitch.Contains(selfId);
                            actionPending = turn.PlayersRequiringForcedSwitch.Length == 0 || forcedSwitch;
                            break;
                        case TurnStarted:
                            await ActAsync();
                            break;
                        case BattleEndPayload:
                            return;
                    }
                }
            }
            catch (Exception e)
            {
                // 行動しなくなっても、制限時間で対戦は終わる。
                logger.LogError(e, "Bot stopped. matchId={MatchId}", session.MatchId);
            }
        }

        private async Task SelectAsync(SelectionStartPayload selection)
        {
            var selected = selection.SelfParty
                .OrderBy(_ => Random.Shared.Next())
                .Take(selection.MaxSelectionCount)
                .Select(p => p.PlayerPachimonId)
                .ToArray();
            await Task.Delay(RandomDelay(timing.BotSelectionDelayMin, timing.BotSelectionDelayMax));
            try
            {
                await coordinator.SubmitSelectionAsync(session, slot, selected);
            }
            catch (ReturnStatusException e) when (e.StatusCode == StatusCode.FailedPrecondition)
            {
                // 間を置いている間に決着した場合(相手の降参等)。直後にOnBattleEndが届く。
            }
        }

        private void HandleStart(BattleStartPayload start)
        {
            selfId = start.Self.PlayerId;
            activeIndex = start.Self.ActivePachimonIndex;
            fainted = start.Self.SelectedPachimon.Select(s => s.State!.IsFainted).ToArray();
            moveIds = start.SelfMoves.Select(set => set.Moves.Select(m => m.MoveId).ToArray()).ToArray();
            currentPp = start.SelfMoves.Select(set => set.Moves.Select(m => m.CurrentPp).ToArray()).ToArray();
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
                        int moveIndex = Array.IndexOf(moveIds[activeIndex], action.MoveId);
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
        }

        private async Task ActAsync()
        {
            if (!actionPending)
            {
                return;
            }

            await Task.Delay(RandomDelay(timing.BotActionDelayMin, timing.BotActionDelayMax));
            try
            {
                if (forcedSwitch)
                {
                    int next = Enumerable.Range(0, fainted.Length).First(i => !fainted[i] && i != activeIndex);
                    await coordinator.SwitchAsync(session, slot, next);
                }
                else
                {
                    var usable = Enumerable.Range(0, moveIds[activeIndex].Length).Where(i => currentPp[activeIndex][i] > 0).ToList();
                    if (usable.Count == 0)
                    {
                        // 全部の技のPPが尽きたら何もしない(時間切れでスキップになる)。
                        actionPending = false;
                        return;
                    }

                    var moveId = moveIds[activeIndex][usable[Random.Shared.Next(usable.Count)]];
                    await coordinator.SubmitMoveAsync(session, slot, new MoveRequest(moveId));
                }

                actionPending = false;
            }
            catch (ReturnStatusException e) when (e.StatusCode == StatusCode.FailedPrecondition)
            {
                // 間を置いている間に相手が切断した(戻ったら届くOnTurnStartで送り直す)、または決着した場合。
            }
        }

        private static TimeSpan RandomDelay(TimeSpan min, TimeSpan max) =>
            min + (max - min) * Random.Shared.NextDouble();

        void IBattleHubReceiver.OnSelectionStart(SelectionStartPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnMatchStart(BattleStartPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnTurnResult(TurnResultPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnBattleEnd(BattleEndPayload payload) => events.Writer.TryWrite(payload);
        void IBattleHubReceiver.OnTurnStart(int timeLimitSeconds) => events.Writer.TryWrite(new TurnStarted());
        void IBattleHubReceiver.OnOpponentDisconnected() { }
        void IBattleHubReceiver.OnOpponentReconnected() { }
    }
}
