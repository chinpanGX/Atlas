using System.Text.Json;
using Atlas.BattleCore;
using Atlas.BattleContracts;
using Grpc.Core;

namespace Atlas.BattleServer.Tests
{
    // BattleHub(MagicOnion StreamingHub)を2クライアントから操作する結合テスト。
    // ステータス・技はDummyParticipantDataSource(全員同一ステータス、技19/20)を前提にしている。
    public class BattleHubTests
    {
        [Fact]
        public async Task JoinAsync_RejectsInvalidTokens()
        {
            await using var host = new BattleServerTestHost();
            var (hub, _) = await host.ConnectAsync();

            var valid = BattleServerTestHost.Token("m", "p");
            var tampered = valid[..^4] + (valid[^4] == 'A' ? "BBBB" : "AAAA");

            Assert.Equal(JoinResultStatus.InvalidToken, (await hub.JoinAsync("garbage", "m")).Status);
            Assert.Equal(JoinResultStatus.InvalidToken, (await hub.JoinAsync(valid, "other-match")).Status);
            Assert.Equal(JoinResultStatus.InvalidToken, (await hub.JoinAsync(tampered, "m")).Status);
            Assert.Equal(JoinResultStatus.InvalidToken, (await hub.JoinAsync(BattleServerTestHost.Token("m", "p", secret: "wrong-secret-wrong-secret-wrong-secret"), "m")).Status);
            // 期限切れトークンは既存の対戦への再接続にしか使えない
            Assert.Equal(JoinResultStatus.InvalidToken, (await hub.JoinAsync(BattleServerTestHost.Token("m", "p", expOffsetSeconds: -60), "m")).Status);

            Assert.Equal(JoinResultStatus.Success, (await hub.JoinAsync(valid, "m")).Status);
            Assert.Equal(JoinResultStatus.AlreadyJoined, (await hub.JoinAsync(valid, "m")).Status);

            var (another, _) = await host.ConnectAsync();
            Assert.Equal(JoinResultStatus.AlreadyJoined, (await another.JoinAsync(valid, "m")).Status);
        }

        [Fact]
        public async Task MatchStart_IsSentOnlyAfterBothSelected_AndHidesUnrevealedOpponentSlots()
        {
            await using var host = new BattleServerTestHost();
            var (a, ra) = await host.ConnectAsync();
            var (b, rb) = await host.ConnectAsync();
            await a.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m");
            await b.JoinAsync(BattleServerTestHost.Token("m", "pB"), "m");

            await AssertRpcStatus(StatusCode.InvalidArgument, () => a.SubmitSelectionAsync(["a1", "a1"]));
            await a.SubmitSelectionAsync(["a1", "a2", "a3"]);
            await Task.Delay(100);
            Assert.Empty(ra.Starts);

            await b.SubmitSelectionAsync(["b1", "b2", "b3"]);
            await ra.WaitForAsync(r => r.Starts.Count == 1);
            await rb.WaitForAsync(r => r.Starts.Count == 1);

            var start = ra.Starts[0];
            Assert.Equal("pA", start.Self.PlayerId);
            Assert.Equal("pB", start.Opponent.PlayerId);
            Assert.All(start.Self.SelectedPachimon, slot => Assert.True(slot.IsRevealed && slot.State is not null));
            Assert.True(start.Opponent.SelectedPachimon[0].IsRevealed);
            Assert.All(start.Opponent.SelectedPachimon.Skip(1), slot => Assert.True(!slot.IsRevealed && slot.State is null));
            Assert.Equal(100, start.Opponent.SelectedPachimon[0].State!.HpPercent);
            Assert.Equal(30, start.TurnTimeLimitSeconds);

            // 自分側の技(サーバーが判定に使う技)が選出各枠分届く。DummyParticipantDataSourceは技19/20固定。
            Assert.Equal(3, start.SelfMoves.Length);
            Assert.All(start.SelfMoves, set =>
            {
                Assert.Equal(["19", "20"], set.Moves.Select(m => m.MoveId));
                Assert.All(set.Moves, m => Assert.Equal(m.MaxPp, m.CurrentPp));
            });
        }

