using System;
using System.Collections.Generic;
using System.Linq;

namespace Atlas.BattleCore
{
    public enum BattleSideId
    {
        Player1,
        Player2,
    }

    // 選出3体分の状態を保持する片側プレイヤーの盤面。
    public sealed class BattleSide
    {
        public IReadOnlyList<PachimonState> Party { get; }
        public int ActiveIndex { get; private set; }

        // 前ターンで瀕死になり、次ターンの行動がSwitch以外受け付けられない状態かどうか。
        public bool RequiresForcedSwitch { get; internal set; }

        public PachimonState Active => Party[ActiveIndex];
        public bool AllFainted => Party.All(p => p.IsFainted);

        public BattleSide(IReadOnlyList<PachimonState> party, int activeIndex = 0)
        {
            if (party.Count == 0)
            {
                throw new ArgumentException("選出3体が空のパーティではバトルを開始できません。", nameof(party));
            }

            Party = party;
            ActiveIndex = activeIndex;
        }

        internal void SwitchTo(int partySlot)
        {
            if (partySlot < 0 || partySlot >= Party.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(partySlot));
            }

            if (Party[partySlot].IsFainted)
            {
                throw new ArgumentException("瀕死のパチモンには交代できません。", nameof(partySlot));
            }

            ActiveIndex = partySlot;
            RequiresForcedSwitch = false;
        }
    }

    // 両プレイヤー分の盤面をまとめて保持する、ターンを跨いで生き続ける対戦状態。
    public sealed class BattleState
    {
        public BattleSide Player1 { get; }
        public BattleSide Player2 { get; }
        public int TurnNumber { get; private set; }

        public BattleState(BattleSide player1, BattleSide player2)
        {
            Player1 = player1;
            Player2 = player2;
        }

        public BattleSide GetSide(BattleSideId side) => side == BattleSideId.Player1 ? Player1 : Player2;

        public BattleSideId GetOpponentSideId(BattleSideId side) =>
            side == BattleSideId.Player1 ? BattleSideId.Player2 : BattleSideId.Player1;

        internal int AdvanceTurn() => ++TurnNumber;
    }
}
