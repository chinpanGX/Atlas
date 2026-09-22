using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Battle
{
    public sealed class BattlePage : Page
    {
        [SerializeField] private TextMeshProUGUI selfNameText;
        [SerializeField] private TextMeshProUGUI selfHpText;
        [SerializeField] private TextMeshProUGUI opponentNameText;
        [SerializeField] private TextMeshProUGUI opponentHpText;
        [SerializeField] private Button moveButton1;
        [SerializeField] private TextMeshProUGUI moveLabel1;
        [SerializeField] private Button moveButton2;
        [SerializeField] private TextMeshProUGUI moveLabel2;
        [SerializeField] private Button moveButton3;
        [SerializeField] private TextMeshProUGUI moveLabel3;
        [SerializeField] private Button moveButton4;
        [SerializeField] private TextMeshProUGUI moveLabel4;

        public Observable<Unit> OnMoveButton1Clicked => moveButton1.OnClickAsObservable();
        public Observable<Unit> OnMoveButton2Clicked => moveButton2.OnClickAsObservable();
        public Observable<Unit> OnMoveButton3Clicked => moveButton3.OnClickAsObservable();
        public Observable<Unit> OnMoveButton4Clicked => moveButton4.OnClickAsObservable();

        public void Refresh(BattleUiState state)
        {
            selfNameText.text = state.SelfName;
            selfHpText.text = $"HP {state.SelfHpPercent}%";
            opponentNameText.text = state.OpponentName;
            opponentHpText.text = $"HP {state.OpponentHpPercent}%";

            SetMoveButton(moveButton1, moveLabel1, state.SelfMoveNames, 0);
            SetMoveButton(moveButton2, moveLabel2, state.SelfMoveNames, 1);
            SetMoveButton(moveButton3, moveLabel3, state.SelfMoveNames, 2);
            SetMoveButton(moveButton4, moveLabel4, state.SelfMoveNames, 3);
        }

        // 習得技が2つしかないパチモンでは3/4番目のボタンを隠す。
        private static void SetMoveButton(Button button, TextMeshProUGUI label, IReadOnlyList<string> moveNames, int index)
        {
            var hasMove = index < moveNames.Count;
            button.gameObject.SetActive(hasMove);
            if (hasMove)
            {
                label.text = moveNames[index];
            }
        }
    }
}
