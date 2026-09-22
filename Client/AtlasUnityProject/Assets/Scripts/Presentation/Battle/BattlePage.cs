using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Battle
{
    public sealed class BattlePage : Page
    {
        [SerializeField] private SelfInfoView selfInfoView;
        [SerializeField] private OpponentInfoView opponentInfoView;
        [SerializeField] private CommandView commandView;

        public IReadOnlyList<Observable<Unit>> OnCommandButtonClicked => commandView.OnCommandButtonClicked;

        public void Refresh(BattleUiState state)
        {
            selfInfoView.Refresh(state.SelfInfo);
            opponentInfoView.Refresh(state.OpponentInfo);
            commandView.Refresh(state.Commands);
        }
    }
}
