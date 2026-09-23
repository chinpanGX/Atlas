using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure.Realtime
{
    public sealed class RealtimeBattleConnectionFactory : IBattleConnectionFactory
    {
        public IBattleConnection Create(BattleMatch match)
        {
            return new RealtimeBattleConnection(match.BattleServerUrl);
        }
    }
}