        [Fact]
        public async Task Actions_AreValidated()
        {
            await using var host = new BattleServerTestHost();
            var (a, _) = await host.ConnectAsync();
            await a.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m");
            await AssertRpcStatus(StatusCode.FailedPrecondition, () => a.SubmitMoveAsync(new MoveRequest("19")));

            var (b, _) = await host.ConnectAsync();
            await b.JoinAsync(BattleServerTestHost.Token("m", "pB"), "m");
            await a.SubmitSelectionAsync(["a1", "a2"]);
            await b.SubmitSelectionAsync(["b1", "b2"]);
            await Task.Delay(100);

            await AssertRpcStatus(StatusCode.InvalidArgument, () => a.SubmitMoveAsync(new MoveRequest("999")));
            await AssertRpcStatus(StatusCode.InvalidArgument, () => a.SwitchAsync(0)); // 場に出ている枠
            await AssertRpcStatus(StatusCode.InvalidArgument, () => a.SwitchAsync(5)); // 範囲外
            await a.SubmitMoveAsync(new MoveRequest("19"));
            await AssertRpcStatus(StatusCode.FailedPrecondition, () => a.SubmitMoveAsync(new MoveRequest("19")));
        }

        [Fact]
        public async Task Battle_ProgressesUntilAllFainted_AndReportsResult()
        {
            await using var host = new BattleServerTestHost();
            var (a, ra, b, rb) = await host.StartBattleAsync("m", ["a1", "a2", "a3"], ["b1", "b2", "b3"]);

            // 1ターン目: Bが交代(初公開)、Aは技。交代は技より先に処理される
            await b.SwitchAsync(1);
            await a.SubmitMoveAsync(new MoveRequest("19"));
            await ra.WaitForAsync(r => r.Turns.Count == 1);
            var switchAction = ra.Turns[0].Actions[0];
            Assert.Equal(ActionType.Switch, switchAction.Type);
            Assert.Equal("pB", switchAction.PlayerId);
            Assert.Equal(1, switchAction.NewActiveIndex);
            Assert.Equal("b2", switchAction.RevealedPachimon?.PlayerPachimonId);
            Assert.Equal(ActionType.Move, ra.Turns[0].Actions[1].Type);

            // 決着まで殴り合う。強制交代が必要な側は生存している枠へ交代する
            var active = new Dictionary<string, int> { ["pA"] = 0, ["pB"] = 1 };
            var fainted = new Dictionary<string, bool[]> { ["pA"] = new bool[3], ["pB"] = new bool[3] };
            while (ra.Ends.Count == 0)
            {
                Assert.True(ra.Turns.Count < 200, "battle did not finish");
                var last = ra.Turns[^1];
                foreach (var action in last.Actions.Where(x => x.Type == ActionType.Move && x.TargetFainted))
                {
                    var target = action.PlayerId == "pA" ? "pB" : "pA";
                    fainted[target][active[target]] = true;
                }

                Task Act(IBattleHub hub, string playerId, string moveId)
                {
                    if (!last.PlayersRequiringForcedSwitch.Contains(playerId))
                    {
                        return hub.SubmitMoveAsync(new MoveRequest(moveId));
                    }

                    active[playerId] = Array.FindIndex(fainted[playerId], f => !f);
                    return hub.SwitchAsync(active[playerId]);
                }

                int before = ra.Turns.Count;
                await Task.WhenAll(Act(a, "pA", "19"), Act(b, "pB", "20"));
                await ra.WaitForAsync(r => r.Turns.Count > before);
            }

            await rb.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(BattleEndReason.AllFainted, ra.Ends[0].Reason);
            Assert.Equal(ra.Ends[0], rb.Ends[0]);
            Assert.Contains(ra.Turns, t => t.PlayersRequiringForcedSwitch.Length > 0);

            await host.ApiServer.WaitForCountAsync(1);
            Assert.True(host.ApiServer.Received.TryPeek(out var request));
            Assert.Equal("/internal/battle/result", request.Path);
            Assert.Equal(BattleServerTestHost.InternalSecret, request.Secret);

            using var json = JsonDocument.Parse(request.Body);
            var root = json.RootElement;
            Assert.Equal("m", root.GetProperty("matchId").GetString());
            Assert.Equal(ra.Ends[0].WinnerId, root.GetProperty("winnerId").GetString());
            Assert.Equal("pA", root.GetProperty("player1Id").GetString());
            Assert.Equal("pB", root.GetProperty("player2Id").GetString());
            Assert.Equal(["a1", "a2", "a3"], root.GetProperty("player1SelectedPachimon").EnumerateArray().Select(e => e.GetString()));
            var turns = root.GetProperty("turns").EnumerateArray().ToList();
            Assert.Equal(ra.Turns.Sum(t => t.Actions.Length), turns.Count);
            Assert.Equal("Switch", turns[0].GetProperty("actionData").GetProperty("type").GetString());
            Assert.Equal(1, turns[0].GetProperty("actionData").GetProperty("partySlot").GetInt32());

            // 決着後は行動も参加も受け付けない
            await AssertRpcStatus(StatusCode.FailedPrecondition, () => a.SubmitMoveAsync(new MoveRequest("19")));
            var (late, _) = await host.ConnectAsync();
            Assert.Equal(JoinResultStatus.MatchNotFound, (await late.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m")).Status);
        }

        [Fact]
        public async Task Forfeit_EndsBattleWithOpponentAsWinner()
        {
            await using var host = new BattleServerTestHost();
            var (a, ra, _, rb) = await host.StartBattleAsync("m", ["a1"], ["b1", "b2"]);

            await a.ForfeitAsync();

            await ra.WaitForAsync(r => r.Ends.Count == 1);
            await rb.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(new BattleEndPayload("pB", BattleEndReason.Forfeit), rb.Ends[0]);
            await host.ApiServer.WaitForCountAsync(1);
        }

        [Fact]
        public async Task Reconnect_RestoresBoardAndContinuesBattle()
        {
            await using var host = new BattleServerTestHost();
            var tokenA = BattleServerTestHost.Token("m", "pA");
            var (a, ra) = await host.ConnectAsync();
            var (b, rb) = await host.ConnectAsync();
            await a.JoinAsync(tokenA, "m");
            await b.JoinAsync(BattleServerTestHost.Token("m", "pB"), "m");
            await a.SubmitSelectionAsync(["a1", "a2", "a3"]);
            await b.SubmitSelectionAsync(["b1", "b2", "b3"]);
            await ra.WaitForAsync(r => r.Starts.Count == 1);

            await b.SwitchAsync(2);
            await a.SubmitMoveAsync(new MoveRequest("19"));
            await ra.WaitForAsync(r => r.Turns.Count == 1);
            int opponentHpAfterHit = ra.Turns[0].Actions.Single(x => x.PlayerId == "pA").TargetRemainingHpPercent;

            await a.DisposeAsync();
            await rb.WaitForAsync(r => r.OpponentDisconnectedCount == 1);
            // 猶予中は両者とも行動できない
            await AssertRpcStatus(StatusCode.FailedPrecondition, () => b.SubmitMoveAsync(new MoveRequest("19")));

            var (a2, ra2) = await host.ConnectAsync();
            Assert.Equal(JoinResultStatus.Success, (await a2.JoinAsync(tokenA, "m")).Status);
            await a2.SubmitSelectionAsync(["a1", "a2", "a3"]); // クライアントの自動再送は無視される
            await rb.WaitForAsync(r => r.OpponentReconnectedCount == 1);
            await ra2.WaitForAsync(r => r.Starts.Count == 1);
            Assert.Single(rb.Starts); // 再送は再接続者にだけ

            var restored = ra2.Starts[0];
            Assert.Equal(2, restored.Opponent.ActivePachimonIndex);
            Assert.True(restored.Opponent.SelectedPachimon[0].IsRevealed);
            Assert.False(restored.Opponent.SelectedPachimon[1].IsRevealed);
            Assert.True(restored.Opponent.SelectedPachimon[2].IsRevealed);
            Assert.Equal(opponentHpAfterHit, restored.Opponent.SelectedPachimon[2].State!.HpPercent);

            await a2.SubmitMoveAsync(new MoveRequest("19"));
            await b.SubmitMoveAsync(new MoveRequest("19"));
            await ra2.WaitForAsync(r => r.Turns.Count == 1);
            Assert.Equal(2, ra2.Turns[0].TurnNumber);
        }

        [Fact]
        public async Task TurnTimeout_ResolvesTurnWithSkipForIdlePlayer()
        {
            await using var host = new BattleServerTestHost(o => o.TurnTimeLimit = TimeSpan.FromMilliseconds(500));
            var (a, ra, _, _) = await host.StartBattleAsync("m", ["a1"], ["b1"]);

            await a.SubmitMoveAsync(new MoveRequest("19"));
            await ra.WaitForAsync(r => r.Turns.Count == 1);

            var actions = ra.Turns[0].Actions;
            Assert.Contains(actions, x => x.PlayerId == "pA" && x.Type == ActionType.Move);
            Assert.Contains(actions, x => x.PlayerId == "pB" && x.Type == ActionType.Skip);
        }

        [Fact]
        public async Task Reconnect_AcceptsExpiredTokenOnlyForExistingParticipant()
        {
            await using var host = new BattleServerTestHost();
            var (_, ra, b, _) = await host.StartBattleAsync("m", ["a1"], ["b1"]);

            await b.DisposeAsync();
            await ra.WaitForAsync(r => r.OpponentDisconnectedCount == 1);

            // 有効期限(30秒)は再接続猶予(60秒)より短いため、署名が正しければ期限切れでも再接続できる
            var (b2, rb2) = await host.ConnectAsync();
            Assert.Equal(JoinResultStatus.Success, (await b2.JoinAsync(BattleServerTestHost.Token("m", "pB", expOffsetSeconds: -40), "m")).Status);
            await rb2.WaitForAsync(r => r.Starts.Count == 1);

            // 対戦に参加していないプレイヤーは期限切れトークンでは入れない
            var (c, _) = await host.ConnectAsync();
            Assert.Equal(JoinResultStatus.InvalidToken, (await c.JoinAsync(BattleServerTestHost.Token("m", "pC", expOffsetSeconds: -40), "m")).Status);
        }

        [Fact]
        public async Task DisconnectTimeout_EndsBattleWithRemainingPlayerAsWinner()
        {
            await using var host = new BattleServerTestHost(o => o.ReconnectGracePeriod = TimeSpan.FromMilliseconds(500));
            var (_, ra, b, _) = await host.StartBattleAsync("m", ["a1"], ["b1"]);

            await b.DisposeAsync();

            await ra.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(new BattleEndPayload("pA", BattleEndReason.DisconnectTimeout), ra.Ends[0]);
            await host.ApiServer.WaitForCountAsync(1);
        }

        [Fact]
        public async Task OpponentNeverJoins_JoinedPlayerWinsAfterGracePeriod()
        {
            await using var host = new BattleServerTestHost(o => o.ReconnectGracePeriod = TimeSpan.FromMilliseconds(500));
            var (a, ra) = await host.ConnectAsync();
            await a.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m");
            await a.SubmitSelectionAsync(["a1"]);

            await ra.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(new BattleEndPayload("pA", BattleEndReason.DisconnectTimeout), ra.Ends[0]);

            await host.ApiServer.WaitForCountAsync(1);
            Assert.True(host.ApiServer.Received.TryPeek(out var request));
            using var json = JsonDocument.Parse(request.Body);
            Assert.Equal("pA", json.RootElement.GetProperty("player1Id").GetString());
            Assert.Equal("", json.RootElement.GetProperty("player2Id").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("player2SelectedPachimon").GetArrayLength());
        }

        [Fact]
        public async Task SelectionTimeout_PlayerWhoDidNotSelectLoses()
        {
            await using var host = new BattleServerTestHost(o => o.SelectionTimeLimit = TimeSpan.FromMilliseconds(500));
            var (a, ra) = await host.ConnectAsync();
            var (b, rb) = await host.ConnectAsync();
            await a.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m");
            await b.JoinAsync(BattleServerTestHost.Token("m", "pB"), "m");
            await a.SubmitSelectionAsync(["a1"]);

            await rb.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(new BattleEndPayload("pA", BattleEndReason.Forfeit), rb.Ends[0]);
            Assert.Empty(ra.Starts);
            await host.ApiServer.WaitForCountAsync(1);
        }

        [Fact]
        public async Task SelectionTimeout_NeitherSelected_EndsWithoutWinner()
        {
            await using var host = new BattleServerTestHost(o => o.SelectionTimeLimit = TimeSpan.FromMilliseconds(500));
            var (a, ra) = await host.ConnectAsync();
            var (b, rb) = await host.ConnectAsync();
            await a.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m");
            await b.JoinAsync(BattleServerTestHost.Token("m", "pB"), "m");

            await ra.WaitForAsync(r => r.Ends.Count == 1);
            await rb.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(new BattleEndPayload("", BattleEndReason.Forfeit), ra.Ends[0]);
            await AssertReportedWithoutWinnerAsync(host);
        }

        [Fact]
        public async Task IdleForThreeTurns_Loses()
        {
            await using var host = new BattleServerTestHost(o => o.TurnTimeLimit = TimeSpan.FromMilliseconds(300));
            var (a, ra, _, _) = await host.StartBattleAsync("m", ["a1"], ["b1"]);

            // Aだけ毎ターン行動し、Bは放置する(ダミーのステータスでは3発では倒れない)
            for (int turn = 1; turn <= 3; turn++)
            {
                await a.SubmitMoveAsync(new MoveRequest("19"));
                await ra.WaitForAsync(r => r.Turns.Count == turn);
            }

            await ra.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(new BattleEndPayload("pA", BattleEndReason.Forfeit), ra.Ends[0]);
            Assert.All(ra.Turns, t => Assert.Contains(t.Actions, x => x.PlayerId == "pB" && x.Type == ActionType.Skip));
            await host.ApiServer.WaitForCountAsync(1);
        }

        [Fact]
        public async Task IdleCounter_ResetsWhenPlayerActs()
        {
            await using var host = new BattleServerTestHost(o => o.TurnTimeLimit = TimeSpan.FromMilliseconds(300));
            var (a, ra, b, _) = await host.StartBattleAsync("m", ["a1", "a2"], ["b1", "b2"]);

            // Bは2ターン放置→1ターン行動→2ターン放置。連続3ターンには達しないので決着しない。
            // ダメージで決着しないよう、行動は交代のみにする
            int aActive = 0;
            foreach (bool bActs in new[] { false, false, true, false, false })
            {
                int before = ra.Turns.Count;
                aActive = 1 - aActive;
                await a.SwitchAsync(aActive);
                if (bActs)
                {
                    await b.SwitchAsync(1);
                }

                await ra.WaitForAsync(r => r.Turns.Count > before);
            }

            Assert.Empty(ra.Ends);
        }

        [Fact]
        public async Task BothIdle_EndsWithoutWinner()
        {
            await using var host = new BattleServerTestHost(o => o.TurnTimeLimit = TimeSpan.FromMilliseconds(200));
            var (_, ra, _, rb) = await host.StartBattleAsync("m", ["a1"], ["b1"]);

            await ra.WaitForAsync(r => r.Ends.Count == 1);
            await rb.WaitForAsync(r => r.Ends.Count == 1);
            Assert.Equal(3, ra.Turns.Count);
            Assert.Equal(new BattleEndPayload("", BattleEndReason.Forfeit), ra.Ends[0]);
            await AssertReportedWithoutWinnerAsync(host);
        }

        // 勝者なしでもRust側でbattle_matchesを片付けられるよう、winnerIdを空文字にして報告する。
        private static async Task AssertReportedWithoutWinnerAsync(BattleServerTestHost host)
        {
            await host.ApiServer.WaitForCountAsync(1);
            Assert.True(host.ApiServer.Received.TryPeek(out var request));
            using var json = JsonDocument.Parse(request.Body);
            Assert.Equal("", json.RootElement.GetProperty("winnerId").GetString());
        }

        private static async Task AssertRpcStatus(StatusCode expected, Func<Task> action)
        {
            var e = await Assert.ThrowsAsync<RpcException>(action);
            Assert.Equal(expected, e.StatusCode);
        }
    }
}
