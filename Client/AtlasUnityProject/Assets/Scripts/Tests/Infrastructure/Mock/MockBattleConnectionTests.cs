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
            };
            connection.OnBattleEnd += payload => battleEnd = payload;

            var joinResult = await connection.JoinAsync("dummy-token", "dummy-match");
            Assert.AreEqual(JoinResultStatus.Success, joinResult.Status);

            await connection.SubmitSelectionAsync(selfIds.Select(id => id.ToString()).ToArray());
            Assert.IsNotNull(selfPlayerId, "OnMatchStartが発火していない");

            var turns = 0;
            while (battleEnd == null && turns < MaxTurns)
            {
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
