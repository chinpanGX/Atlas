using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    public sealed class SelectionViewDto
    {
        // 選出の残り時間(秒)。Modal側で1秒ごとに減らして表示する(時間切れの判定はサーバーが行う)。
        public int RemainingSeconds;
        // 選ぶ数。パーティがMaxSelectionCount体未満ならパーティの体数。
        public int SelectionCount;
        // 中央に出す自分のパーティ(枠番号順)。
        public IReadOnlyList<SelectionCandidateDto> SelfParty = Array.Empty<SelectionCandidateDto>();
        // 右側に出す相手のパーティ(枠番号順)。どのパチモンかだけ分かる。
        public IReadOnlyList<SelectionOpponentDto> OpponentParty = Array.Empty<SelectionOpponentDto>();
    }
}
