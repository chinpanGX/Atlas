namespace Atlas.Presentation.GlobalHeader
{
    public sealed class GlobalHeaderViewDto
    {
        // ヘッダーを使う画面が無い(タイトル)ときは出さない。
        public bool IsVisible;
        public string Title;
        public int Gems;
        public int Tickets;
        public bool ShowsBack;
        public bool CanOpenShop;
        public bool IsInteractable;
    }
}
