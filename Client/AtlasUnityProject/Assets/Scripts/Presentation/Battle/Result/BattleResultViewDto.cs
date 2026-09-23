namespace Atlas.Presentation.Battle
{
    // BattlePresenterがBattleEndPayloadから表示用の文言を組み立ててPush時に渡す。
    public sealed class BattleResultViewDto
    {
        public string ResultText;
        public string ReasonText;
    }
}
