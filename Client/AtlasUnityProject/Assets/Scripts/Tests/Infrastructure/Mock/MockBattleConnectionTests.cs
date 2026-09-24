using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Domain;
using Atlas.MasterData;
using MasterMemory;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Infrastructure.Mock.Tests
{
    public sealed class MockBattleConnectionTests
    {
        private const int MaxTurns = 100;

        private MemoryDatabase database;

        [SetUp]
        public void SetUp()
        {
            var path = Path.Combine(Application.dataPath, "Addressables/MasterData/masterdata.bytes");
            database = MasterDataLoader.Load(File.ReadAllBytes(path));
        }

        [Test]
        public async Task Battle_RunsToCompletionWithinTurnLimit()
        {
            var selfIds = new[] { 1001, 1002, 1003 };
            var selfParty = TestPartyFactory.BuildParty(database, selfIds);
            var driver = new SelfDriver(selfParty);

            var connection = new MockBattleConnection(database);

            string selfPlayerId = null;
            string opponentPlayerId = null;
            TurnResultPayload lastTurn = null;
            var turnResults = new List<TurnResultPayload>();
            BattleEndPayload battleEnd = null;

            connection.OnMatchStart += payload =>
            {
                selfPlayerId = payload.Self.PlayerId;
                opponentPlayerId = payload.Opponent.PlayerId;
            };
            connection.OnTurnResult += payload =>
            {
                driver.ApplyTurn(selfPlayerId, opponentPlayerId, payload);
                lastTurn = payload;
                turnResults.Add(payload);
            };
            connection.OnBattleEnd += payload => battleEnd = payload;

            var joinResult = await connection.JoinAsync("dummy-token", "dummy-match");
            Assert.AreEqual(JoinResultStatus.Success, joinResult.Status);

            await connection.SubmitSelectionAsync(selfIds.Select(id => id.ToString()).ToArray());
            Assert.IsNotNull(selfPlayerId, "OnMatchStartが発火していない");

            var turns = 0;
            while (battleEnd == null && turns < MaxTurns)
            {
                // 簡易AIの強制交代はプレイヤーの行動を待たずに続けて処理されるため、送信時点で相手待ちにはならない。
                Assert.IsFalse(lastTurn != null && lastTurn.PlayersRequiringForcedSwitch.Contains(opponentPlayerId),
                    "簡易AIの強制交代ターンが処理されずに残っています。");

                if (lastTurn != null && lastTurn.PlayersRequiringForcedSwitch.Contains(selfPlayerId))
                {
                    await connection.SwitchAsync(driver.NextAliveSlot());
                }
                else
                {
                    await connection.SubmitMoveAsync(new MoveRequest(driver.NextMoveId()));
                }

                turns++;
            }

            Assert.IsNotNull(battleEnd, $"{MaxTurns}ターン以内にバトルが終了しませんでした。");
            Assert.IsTrue(battleEnd.WinnerId == selfPlayerId || battleEnd.WinnerId == opponentPlayerId);

            // 強制交代ターンは、倒れた側の交代だけが処理され、もう一方は行動しない(Skip)。
            var forcedSwitchTurns = 0;
            for (var i = 1; i < turnResults.Count; i++)
            {
                var switching = turnResults[i - 1].PlayersRequiringForcedSwitch;
                if (switching.Length == 0)
                {
                    continue;
                }

                forcedSwitchTurns++;
                foreach (var action in turnResults[i].Actions)
                {
                    var expected = switching.Contains(action.PlayerId) ? ActionType.Switch : ActionType.Skip;
                    Assert.AreEqual(expected, action.Type, $"ターン{turnResults[i].TurnNumber}: {action.PlayerId}");
                }
            }

            Assert.Greater(forcedSwitchTurns, 0, "強制交代ターンが一度も発生しませんでした。");
        }

        // テスト用の簡易「自分側」進行役。OnTurnResultのActionResultだけから自パーティの
        // 生死・アクティブ枠を追跡し、次に送るMoveId/交代先を決める。design/battle.md
        // 「PP制限(UI側)」等と同様、Clientは自分のパチモンの技一覧を静的マスタから
        // 把握できる前提(TestPartyFactoryで組み立てたPartyMemberをそのまま使う)。
        private sealed class SelfDriver
        {
            private readonly List<int> partySlotToPachimonId;
            private readonly Dictionary<int, IReadOnlyList<string>> moveIdsByPachimonId;
            private readonly bool[] fainted;
            private int activeIndex;

            public SelfDriver(IReadOnlyList<PartyMember> party)
            {
                partySlotToPachimonId = party.Select(m => m.PachimonId).ToList();
                moveIdsByPachimonId = party.ToDictionary(m => m.PachimonId, m => m.MoveIds);
                fainted = new bool[party.Count];
                activeIndex = 0;
            }

            public string NextMoveId() => moveIdsByPachimonId[partySlotToPachimonId[activeIndex]][0];

            public int NextAliveSlot()
            {
                for (var i = 0; i < fainted.Length; i++)
                {
                    if (!fainted[i])
                    {
                        return i;
                    }
                }

                throw new InvalidOperationException("自パーティが全滅しています。");
            }

            public void ApplyTurn(string selfPlayerId, string opponentPlayerId, TurnResultPayload payload)
            {
                foreach (var action in payload.Actions)
                {
                    if (action.PlayerId == opponentPlayerId && action.TargetFainted)
                    {
                        fainted[activeIndex] = true;
                    }
                    else if (action.PlayerId == selfPlayerId && action.Type == ActionType.Switch
                        && action.NewActiveIndex is { } newIndex)
                    {
                        activeIndex = newIndex;
                    }
                }
            }
        }
    }
}
