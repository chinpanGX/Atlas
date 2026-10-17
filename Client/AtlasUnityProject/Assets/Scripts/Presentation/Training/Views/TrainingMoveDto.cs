using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Training
{
    public sealed class TrainingMoveDto
    {
        // 1始まり(技スロット1〜4)。
        public int Slot;
        public string Name;
        public PachimonType Type;
        public string CategoryName;
        public int BasePower;
        public int Accuracy;
        public int MaxPp;
    }
}
