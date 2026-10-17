using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Training
{
    // 画面右の上に出す、選んでいるパチモンの名前・サムネイル・タイプ。
    public sealed class TrainingPachimonDto
    {
        public string Name;
        public long PachimonId;
        public PachimonType PrimaryType;
        // 単タイプならNone。
        public PachimonType SecondaryType;
    }
}
