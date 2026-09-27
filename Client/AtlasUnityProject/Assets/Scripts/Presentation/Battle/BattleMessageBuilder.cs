using Atlas.Domain;

namespace Atlas.Presentation.Battle
{
    // BattleMessageViewに流す文言。相手側のパチモンには「相手の」を付ける。
    public static class BattleMessageBuilder
    {
        public const string WaitingForOpponent = "相手の 行動を 待っています…";
        public const string WaitingForOpponentSelection = "相手の 選出を 待っています…";
        public const string Missed = "しかし 攻撃は はずれた!";
        public const string Critical = "急所に 当たった!";

        public static string MoveUsed(bool isSelf, string pachimonName, string moveName)
        {
            return $"{Owner(isSelf)}{pachimonName}の {moveName}!";
        }

        // 効果が普通の場合は出す文が無いためnull。
        public static string Effectiveness(EffectivenessResult effectiveness, bool targetIsSelf, string targetName)
        {
            return effectiveness switch
            {
                EffectivenessResult.SuperEffective => "効果は バツグンだ!",
                EffectivenessResult.NotVeryEffective => "効果は いまひとつのようだ…",
                EffectivenessResult.Immune => $"{Owner(targetIsSelf)}{targetName}には 効果が ないようだ…",
                _ => null,
            };
        }

        public static string Fainted(bool isSelf, string pachimonName)
        {
            return $"{Owner(isSelf)}{pachimonName}は たおれた!";
        }

        public static string SentOut(bool isSelf, string pachimonName)
        {
            return isSelf ? $"ゆけっ! {pachimonName}!" : $"相手は {pachimonName}を くりだした!";
        }

        public static string CouldNotAct(bool isSelf, string pachimonName)
        {
            return $"{Owner(isSelf)}{pachimonName}は 行動できなかった!";
        }

        private static string Owner(bool isSelf) => isSelf ? string.Empty : "相手の ";
    }
}
