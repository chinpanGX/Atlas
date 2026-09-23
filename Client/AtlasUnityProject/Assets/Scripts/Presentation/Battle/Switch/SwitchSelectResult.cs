namespace Atlas.Presentation.Battle
{
    // SwitchSelectModalのPop結果。やめた場合、およびターンの進行や決着でPush元が閉じた場合はCanceled。
    public sealed class SwitchSelectResult
    {
        public static readonly SwitchSelectResult Canceled = new(isCanceled: true, partySlot: -1);

        public readonly bool IsCanceled;
        // 選ばれた交代先(選出3体の中での位置、0始まり)。IsCanceledの時は使わない。
        public readonly int PartySlot;

        private SwitchSelectResult(bool isCanceled, int partySlot)
        {
            IsCanceled = isCanceled;
            PartySlot = partySlot;
        }

        public static SwitchSelectResult Selected(int partySlot) => new(isCanceled: false, partySlot);
    }
}
