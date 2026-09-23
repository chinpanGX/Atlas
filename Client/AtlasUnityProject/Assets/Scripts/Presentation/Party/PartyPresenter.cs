using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Atlas.Application;
using Atlas.Domain;
using Atlas.MasterData;
using Atlas.MasterData.Enums;
using Atlas.MasterData.Models;
using Atlas.Navigation;
using Atlas.Presentation.Common;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Atlas.Presentation.Party
{
    // 編成の入れ替えはPartyEditorが手元で持つ状態だけを書き換え、戻るボタンで変更があった時だけ
    // POST /edit/party(全置き換え)で保存してから前の画面へ戻る。
    public sealed class PartyPresenter : IInitializable, IDisposable
    {
        // 種族値の上限。ステータスゲージの長さは種族値/この値で表す(実効値はレベル固定で種族値に比例するため)。
        private const float MaxBaseStat = 255f;

        private readonly PartyPage view;
        private readonly IPartyService partyService;
        private readonly IPachimonService pachimonService;
        private readonly IPachimonMoveMappingService pachimonMoveMappingService;
        private readonly IMasterDataService masterDataService;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private PartyEditor editor;
        // PlayerPachimonId → 一覧・枠の表示データ。入れ替えのたびにマスタを引き直さないよう画面を開いた時に作る。
        private Dictionary<string, PachimonDto> pachimonDtos;

        public PartyPresenter(
            PartyPage view,
            IPartyService partyService,
            IPachimonService pachimonService,
            IPachimonMoveMappingService pachimonMoveMappingService,
            IMasterDataService masterDataService,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.partyService = partyService;
            this.pachimonService = pachimonService;
            this.pachimonMoveMappingService = pachimonMoveMappingService;
            this.masterDataService = masterDataService;
            this.screenNavigator = screenNavigator;
        }

        public void Initialize()
        {
            var pachimons = pachimonService.GetAll();
            pachimonDtos = pachimons.ToDictionary(p => p.PlayerPachimonId, CreatePachimonDto);

            // 編成はplayerDiffで所持パチモンと同時に届くため通常は揃っているが、所持していない
            // パチモンを指す枠があれば保存時にサーバーが404を返すので、最初から除外しておく。
            editor = new PartyEditor(partyService.GetAll()
                .Where(slot => pachimonDtos.ContainsKey(slot.PlayerPachimonId))
                .Select(slot => new PartySlotInput(slot.Slot, slot.PlayerPachimonId)));

            view.RefreshPachimonList(pachimons.Select(p => pachimonDtos[p.PlayerPachimonId]).ToList());
            RefreshParty();

            // 初期表示は編成の先頭(slot昇順)のパチモン。
            var first = editor.ToInputs().FirstOrDefault();
            if (first.PlayerPachimonId is null)
            {
                view.HidePachimonInfo();
            }
            else
            {
                ShowPachimonInfo(first.PlayerPachimonId);
            }

            view.OnSlotClicked.Subscribe(OnSlotClicked).AddTo(disposables);
            view.OnPachimonClicked.Subscribe(OnPachimonClicked).AddTo(disposables);
            // 保存中の連打で二重送信や、下のHomePageまでのPopが起きないよう、実行中の押下は捨てる。
            view.OnBackButtonClicked
                .SubscribeAwait(async (_, ct) => await SaveAndBackAsync(ct), AwaitOperation.Drop)
                .AddTo(disposables);
        }

        private void OnSlotClicked(int slot)
        {
            HandleTapResult(editor.TapSlot(slot));

            // 入れ替え後も含め、タップした枠に今いるパチモンを表示する(空欄なら表示はそのまま)。
            if (editor.GetPachimonAt(slot) is { } playerPachimonId)
            {
                ShowPachimonInfo(playerPachimonId);
            }
        }

        private void OnPachimonClicked(string playerPachimonId)
        {
            HandleTapResult(editor.TapPachimon(playerPachimonId));
            ShowPachimonInfo(playerPachimonId);
        }

        private void HandleTapResult(PartyTapResult result)
        {
            if (result == PartyTapResult.BlockedLastMember)
            {
                Debug.Log("[Party] パーティの最後の1体は外せません");
            }

            RefreshParty();
        }

        private async UniTask SaveAndBackAsync(CancellationToken cancellation)
        {
            if (editor.IsDirty)
            {
                view.SetBackButtonInteractable(false);
                try
                {
                    await partyService.SaveAsync(editor.ToInputs());
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // エラーModalの仕組みが未実装のため、画面に留まって編集内容を残す。もう一度戻るを押すと再送する。
                    Debug.LogError($"[Party] パーティ編成の保存に失敗しました: {e.Message}");
                    view.SetBackButtonInteractable(true);
                    return;
                }
            }

            cancellation.ThrowIfCancellationRequested();
            await screenNavigator.PopPageAsync();
        }

        private void RefreshParty()
        {
            var slots = new List<PartySlotDto>();
            for (var slot = 1; slot <= PartyEditor.MaxSlots; slot++)
            {
                if (editor.GetPachimonAt(slot) is { } playerPachimonId)
                {
                    slots.Add(new PartySlotDto { Slot = slot, Pachimon = pachimonDtos[playerPachimonId] });
                }
            }

            view.RefreshParty(new PartyDto { Slots = slots });
            view.RefreshFrames(
                editor.SelectedSlot,
                editor.SelectedPachimonId,
                slots.Select(s => s.Pachimon.PlayerPachimonId).ToList());
        }

        private void ShowPachimonInfo(string playerPachimonId)
        {
            var pachimon = pachimonService.Get(playerPachimonId);
            if (pachimon is null)
            {
                view.HidePachimonInfo();
                return;
            }

            view.RefreshPachimonInfo(CreatePachimonInfoDto(pachimon));
        }

        private PachimonDto CreatePachimonDto(PachimonEntity pachimon)
        {
            return new PachimonDto
            {
                PlayerPachimonId = pachimon.PlayerPachimonId,
                Name = FindMaster(pachimon).Name,
                // サムネイル画像は未作成。用意できたらここでpachimonIdから読み込んだSpriteを渡す。
                Thumbnail = null,
            };
        }

        private PachimonInfoDto CreatePachimonInfoDto(PachimonEntity pachimon)
        {
            var master = FindMaster(pachimon);

            var typeNames = new List<string> { PachimonTypeNames.ToDisplayName(master.PrimaryType) };
            if (master.SecondaryType != PachimonType.None)
            {
                typeNames.Add(PachimonTypeNames.ToDisplayName(master.SecondaryType));
            }

            var moves = pachimonMoveMappingService.GetByPlayerPachimonId(pachimon.PlayerPachimonId)
                .Select(moveMap =>
                {
                    var move = masterDataService.Database.MovesDataTable.FindByMoveId((int)moveMap.MoveId);
                    return new PachimonMoveDto
                    {
                        Slot = moveMap.Slot,
                        Name = move.Name,
                        TypeName = PachimonTypeNames.ToDisplayName(move.MoveType),
                        MaxPp = move.MaxPp,
                    };
                })
                .ToList();

            return new PachimonInfoDto
            {
                Name = master.Name,
                TypeNames = typeNames,
                Stats = new[]
                {
                    CreateStatDto("HP", PachimonStatCalculator.CalculateHp(master.BaseHp), master.BaseHp),
                    CreateStatDto("こうげき", PachimonStatCalculator.CalculateOther(master.BaseAtk), master.BaseAtk),
                    CreateStatDto("ぼうぎょ", PachimonStatCalculator.CalculateOther(master.BaseDef), master.BaseDef),
                    CreateStatDto("とくこう", PachimonStatCalculator.CalculateOther(master.BaseSpatk), master.BaseSpatk),
                    CreateStatDto("とくぼう", PachimonStatCalculator.CalculateOther(master.BaseSpdef), master.BaseSpdef),
                    CreateStatDto("すばやさ", PachimonStatCalculator.CalculateOther(master.BaseSpeed), master.BaseSpeed),
                },
                Moves = moves,
            };
        }

        private static PachimonStatDto CreateStatDto(string label, int value, int baseStat)
        {
            return new PachimonStatDto { Label = label, Value = value, Ratio = baseStat / MaxBaseStat };
        }

        private PachimonData FindMaster(PachimonEntity pachimon)
        {
            return masterDataService.Database.PachimonDataTable.FindByPachimonId((int)pachimon.PachimonId);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
