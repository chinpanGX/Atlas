using System;
using System.Collections.Generic;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Domain;
using Atlas.MasterData;
using Atlas.MasterData.Enums;
using Atlas.MasterData.Models;
using Atlas.Presentation.GlobalHeader;
using Atlas.Presentation.Party;
using Atlas.Presentation.Shop;
using Cysharp.Threading.Tasks;
using MasterMemory;
using R3;
using Supplement.Core;
using UnityEngine;
using UnityScreenNavigator;
using ZLinq;

namespace Atlas.Presentation.Training
{
    // ステータスポイントの割り振りはTrainingStatAllocationが手元で持つ調整だけを変え、「確定する」で一度に保存する。
    // 確定していない調整があるまま別のパチモン・別のタブ・別の画面へ移るときは、捨てるかを確かめる。
    [AssetAddress(AddressDefinition.TrainingPage)]
    public sealed class TrainingPresenter : IScreenWithArgs<TrainingArgs>
    {
        // itemsマスタのitem_id=2(育成チケット)。
        private const int TicketItemId = 2;
        // 技の付け替え1回に使う育成チケットの枚数(サーバーと同じ値)。
        private const int MoveReplaceTicketCost = 5;

        private static readonly string[] StatLabels = { "HP", "こうげき", "ぼうぎょ", "とくこう", "とくぼう", "すばやさ" };

        private readonly TrainingPage view;
        private readonly TrainingArgs args;
        private readonly IPachimonService pachimonService;
        private readonly IPartyService partyService;
        private readonly IPachimonMoveMappingService pachimonMoveMappingService;
        private readonly IPachimonStatPointsEditService statPointsEditService;
        private readonly IItemService itemService;
        private readonly IMessageBroker messageBroker;
        private readonly MemoryDatabase database;
        private readonly IScreenNavigator screenNavigator;
        private readonly GlobalHeaderPresenter globalHeader;
        private readonly CompositeDisposable disposables = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();

        private GlobalHeaderHandle header;
        private TrainingTab tab = TrainingTab.Moves;
        // 所持が0体のときはnull。
        private PachimonEntity selected;
        private TrainingStatAllocation allocation;

        public TrainingPresenter(
            TrainingPage view,
            TrainingArgs args,
            IPachimonService pachimonService,
            IPartyService partyService,
            IPachimonMoveMappingService pachimonMoveMappingService,
            IPachimonStatPointsEditService statPointsEditService,
            IItemService itemService,
            IMessageBroker messageBroker,
            MemoryDatabase database,
            IScreenNavigator screenNavigator,
            GlobalHeaderPresenter globalHeader)
        {
            this.view = view;
            this.args = args;
            this.pachimonService = pachimonService;
            this.partyService = partyService;
            this.pachimonMoveMappingService = pachimonMoveMappingService;
            this.statPointsEditService = statPointsEditService;
            this.itemService = itemService;
            this.messageBroker = messageBroker;
            this.database = database;
            this.screenNavigator = screenNavigator;
            this.globalHeader = globalHeader;
        }

        public UniTask InitializeAsync()
        {
            header = globalHeader.Attach(this, new GlobalHeaderOptions { Title = "育成", LeaveAsync = ConfirmDiscardIfDirtyAsync })
                .AddTo(disposables);

            var pachimons = pachimonService.GetAll();
            view.RefreshPachimonList(pachimons.Select(CreatePachimonDto).ToList());
            view.ShowTab(tab);
            Select(FindInitialPachimonId(pachimons));

            // 確認Modalや通信を待つ間の連打で、二重に確認を出したり送ったりしないよう、実行中の押下は捨てる。
            view.OnPachimonClicked
                .SubscribeAwait(async (id, _) => await SelectWithConfirmAsync(id), AwaitOperation.Drop)
                .AddTo(disposables);
            view.OnTabClicked
                .SubscribeAwait(async (next, _) => await ChangeTabWithConfirmAsync(next), AwaitOperation.Drop)
                .AddTo(disposables);
            view.MovesView.OnReplaceButtonClicked
                .SubscribeAwait(async (slot, ct) => await OpenMoveCandidatesAsync(slot, ct), AwaitOperation.Drop)
                .AddTo(disposables);
            view.StatsView.OnIncreaseButtonClicked.Subscribe(stat => Adjust(() => allocation.Increase(stat))).AddTo(disposables);
            view.StatsView.OnDecreaseButtonClicked.Subscribe(stat => Adjust(() => allocation.Decrease(stat))).AddTo(disposables);
            view.StatsView.OnRevertButtonClicked.Subscribe(_ => Adjust(() => allocation.Revert())).AddTo(disposables);
            view.StatsView.OnConfirmButtonClicked
                .SubscribeAwait(async (_, ct) => await ConfirmStatPointsAsync(ct), AwaitOperation.Drop)
                .AddTo(disposables);
            // ショップでチケットを買って戻ってきたときなど、所持数が変わったら必要な枚数と確定後の所持数を出し直す。
            disposables.Add(messageBroker.Subscribe<ItemQuantityChanged>(_ => RefreshStats()));
            return UniTask.CompletedTask;
        }

