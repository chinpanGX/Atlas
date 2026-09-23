using System;
using System.Collections.Generic;

namespace Atlas.Presentation.PartyEdit
{
    public sealed class PartyEditViewDto
    {
        // 編成済みのスロットのみを持つ(未編成のslotは要素自体が無い)。
        public IReadOnlyList<PartyEditSlotDto> Slots = Array.Empty<PartyEditSlotDto>();
    }
}
