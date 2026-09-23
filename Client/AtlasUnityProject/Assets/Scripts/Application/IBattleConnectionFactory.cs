using Atlas.Domain;

namespace Atlas.Application
{
    // 対戦ごとにIBattleConnectionを作る。BattleServerへの接続は1対戦で閉じるため、Root(常駐)で1つを
    // 使い回さず、BattleシーンのLifetimeScopeがマッチング結果から都度生成し、シーン終了時に破棄する。
    public interface IBattleConnectionFactory
    {
        IBattleConnection Create(BattleMatch match);
    }
}
