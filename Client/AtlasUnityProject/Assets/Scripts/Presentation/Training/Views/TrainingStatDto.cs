namespace Atlas.Presentation.Training
{
    // ステータスタブの能力1つ分。確定済みの割り振りと、画面の中で調整した後の割り振りを並べて出すための値。
    public sealed class TrainingStatDto
    {
        public string Label;
        // 調整後のポイント。
        public int Points;
        // 確定済みのポイントからの変化。増やした分は正、減らした分は負。
        public int Change;
        public int CommittedValue;
        public int AdjustedValue;
        // ポイントのゲージの長さ(0〜1)。
        public float PointsRatio;
        public bool CanDecrease;
        public bool CanIncrease;
    }
}
