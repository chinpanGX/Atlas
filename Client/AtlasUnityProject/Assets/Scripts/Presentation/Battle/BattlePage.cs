using System.Collections.Generic;
using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Battle
{
    public sealed class BattlePage : Page
    {
        [SerializeField] private SelfInfoView selfInfoView;
        [SerializeField] private OpponentInfoView opponentInfoView;
        [SerializeField] private CommandView commandView;
        [SerializeField] private CommonButton forfeitButton;

        public IReadOnlyList<Observable<Unit>> OnCommandButtonClicked => commandView.OnCommandButtonClicked;
        public Observable<Unit> OnForfeitButtonClicked => forfeitButton.OnClickAsObservable();

        public void Refresh(BattleUIStateDto stateDto)
        {
            selfInfoView.Refresh(stateDto.SelfInfo);
            opponentInfoView.Refresh(stateDto.OpponentInfo);
            commandView.Refresh(stateDto.Commands);
        }
    }
}
