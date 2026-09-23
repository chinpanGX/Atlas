using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    public sealed class SwitchSelectViewDto
    {
        // 瀕死による強制交代。やめるボタンを出さず、必ずどれかを選ばせる。
        public bool IsForced;
        public IReadOnlyList<SwitchCandidateDto> Candidates = Array.Empty<SwitchCandidateDto>();
    }
}
