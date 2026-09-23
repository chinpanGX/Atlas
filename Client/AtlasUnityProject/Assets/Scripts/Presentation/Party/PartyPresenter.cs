using System;
using System.Collections.Generic;
using System.Linq;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Party
{
    // View先行で作っている段階のため、表示データはダミー。所持パチモン・編成・技をRepositoryと
    // マスタから組み立てるServiceを実装したら、Create*Dummy系をそちらの呼び出しに置き換える。
    public sealed class PartyPresenter : IInitializable, IDisposable
    {
        private readonly PartyPage view;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        public PartyPresenter(PartyPage view, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.screenNavigator = screenNavigator;
        }

        public void Initialize()
        {
            var pachimons = CreateDummyPachimons();
            var party = CreateDummyParty(pachimons);

            view.RefreshParty(party);
            view.RefreshPachimonList(pachimons);
            view.RefreshPachimonInfo(CreateDummyInfo(party.Slots[0].Pachimon));

            view.OnSlotClicked
                .Subscribe(slotNo =>
                {
                    var slot = party.Slots.FirstOrDefault(s => s.Slot == slotNo);
                    if (slot is not null)
                    {
                        view.RefreshPachimonInfo(CreateDummyInfo(slot.Pachimon));
                    }
                })
                .AddTo(disposables);
            view.OnPachimonClicked
                .Subscribe(id => view.RefreshPachimonInfo(
                    CreateDummyInfo(pachimons.First(p => p.PlayerPachimonId == id))))
                .AddTo(disposables);
            // Pop中の連打で下のHomePageまでPopしないよう、最初の1回だけ受け付ける。
            view.OnBackButtonClicked.Take(1)
                .Subscribe(_ => screenNavigator.PopPageAsync().Forget())
                .AddTo(disposables);
        }

        private static IReadOnlyList<PachimonDto> CreateDummyPachimons()
        {
            return Enumerable.Range(1, 12)
                .Select(n => new PachimonDto { PlayerPachimonId = $"dummy-{n}", Name = $"パチモン{n}" })
                .ToList();
        }

        // slot1〜3のみ編成済み、4〜6は未編成。
        private static PartyDto CreateDummyParty(IReadOnlyList<PachimonDto> pachimons)
        {
            return new PartyDto
            {
                Slots = Enumerable.Range(1, 3)
                    .Select(slot => new PartySlotDto { Slot = slot, Pachimon = pachimons[slot - 1] })
                    .ToList(),
            };
        }

        // 技スロット4は未設定(ブランク表示)の例。
        private static PachimonInfoDto CreateDummyInfo(PachimonDto pachimon)
        {
            return new PachimonInfoDto
            {
                Name = pachimon.Name,
                TypeNames = new[] { "あく", "ひこう" },
                Stats = new[]
                {
                    new PachimonStatDto { Label = "HP", Value = 201, Ratio = 0.75f },
                    new PachimonStatDto { Label = "こうげき", Value = 76, Ratio = 0.25f },
                    new PachimonStatDto { Label = "ぼうぎょ", Value = 178, Ratio = 0.65f },
                    new PachimonStatDto { Label = "とくこう", Value = 80, Ratio = 0.3f },
                    new PachimonStatDto { Label = "とくぼう", Value = 150, Ratio = 0.55f },
                    new PachimonStatDto { Label = "すばやさ", Value = 86, Ratio = 0.32f },
                },
                Moves = new[]
                {
                    new PachimonMoveDto { Slot = 1, Name = "イカサマ", TypeName = "あく", MaxPp = 16 },
                    new PachimonMoveDto { Slot = 2, Name = "ちょうはつ", TypeName = "あく", MaxPp = 20 },
                    new PachimonMoveDto { Slot = 3, Name = "でんじは", TypeName = "でんき", MaxPp = 20 },
                },
            };
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
