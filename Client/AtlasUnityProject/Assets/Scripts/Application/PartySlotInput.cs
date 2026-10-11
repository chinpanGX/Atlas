namespace Atlas.Application
{
    // パーティ編成の保存で送る1枠分。slotは1始まり。
    public readonly struct PartySlotInput
    {
        public readonly int Slot;
        public readonly string PlayerPachimonId;

        public PartySlotInput(int slot, string playerPachimonId)
        {
            Slot = slot;
            PlayerPachimonId = playerPachimonId;
        }
    }
}