        // 編成画面から来たときはそこで選んでいたパチモン、そうでなければパーティの先頭(パーティが空なら一覧の先頭)。
        private string FindInitialPachimonId(IReadOnlyList<PachimonEntity> pachimons)
        {
            if (args.InitialPlayerPachimonId is { } initial && pachimonService.Get(initial) is not null)
            {
                return initial;
            }

            var partyFirst = partyService.GetAll().FirstOrDefault();
            if (partyFirst is not null && pachimonService.Get(partyFirst.PlayerPachimonId) is not null)
            {
                return partyFirst.PlayerPachimonId;
            }

            return pachimons.FirstOrDefault()?.PlayerPachimonId;
        }

        private async UniTask SelectWithConfirmAsync(string playerPachimonId)
        {
            if (playerPachimonId == selected?.PlayerPachimonId || !await ConfirmDiscardIfDirtyAsync())
            {
                return;
            }

            Select(playerPachimonId);
        }

        private async UniTask ChangeTabWithConfirmAsync(TrainingTab next)
        {
            if (next == tab || !await ConfirmDiscardIfDirtyAsync())
            {
                return;
            }

            tab = next;
            view.ShowTab(tab);
        }

        // 確定していない調整が無ければそのまま進めてよい。あれば捨てるかを確かめ、捨てるなら確定済みの割り振りに戻す。
        private async UniTask<bool> ConfirmDiscardIfDirtyAsync()
        {
            if (allocation is null || !allocation.IsDirty)
            {
                return true;
            }

            var modal = await screenNavigator.PushModalAsync<DiscardAdjustmentPresenter>();
            if (!await screenNavigator.WaitForPopAsync<bool>(modal, lifetimeCancellation.Token))
            {
                return false;
            }

            allocation.Revert();
            RefreshStats();
            return true;
        }

        private void Select(string playerPachimonId)
        {
            selected = playerPachimonId is null ? null : pachimonService.Get(playerPachimonId);
            view.RefreshSelection(selected?.PlayerPachimonId, partyService.GetAll().Select(slot => slot.PlayerPachimonId).ToList());
            if (selected is null)
            {
                allocation = null;
                view.HidePachimon();
                return;
            }

            var master = FindMaster(selected);
            view.RefreshPachimon(new TrainingPachimonDto
            {
                Name = master.Name,
                PachimonId = selected.PachimonId,
                PrimaryType = master.PrimaryType,
                SecondaryType = master.SecondaryType,
            });
            view.MovesView.Refresh(CreateMoveDtos(selected));
            allocation = new TrainingStatAllocation(selected.StatPoints);
            RefreshStats();
        }

        private void Adjust(Action change)
        {
            if (allocation is null)
            {
                return;
            }

            change();
            RefreshStats();
        }

        private async UniTask ConfirmStatPointsAsync(CancellationToken cancellation)
        {
            if (selected is null || !allocation.IsDirty)
            {
                return;
            }

            var owned = itemService.GetAmount(TicketItemId);
            if (allocation.RequiredTickets > owned)
            {
                await ShowTicketShortageAsync("ステータスポイントの割り振り", owned, allocation.RequiredTickets, cancellation);
                return;
            }

            // 通信の途中で戻ると、結果を出す画面が無くなるため、終わるまでヘッダーを押せなくする。
            header.SetLocked(true);
            var saved = await statPointsEditService.EditAsync(selected.PlayerPachimonId, allocation.ToAdjustedStatPoints());
            cancellation.ThrowIfCancellationRequested();
            header.SetLocked(false);
            // 失敗したときのダイアログは通信の共通処理が出している。調整はそのまま残し、もう一度確定できるようにする
            if (!saved)
            {
                return;
            }

            selected = pachimonService.Get(selected.PlayerPachimonId);
            allocation = new TrainingStatAllocation(selected.StatPoints);
            RefreshStats();
        }

