using System.Collections.Generic;

namespace Atlas.Domain
{
    // パーティ編成(party_slots)専用のリポジトリ。
    public interface IPartyRepository
    {
        // 編成済みのスロットをslot昇順で返す。
        IReadOnlyList<PartyEntity> GetAll();

        void Upsert(PartyEntity partyEntity);

        void Delete(string partySlotId);
    }
}
