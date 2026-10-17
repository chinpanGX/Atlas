using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Training
{
    public sealed class MoveReplaceViewDto
    {
        public string PachimonName;
        // 入れ替える枠の、いまの技の名前。
        public string CurrentMoveName;
        public IReadOnlyList<MoveCandidateDto> Candidates = Array.Empty<MoveCandidateDto>();
        public int TicketsBefore;
        public int TicketCost;
    }
}
