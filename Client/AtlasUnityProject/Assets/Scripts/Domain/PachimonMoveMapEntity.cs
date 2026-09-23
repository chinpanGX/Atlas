namespace Atlas.Domain
{
    // player_pachimon_movesの1行。所持パチモン(playerPachimonId)のslot番目に割り当てた技(moveId)。
    public class PachimonMoveMapEntity
    {
        public readonly string PlayerPachimonMoveId;
        public readonly string PlayerPachimonId;
        public readonly int Slot;
        public readonly long MoveId;

        public PachimonMoveMapEntity(string playerPachimonMoveId, string playerPachimonId, int slot, long moveId)
        {
            PlayerPachimonMoveId = playerPachimonMoveId;
            PlayerPachimonId = playerPachimonId;
            Slot = slot;
            MoveId = moveId;
        }
    }
}
