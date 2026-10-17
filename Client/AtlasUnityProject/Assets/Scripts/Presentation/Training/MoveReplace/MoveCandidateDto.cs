using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Training
{
    // 付け替える技の候補1つ。
    public sealed class MoveCandidateDto
    {
        public int MoveId;
        public string Name;
        public PachimonType Type;
        public string CategoryName;
        public int BasePower;
        public int Accuracy;
        public int MaxPp;
        // 選べない理由(「いまの技」「覚えている」)。選べる技はnull。
        public string DisabledReason;
    }
}
