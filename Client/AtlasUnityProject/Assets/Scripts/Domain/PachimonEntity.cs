namespace Atlas.Domain
{
    // player_pachimonの1行。所持している個体(playerPachimonId)とそのマスタ(pachimonId)の対応。
    public class PachimonEntity
    {
        public readonly string PlayerPachimonId;
        public readonly long PachimonId;

        public PachimonEntity(string playerPachimonId, long pachimonId)
        {
            PlayerPachimonId = playerPachimonId;
            PachimonId = pachimonId;
        }
    }
}
