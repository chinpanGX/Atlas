namespace Atlas.Presentation.Training
{
    public sealed class TicketShortageViewDto
    {
        // 「技の付け替え」「ステータスポイントの割り振り」のような、チケットを使おうとした操作の名前。
        public string Purpose;
        public int Owned;
        public int Required;
    }
}
