namespace Atlas.BattleCore
{
    public enum ActionKind
    {
        Move,
        Switch,
    }

    // BattleEngine.ProcessTurnへ渡す1プレイヤー分の行動。nullは非行動(タイムアウト等)を表す。
    public readonly struct PlayerAction
    {
        public ActionKind Kind { get; }

        // Kind == Moveの場合のみ有効。対象パチモンのMoves配列内のインデックス。
        public int MoveIndex { get; }

        // Kind == Switchの場合のみ有効。交代先のパーティ内インデックス。
        public int PartySlot { get; }

        private PlayerAction(ActionKind kind, int moveIndex, int partySlot)
        {
            Kind = kind;
            MoveIndex = moveIndex;
            PartySlot = partySlot;
        }

        public static PlayerAction UseMove(int moveIndex) => new(ActionKind.Move, moveIndex, -1);

        public static PlayerAction SwitchTo(int partySlot) => new(ActionKind.Switch, -1, partySlot);
    }
}
