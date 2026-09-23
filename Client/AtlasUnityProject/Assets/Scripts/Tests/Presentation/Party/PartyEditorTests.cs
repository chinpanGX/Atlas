using System.Linq;
using Atlas.Application;
using Atlas.Presentation.Party;
using NUnit.Framework;

namespace Atlas.Tests.Presentation.Party
{
    public sealed class PartyEditorTests
    {
        // slot1:A, slot2:B, slot3:C が編成済み、X/Yは編成外の所持パチモン。
        private static PartyEditor CreateEditor()
        {
            return new PartyEditor(new[]
            {
                new PartySlotInput(1, "A"),
                new PartySlotInput(2, "B"),
                new PartySlotInput(3, "C"),
            });
        }

        private static string Dump(PartyEditor editor)
        {
            return string.Join(",", editor.ToInputs().Select(i => $"{i.Slot}:{i.PlayerPachimonId}"));
        }

        [Test]
        public void TapSlot_ThenAnotherSlot_SwapsAndClearsSelection()
        {
            var editor = CreateEditor();

            Assert.AreEqual(PartyTapResult.Selected, editor.TapSlot(1));
            Assert.AreEqual(1, editor.SelectedSlot);
            Assert.AreEqual(PartyTapResult.Changed, editor.TapSlot(2));
            Assert.AreEqual("1:B,2:A,3:C", Dump(editor));
            Assert.IsNull(editor.SelectedSlot);
        }

        [Test]
        public void TapSlot_ThenEmptySlot_MovesWithoutCompacting()
        {
            var editor = CreateEditor();
            editor.TapSlot(1);

            Assert.AreEqual(PartyTapResult.Changed, editor.TapSlot(5));
            Assert.AreEqual("2:B,3:C,5:A", Dump(editor));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SlotAndOutsider_OutsiderReplacesSlotMember_InEitherOrder(bool slotFirst)
        {
            var editor = CreateEditor();

            PartyTapResult result;
            if (slotFirst)
            {
                editor.TapSlot(2);
                result = editor.TapPachimon("X");
            }
            else
            {
                editor.TapPachimon("X");
                result = editor.TapSlot(2);
            }

            Assert.AreEqual(PartyTapResult.Changed, result);
            Assert.AreEqual("1:A,2:X,3:C", Dump(editor));
        }

        [Test]
        public void TapSlot_ThenPachimonInAnotherSlot_SwapsSlots()
        {
            var editor = CreateEditor();
            editor.TapSlot(1);

            Assert.AreEqual(PartyTapResult.Changed, editor.TapPachimon("C"));
            Assert.AreEqual("1:C,2:B,3:A", Dump(editor));
        }

        [Test]
        public void TapPartyMembersInList_SwapsSlots()
        {
            var editor = CreateEditor();
            editor.TapPachimon("A");

            Assert.AreEqual(PartyTapResult.Changed, editor.TapPachimon("B"));
            Assert.AreEqual("1:B,2:A,3:C", Dump(editor));
        }

        [Test]
        public void TapTwoOutsiders_OnlyMovesSelection()
        {
            var editor = CreateEditor();
            editor.TapPachimon("X");

            Assert.AreEqual(PartyTapResult.Selected, editor.TapPachimon("Y"));
            Assert.AreEqual("Y", editor.SelectedPachimonId);
            Assert.IsFalse(editor.IsDirty);
        }

        [Test]
        public void TapEmptySlot_ThenPartyMember_MovesMemberToEmptySlot()
        {
            var editor = CreateEditor();
            editor.TapSlot(6);

            Assert.AreEqual(PartyTapResult.Changed, editor.TapPachimon("A"));
            Assert.AreEqual("2:B,3:C,6:A", Dump(editor));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SlotAndSamePachimon_RemovesFromParty_InEitherOrder(bool slotFirst)
        {
            var editor = CreateEditor();

            PartyTapResult result;
            if (slotFirst)
            {
                editor.TapSlot(2);
                result = editor.TapPachimon("B");
            }
            else
            {
                editor.TapPachimon("B");
                result = editor.TapSlot(2);
            }

            Assert.AreEqual(PartyTapResult.Changed, result);
            Assert.AreEqual("1:A,3:C", Dump(editor));
        }

        [Test]
        public void RemovingLastMember_IsBlocked_ButReplacingIsAllowed()
        {
            var editor = new PartyEditor(new[] { new PartySlotInput(4, "A") });

            editor.TapSlot(4);
            Assert.AreEqual(PartyTapResult.BlockedLastMember, editor.TapPachimon("A"));
            Assert.AreEqual("4:A", Dump(editor));
            Assert.IsNull(editor.SelectedSlot);

            editor.TapSlot(4);
            Assert.AreEqual(PartyTapResult.Changed, editor.TapPachimon("X"));
            Assert.AreEqual("4:X", Dump(editor));
        }

        [Test]
        public void TapSameTargetTwice_Deselects()
        {
            var editor = CreateEditor();
            editor.TapSlot(1);

            Assert.AreEqual(PartyTapResult.Deselected, editor.TapSlot(1));
            Assert.IsNull(editor.SelectedSlot);
            Assert.IsFalse(editor.IsDirty);
        }

        [Test]
        public void IsDirty_IsFalseAfterSwappingBack()
        {
            var editor = CreateEditor();
            editor.TapSlot(1);
            editor.TapSlot(2);
            Assert.IsTrue(editor.IsDirty);

            editor.TapSlot(1);
            editor.TapSlot(2);
            Assert.IsFalse(editor.IsDirty);
        }
    }
}
