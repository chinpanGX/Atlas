using System.Collections.Generic;

namespace Atlas.Application
{
    // 紹介された候補1体。技は入手時にそのまま引き継がれる(選択時に再抽選しない)。
    public sealed class ScoutCandidate
    {
        // SelectAsyncに渡す値(0〜9)。
        public readonly int Index;
        public readonly int PachimonId;
        // 要素番号が技スロット-1に対応する。
        public readonly IReadOnlyList<int> MoveIds;

        public ScoutCandidate(int index, int pachimonId, IReadOnlyList<int> moveIds)
        {
            Index = index;
            PachimonId = pachimonId;
            MoveIds = moveIds;
        }
    }
}
