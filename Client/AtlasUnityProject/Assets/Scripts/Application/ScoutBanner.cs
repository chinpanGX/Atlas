namespace Atlas.Application
{
    public sealed class ScoutBanner
    {
        public readonly string BannerId;
        public readonly string Name;
        public readonly int CostPerRoll;

        public ScoutBanner(string bannerId, string name, int costPerRoll)
        {
            BannerId = bannerId;
            Name = name;
            CostPerRoll = costPerRoll;
        }
    }
}
