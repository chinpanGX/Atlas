namespace Atlas.Domain
{
    // 所持パチモンが6つの能力に割り振ったステータスポイント。どれも0以上で、合計はMaxTotal以下。
    public sealed class StatPoints
    {
        public const int MaxTotal = 64;

        public static readonly StatPoints Zero = new(0, 0, 0, 0, 0, 0);

        public readonly int Hp;
        public readonly int Atk;
        public readonly int Def;
        public readonly int Spatk;
        public readonly int Spdef;
        public readonly int Speed;

        public StatPoints(int hp, int atk, int def, int spatk, int spdef, int speed)
        {
            Hp = hp;
            Atk = atk;
            Def = def;
            Spatk = spatk;
            Spdef = spdef;
            Speed = speed;
        }
    }
}