        // 技の候補の画面はまだ無い。チケットが足りないときは、候補を選んでから足りないと分かることのないよう先に知らせる。
        private async UniTask OpenMoveCandidatesAsync(int slot, CancellationToken cancellation)
        {
            var owned = itemService.GetAmount(TicketItemId);
            if (owned < MoveReplaceTicketCost)
            {
                await ShowTicketShortageAsync("技の付け替え", owned, MoveReplaceTicketCost, cancellation);
                return;
            }

            Debug.Log($"[Training] 技の候補の画面は未実装です(slot={slot})");
        }

        // 「ショップへ」を選んだら、育成画面の上にショップを開く(戻ると育成画面に戻る)。
        private async UniTask ShowTicketShortageAsync(string purpose, int owned, int required, CancellationToken cancellation)
        {
            var modal = await screenNavigator.PushModalAsync<TicketShortagePresenter, TicketShortageViewDto>(
                new TicketShortageViewDto { Purpose = purpose, Owned = owned, Required = required });
            if (await screenNavigator.WaitForPopAsync<bool>(modal, cancellation))
            {
                await screenNavigator.PushPageAsync<ShopPresenter>();
            }
        }

        private void RefreshStats()
        {
            if (selected is null || allocation is null)
            {
                return;
            }

            var master = FindMaster(selected);
            var baseStats = new[] { master.BaseHp, master.BaseAtk, master.BaseDef, master.BaseSpatk, master.BaseSpdef, master.BaseSpeed };
            var stats = new TrainingStatDto[TrainingStatAllocation.StatCount];
            for (var stat = 0; stat < stats.Length; stat++)
            {
                var committed = allocation.GetCommitted(stat);
                var adjusted = allocation.GetAdjusted(stat);
                stats[stat] = new TrainingStatDto
                {
                    Label = StatLabels[stat],
                    Points = adjusted,
                    Change = adjusted - committed,
                    CommittedValue = CalculateStat(stat, baseStats[stat], committed),
                    AdjustedValue = CalculateStat(stat, baseStats[stat], adjusted),
                    PointsRatio = (float)adjusted / StatPoints.MaxTotal,
                    CanDecrease = allocation.CanDecrease(stat),
                    CanIncrease = allocation.CanIncrease(stat),
                };
            }

            var owned = itemService.GetAmount(TicketItemId);
            view.StatsView.Refresh(new TrainingStatsViewDto
            {
                RemainingPoints = allocation.RemainingPoints,
                Stats = stats,
                RequiredTickets = allocation.RequiredTickets,
                TicketsBefore = owned,
                TicketsAfter = owned - allocation.RequiredTickets,
                IsDirty = allocation.IsDirty,
            });
        }

        // HPだけ計算式が違う(要素0がHP)。
        private static int CalculateStat(int stat, int baseStat, int points)
        {
            return stat == 0
                ? PachimonStatCalculator.CalculateHp(baseStat, points)
                : PachimonStatCalculator.CalculateOther(baseStat, points);
        }

        private IReadOnlyList<TrainingMoveDto> CreateMoveDtos(PachimonEntity pachimon)
        {
            return pachimonMoveMappingService.GetByPlayerPachimonId(pachimon.PlayerPachimonId)
                .Select(moveMap =>
                {
                    var move = database.MovesDataTable.FindByMoveId((int)moveMap.MoveId);
                    return new TrainingMoveDto
                    {
                        Slot = moveMap.Slot,
                        Name = move.Name,
                        Type = move.MoveType,
                        CategoryName = ToCategoryName(move.Category),
                        BasePower = move.BasePower,
                        Accuracy = move.Accuracy,
                        MaxPp = move.MaxPp,
                    };
                })
                .ToList();
        }

        private static string ToCategoryName(MoveCategory category) => category switch
        {
            MoveCategory.Physical => "物理",
            MoveCategory.Special => "特殊",
            MoveCategory.Status => "変化",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        private PachimonDto CreatePachimonDto(PachimonEntity pachimon)
        {
            return new PachimonDto
            {
                PlayerPachimonId = pachimon.PlayerPachimonId,
                Name = FindMaster(pachimon).Name,
                PachimonId = pachimon.PachimonId,
            };
        }

        private PachimonData FindMaster(PachimonEntity pachimon)
        {
            return database.PachimonDataTable.FindByPachimonId((int)pachimon.PachimonId);
        }

        public void Dispose()
        {
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            disposables.Dispose();
        }
    }
}
