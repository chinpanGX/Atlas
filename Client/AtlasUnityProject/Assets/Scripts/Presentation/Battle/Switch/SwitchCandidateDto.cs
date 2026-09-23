using Atlas.Presentation.Party;

namespace Atlas.Presentation.Battle
{
    // 交代先の候補(選出3体の1枠分)。
    public sealed class SwitchCandidateDto
    {
        // 選出3体の中での位置(0始まり)。IBattleConnection.SwitchAsyncにそのまま渡す。
        public int PartySlot;
        public string Name;
        public int CurrentHp;
        public int MaxHp;
        // 場に出ているパチモン。自分自身への交代になるため選べない。
        public bool IsActive;
        public bool IsFainted;
        // 中央の詳細パネル(パーティ編成画面と同じPachimonInfoView)に出す内容。技の残りPPは対戦中の値。
        public PachimonInfoDto Info;

        public bool CanSwitchTo => !IsActive && !IsFainted;
    }
}
