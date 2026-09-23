namespace Atlas.Domain
{
    // party_slotsの1行。パーティのslot番目に編成した所持パチモン(playerPachimonId)。
    public class PartyEntity
    {
        public readonly string PartySlotId;
        public readonly string PlayerPachimonId;
        public readonly int Slot;

        public PartyEntity(string partySlotId, string playerPachimonId, int slot)
        {
            PartySlotId = partySlotId;
            PlayerPachimonId = playerPachimonId;
            Slot = slot;
        }
    }
}
