namespace Atlas.Presentation.Battle
{
    // Push時に渡す選出3体のID(暫定: player_pachimonが未実装のため、マスターデータの
    // PachimonIdを文字列化したものをそのまま使う。design/battle.md「Stage 1」参照)。
    public sealed class BattleViewDto
    {
        public string[] SelfPachimonIds;
    }
}
