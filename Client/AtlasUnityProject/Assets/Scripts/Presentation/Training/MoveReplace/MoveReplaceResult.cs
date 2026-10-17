namespace Atlas.Presentation.Training
{
    // 「付け替える」で選んだ技。「やめる」で閉じたときは結果自体がnullになる。
    public sealed class MoveReplaceResult
    {
        public readonly int MoveId;

        public MoveReplaceResult(int moveId)
        {
            MoveId = moveId;
        }
    }
}
