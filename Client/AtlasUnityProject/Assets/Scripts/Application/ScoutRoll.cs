using System.Collections.Generic;

namespace Atlas.Application
{
    public sealed class ScoutRoll
    {
        public readonly string RollId;
        public readonly IReadOnlyList<ScoutCandidate> Candidates;

        public ScoutRoll(string rollId, IReadOnlyList<ScoutCandidate> candidates)
        {
            RollId = rollId;
            Candidates = candidates;
        }
    }
}
