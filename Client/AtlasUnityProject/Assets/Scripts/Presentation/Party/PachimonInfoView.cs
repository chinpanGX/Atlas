using System.Linq;
using TMPro;
using UnityEngine;

namespace Atlas.Presentation.Party
{
    // 画面右側の詳細パネル。上から名前・タイプ・ステータス6種・技4つ。
    public sealed class PachimonInfoView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI typeText;
        [SerializeField] private PachimonStatRowView[] statRows;
        // 要素番号が技スロット-1に対応する。
        [SerializeField] private PachimonMoveRowView[] moveRows;

        public void Refresh(PachimonInfoDto info)
        {
            gameObject.SetActive(true);
            nameText.text = info.Name;
            typeText.text = string.Join(" / ", info.TypeNames);

            for (var index = 0; index < statRows.Length; index++)
            {
                var hasStat = index < info.Stats.Count;
                statRows[index].gameObject.SetActive(hasStat);
                if (hasStat)
                {
                    statRows[index].Refresh(info.Stats[index]);
                }
            }

            for (var index = 0; index < moveRows.Length; index++)
            {
                var slotNo = index + 1;
                var move = info.Moves.FirstOrDefault(m => m.Slot == slotNo);
                if (move is null)
                {
                    moveRows[index].Clear();
                }
                else
                {
                    moveRows[index].Refresh(move);
                }
            }
        }

        // パチモン未選択(所持0体など)の時はパネルごと隠す。
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
