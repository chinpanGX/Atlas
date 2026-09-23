using System;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Atlas.Presentation.PartyEdit
{
    // ひな形段階。所持パチモン・現在の編成を読み出すServiceと、POST /edit/partyを送るConnectionが
    // まだ無いため、初期表示は空の編成で、スロット選択・保存はログ出力のみ。
    public sealed class PartyEditPresenter : IInitializable, IDisposable
    {
        private readonly PartyEditPage view;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        public PartyEditPresenter(PartyEditPage view, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.screenNavigator = screenNavigator;
        }

        public void Initialize()
        {
            view.Refresh(new PartyEditViewDto());

            view.OnSlotButtonClicked
                .Subscribe(slot => Debug.Log($"[PartyEdit] Slot {slot} clicked (not implemented yet)"))
                .AddTo(disposables);
            view.OnSaveButtonClicked
                .Subscribe(_ => Debug.Log("[PartyEdit] Save button clicked (not implemented yet)"))
                .AddTo(disposables);
            // Pop中の連打で下のHomePageまでPopしないよう、最初の1回だけ受け付ける。
            view.OnBackButtonClicked.Take(1)
                .Subscribe(_ => screenNavigator.PopPageAsync().Forget())
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
