using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Battle
{
    public sealed record PachimonFigureDto
    {
        // サムネイルを引くマスタのpachimon_id。相手の未公開のパチモンはnull(サムネイルを出さない)。
        public long? PachimonId;
        // サムネイルが読み込めるまで(または無いとき)に出す円の色に使う主タイプ。相手の未公開のパチモンはNone。
        public PachimonType PrimaryType;
        public bool IsFainted;
    }
}
