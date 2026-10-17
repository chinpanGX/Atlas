using System.Collections.Generic;
using R3;
using UnityEngine;
using ZLinq;

namespace Atlas.Presentation.Training
{
    // 技タブ。覚えている4つの技を上から技スロット順に並べる。
    public sealed class TrainingMovesView : MonoBehaviour
    {
        // 要素番号が技スロット-1に対応する。
        [SerializeField] private TrainingMoveRowView[] moveRows;

        private Observable<int> onReplaceButtonClicked;

        // 「付け替える」を押された技スロット(1始まり)を流す。
        // タブが非アクティブの間はAwakeが呼ばれないため、最初に参照したときに作る。
        public Observable<int> OnReplaceButtonClicked => onReplaceButtonClicked ??= Observable.Merge(
            moveRows.Select((row, index) => row.OnReplaceButtonClicked.Select(_ => index + 1)).ToArray());

        public void Refresh(IReadOnlyList<TrainingMoveDto> moves)
        {
            for (var index = 0; index < moveRows.Length; index++)
            {
                var slot = index + 1;
                var move = moves.FirstOrDefault(m => m.Slot == slot);
                if (move is null)
                {
                    moveRows[index].Hide();
                }
                else
                {
                    moveRows[index].Refresh(move);
                }
            }
        }
    }
}
