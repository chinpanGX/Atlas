using System;
using System.Collections.Generic;
using Atlas.Presentation.Party;

namespace Atlas.Presentation.Battle
{
    // 選出候補(自分のパーティの1体)。
    public sealed class SelectionCandidateDto
    {
        // IBattleConnection.SubmitSelectionAsyncにそのまま渡す。
        public string PlayerPachimonId;
        public string Name;
        public IReadOnlyList<string> TypeNames = Array.Empty<string>();
        // 中央の詳細パネル(パーティ編成画面と同じPachimonInfoView)に出す内容。
        public PachimonInfoDto Info;
    }
}
