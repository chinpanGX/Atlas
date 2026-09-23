using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Party
{
    public sealed class PartyDto
    {
        // 編成済みのスロットのみを持つ(未編成のslotは要素自体が無い)。
        public IReadOnlyList<PartySlotDto> Slots = Array.Empty<PartySlotDto>();
    }
}
