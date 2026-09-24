using System.Collections.Generic;
using Atlas.Application;
using ZLinq;

namespace Atlas.Presentation.Party
{
    public enum PartyTapResult
    {
        // 1体目として選択した(編成外同士をタップした時の選択の移動も含む)。
        Selected,
        // 選択中のものをもう一度タップして選択を解除した。
        Deselected,
        // 2体目のタップで編成が変わった(入れ替え・編成への追加・外す)。
        Changed,
        // 最後の1体を外す操作だったため、何もしなかった(サーバーも空の編成は400で拒否する)。
        BlockedLastMember,
    }

    // パーティ編成画面の編集中の状態と、タップによる入れ替えルール。保存(戻る)までは通信せず、
    // ここで持つ編成だけを書き換える。
    //
    // 1回目のタップで選択し、2回目のタップで1体目と2体目の「位置」(編成の枠番号、または編成外)を
    // 入れ替える。例外として、枠のパチモンと一覧の同じパチモンの組み合わせはその枠から外す。
    // 空いた枠は詰めずにそのまま残す。
    public sealed class PartyEditor
    {
        public const int MaxSlots = 6;

        // 枠番号(1始まり) → PlayerPachimonId。未編成の枠はキー自体が無い。
        private readonly Dictionary<int, string> slots;
        private readonly Dictionary<int, string> initialSlots;

        private PartyTapTarget? selected;

        public PartyEditor(IEnumerable<PartySlotInput> initial)
        {
            slots = initial.ToDictionary(s => s.Slot, s => s.PlayerPachimonId);
            initialSlots = new Dictionary<int, string>(slots);
        }

        // 選択中の枠番号。枠を選択していなければnull。
        public int? SelectedSlot => selected is { IsSlot: true } target ? target.Slot : null;

        // 一覧で選択中のPlayerPachimonId。一覧を選択していなければnull。
        public string SelectedPachimonId => selected is { IsSlot: false } target ? target.PlayerPachimonId : null;

        public bool IsDirty =>
            slots.Count != initialSlots.Count
            || slots.Any(pair => !initialSlots.TryGetValue(pair.Key, out var id) || id != pair.Value);

        public int MemberCount => slots.Count;

        // 未編成ならnull。
        public string GetPachimonAt(int slot)
        {
            return slots.TryGetValue(slot, out var id) ? id : null;
        }

        // 編成外ならnull。
        public int? FindSlot(string playerPachimonId)
        {
            foreach (var pair in slots)
            {
                if (pair.Value == playerPachimonId)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        public bool IsInParty(string playerPachimonId)
        {
            return FindSlot(playerPachimonId) is not null;
        }

        public PartyTapResult TapSlot(int slot)
        {
            return Tap(PartyTapTarget.ForSlot(slot));
        }

        public PartyTapResult TapPachimon(string playerPachimonId)
        {
            return Tap(PartyTapTarget.ForPachimon(playerPachimonId));
        }

        public IReadOnlyList<PartySlotInput> ToInputs()
        {
            return slots.OrderBy(pair => pair.Key).Select(pair => new PartySlotInput(pair.Key, pair.Value)).ToList();
        }

        private PartyTapResult Tap(PartyTapTarget tapped)
        {
            if (selected is not { } first)
            {
                selected = tapped;
                return PartyTapResult.Selected;
            }

            if (first.Equals(tapped))
            {
                selected = null;
                return PartyTapResult.Deselected;
            }

            // 編成外同士は入れ替える位置が無いため、選択を移すだけ。
            var firstSlot = ResolveSlot(first);
            var tappedSlot = ResolveSlot(tapped);
            if (firstSlot is null && tappedSlot is null)
            {
                selected = tapped;
                return PartyTapResult.Selected;
            }

            selected = null;

            // 枠とその枠にいるパチモン(一覧側)の組み合わせは「外す」。
            if (firstSlot == tappedSlot)
            {
                return RemoveFromParty(firstSlot.Value);
            }

            // ここから先は、少なくとも片方が枠(firstSlot/tappedSlotのどちらかが非null)。
            if (firstSlot is not null && tappedSlot is not null)
            {
                SwapSlots(firstSlot.Value, tappedSlot.Value);
                return PartyTapResult.Changed;
            }

            // 片方が枠、もう片方が編成外のパチモン(ResolveSlotがnullになるのは編成外のパチモンだけ):
            // 編成外のパチモンがその枠に入り、枠にいたパチモンは編成外になる。人数は減らない。
            var slot = (firstSlot ?? tappedSlot).Value;
            var outsider = firstSlot is null ? first : tapped;
            slots[slot] = outsider.PlayerPachimonId;
            return PartyTapResult.Changed;
        }

        // 枠ならその枠番号(空欄でも枠番号を返す)、パチモンなら編成中の枠番号(編成外ならnull)。
        private int? ResolveSlot(PartyTapTarget target)
        {
            return target.IsSlot ? target.Slot : FindSlot(target.PlayerPachimonId);
        }

        private PartyTapResult RemoveFromParty(int slot)
        {
            if (!slots.ContainsKey(slot))
            {
                return PartyTapResult.Deselected;
            }

            if (slots.Count <= 1)
            {
                return PartyTapResult.BlockedLastMember;
            }

            slots.Remove(slot);
            return PartyTapResult.Changed;
        }

        private void SwapSlots(int a, int b)
        {
            var hasA = slots.TryGetValue(a, out var idA);
            var hasB = slots.TryGetValue(b, out var idB);
            slots.Remove(a);
            slots.Remove(b);
            if (hasA)
            {
                slots[b] = idA;
            }
            if (hasB)
            {
                slots[a] = idB;
            }
        }

        private readonly struct PartyTapTarget : System.IEquatable<PartyTapTarget>
        {
            public readonly bool IsSlot;
            public readonly int Slot;
            public readonly string PlayerPachimonId;

            private PartyTapTarget(bool isSlot, int slot, string playerPachimonId)
            {
                IsSlot = isSlot;
                Slot = slot;
                PlayerPachimonId = playerPachimonId;
            }

            public static PartyTapTarget ForSlot(int slot) => new(true, slot, null);

            public static PartyTapTarget ForPachimon(string playerPachimonId) => new(false, 0, playerPachimonId);

            public bool Equals(PartyTapTarget other) =>
                IsSlot == other.IsSlot && Slot == other.Slot && PlayerPachimonId == other.PlayerPachimonId;

            public override bool Equals(object obj) => obj is PartyTapTarget other && Equals(other);

            public override int GetHashCode() => IsSlot ? Slot : PlayerPachimonId?.GetHashCode() ?? 0;
        }
    }
}
