using System;
using System.Collections.Generic;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Domain;
using Atlas.MasterData.Models;
using Atlas.Presentation.GlobalHeader;
using Atlas.Presentation.Training;
using MasterMemory;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityScreenNavigator;
using ZLinq;

namespace Atlas.Presentation.Party
{
    // 編成の入れ替えはPartyEditorが手元で持つ状態だけを書き換え、ヘッダーの戻るで画面を離れるときに、
    // 変更があった時だけ編成を全置き換えで保存する。
    [AssetAddress(AddressDefinition.PartyPage)]
    public sealed class PartyPresenter : IPresenter
    {
        private readonly PartyPage view;
        private readonly IPartyService partyService;
        private readonly IPachimonService pachimonService;
        private readonly IPachimonMoveMappingService pachimonMoveMappingService;
        private readonly MemoryDatabase database;
        private readonly GlobalHeaderPresenter globalHeader;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private PartyEditor editor;
        // PlayerPachimonId → 一覧・枠の表示データ。入れ替えのたびにマスタを引き直さないよう画面を開いた時に作る。
        private Dictionary<string, PachimonDto> pachimonDtos;
        // 詳細に出しているパチモン。出していなければnull。
        private string shownPachimonId;

        public PartyPresenter(
            PartyPage view,
            IPartyService partyService,
            IPachimonService pachimonService,
            IPachimonMoveMappingService pachimonMoveMappingService,
            MemoryDatabase database,
            GlobalHeaderPresenter globalHeader,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.partyService = partyService;
            this.pachimonService = pachimonService;
            this.pachimonMoveMappingService = pachimonMoveMappingService;
            this.database = database;
            this.globalHeader = globalHeader;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            var pachimons = pachimonService.GetAll();
            pachimonDtos = pachimons.ToDictionary(p => p.PlayerPachimonId, CreatePachimonDto);

            // 編成はplayerDiffで所持パチモンと同時に届くため通常は揃っているが、所持していない
            // パチモンを指す枠があれば保存時にサーバーが404を返すので、最初から除外しておく。
            editor = new PartyEditor(partyService.GetAll()
                .Where(slot => pachimonDtos.ContainsKey(slot.PlayerPachimonId))
                .Select(slot => new PartySlotInput(slot.Slot, slot.PlayerPachimonId))
                .ToArray());

            view.RefreshPachimonList(pachimons.Select(p => pachimonDtos[p.PlayerPachimonId]).ToList());
            RefreshParty();

            // 初期表示は編成の先頭(slot昇順)のパチモン。
            var first = editor.ToInputs().FirstOrDefault();
            if (first.PlayerPachimonId is null)
            {
                shownPachimonId = null;
                view.HidePachimonInfo();
            }
            else
            {
                ShowPachimonInfo(first.PlayerPachimonId);
            }

            view.OnSlotClicked.Subscribe(OnSlotClicked).AddTo(disposables);
            view.OnPachimonClicked.Subscribe(OnPachimonClicked).AddTo(disposables);
            // 保存〜Push完了までの連打で同じPageを重ねて積まないよう、実行中の押下は捨てる。
            view.OnTrainingButtonClicked
                .SubscribeAwait(async (_, _) => await OpenTrainingAsync(), AwaitOperation.Drop)
                .AddTo(disposables);
            globalHeader.Attach(this, new GlobalHeaderOptions { Title = "パーティ編成", LeaveAsync = SaveIfDirtyAsync })
                .AddTo(disposables);
            return UniTask.CompletedTask;
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

        // 育成画面はパーティに入っている個体に印を付けるため、編成を変えていれば先に保存してから開く。
        private async UniTask OpenTrainingAsync()
        {
            if (shownPachimonId is null || !await SaveIfDirtyAsync())
            {
                return;
            }

            await screenNavigator.PushPageAsync<TrainingPresenter, TrainingArgs>(
                new TrainingArgs { InitialPlayerPachimonId = shownPachimonId });
        }

        private UniTask<bool> SaveIfDirtyAsync()
        {
            if (!editor.IsDirty)
            {
                return UniTask.FromResult(true);
            }

            // 失敗したときのダイアログは通信の共通処理が出している。falseを返して画面に留まり、編集内容を残す。
            // もう一度戻るを押すと送り直す
            return partyService.SaveAsync(editor.ToInputs());
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
                shownPachimonId = null;
                view.HidePachimonInfo();
                return;
            }

            shownPachimonId = playerPachimonId;

            view.RefreshPachimonInfo(CreatePachimonInfoDto(pachimon));
        }

        private PachimonDto CreatePachimonDto(PachimonEntity pachimon)
        {
            return new PachimonDto
            {
                PlayerPachimonId = pachimon.PlayerPachimonId,
                Name = FindMaster(pachimon).Name,
                PachimonId = pachimon.PachimonId,
            };
        }

        private PachimonInfoDto CreatePachimonInfoDto(PachimonEntity pachimon)
        {
            // 対戦外なので残りPPは最大値(PachimonInfoDtoBuilderがマスタのMaxPpで埋める値と同じにする)。
            var moves = pachimonMoveMappingService.GetByPlayerPachimonId(pachimon.PlayerPachimonId)
                .Select(moveMap =>
                {
                    var moveId = (int)moveMap.MoveId;
                    var maxPp = database.MovesDataTable.FindByMoveId(moveId).MaxPp;
                    return new PachimonInfoDtoBuilder.MoveInput(moveMap.Slot, moveId, maxPp);
                });
            return PachimonInfoDtoBuilder.Build(database, (int)pachimon.PachimonId, pachimon.StatPoints, moves.ToArray());
        }

        private PachimonData FindMaster(PachimonEntity pachimon)
        {
            return database.PachimonDataTable.FindByPachimonId((int)pachimon.PachimonId);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
