using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Presentation.Party;
using MasterMemory;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityScreenNavigator;
using ZLinq;

namespace Atlas.Presentation.Scout
{
    // 候補は1回目のタップで選択(詳細表示)、選択中の候補をもう一度タップすると確認Modalを出し、
    // 「はい」で候補の選択を送って入手を確定する。
    // ジェムはロールした時点で消費済みのため、候補を選ぶまでは戻るボタンを押せなくする
    // (ロール中の候補を取り直すAPIが無く、離れると消費したジェムが無駄になるため)。
    [AssetAddress(AddressDefinition.ScoutPage)]
    public sealed class ScoutPresenter : IPresenter
    {
        // itemsマスタのitem_id=1(ジェム)。
        private const int GemItemId = 1;

        private readonly ScoutPage view;
        private readonly IScoutService scoutService;
        private readonly IItemService itemService;
        private readonly IPachimonService pachimonService;
        private readonly MemoryDatabase database;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();

        // 開催中バナーのうち先頭の1件だけを使う(バナーを選ぶUIは未作成)。取得前・取得失敗時はnull。
        private ScoutBanner banner;
        // 候補を選んでいないロール。選択が確定するとnullに戻る。
        private ScoutRoll roll;
        private int? selectedIndex;

        public ScoutPresenter(
            ScoutPage view,
            IScoutService scoutService,
            IItemService itemService,
            IPachimonService pachimonService,
            MemoryDatabase database,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.scoutService = scoutService;
            this.itemService = itemService;
            this.pachimonService = pachimonService;
            this.database = database;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.ClearCandidates();
            RefreshState();

            // 通信中の連打で二重に送信しないよう、実行中の押下は捨てる。
            view.OnScoutButtonClicked
                .SubscribeAwait(async (_, ct) => await RollAsync(ct), AwaitOperation.Drop)
                .AddTo(disposables);
            view.OnCandidateClicked
                .SubscribeAwait(async (index, ct) => await OnCandidateClickedAsync(index, ct), AwaitOperation.Drop)
                .AddTo(disposables);
            view.OnBackButtonClicked
                .SubscribeAwait(async (_, _) => await screenNavigator.PopPageAsync(this), AwaitOperation.Drop)
                .AddTo(disposables);

            // バナーの取得を待たずに画面を開き、取得できた時点でスカウトボタンを押せるようにする。
            LoadBannerAsync(lifetimeCancellation.Token).Forget();
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid LoadBannerAsync(CancellationToken cancellation)
        {
            var banners = await scoutService.GetBannersAsync();
            // 失敗したときのダイアログは通信の共通処理が出している。スカウトボタンは押せないままにする
            if (banners is null || cancellation.IsCancellationRequested)
            {
                return;
            }

            banner = banners.FirstOrDefault();

            if (banner is null)
            {
                Debug.LogWarning("[Scout] 開催中のスカウトバナーがありません");
                return;
            }

            view.SetBanner(banner.Name, banner.CostPerRoll);
            RefreshState();
        }

        private async UniTask RollAsync(CancellationToken cancellation)
        {
            if (banner is null || roll is not null)
            {
                return;
            }

            view.SetScoutButtonInteractable(false);
            view.SetBackButtonInteractable(false);
            roll = await scoutService.RollAsync(banner.BannerId);
            if (roll is null)
            {
                RefreshState();
                return;
            }

            cancellation.ThrowIfCancellationRequested();
            selectedIndex = null;
            // 候補を選ぶと所持に加わるが、表示中のNewは紹介を受けた時点の所持で決めたまま変えない。
            var ownedPachimonIds = pachimonService.GetAll().Select(pachimon => pachimon.PachimonId).ToHashSet();
            view.ShowCandidates(roll.Candidates
                .Select(candidate => CreatePachimonDto(candidate, !ownedPachimonIds.Contains(candidate.PachimonId)))
                .ToList());
            view.HidePachimonInfo();
            RefreshState();
        }

        private async UniTask OnCandidateClickedAsync(int index, CancellationToken cancellation)
        {
            if (roll is null)
            {
                return;
            }

            var candidate = roll.Candidates[index];
            if (selectedIndex != index)
            {
                selectedIndex = index;
                view.SetSelectedCandidate(index);
                view.RefreshPachimonInfo(CreatePachimonInfoDto(candidate));
                return;
            }

            await ConfirmAndSelectAsync(candidate, cancellation);
        }

        private async UniTask ConfirmAndSelectAsync(ScoutCandidate candidate, CancellationToken cancellation)
        {
            var pachimonName = FindPachimonName(candidate.PachimonId);
            var confirm = await screenNavigator.PushModalAsync<ScoutConfirmPresenter, ScoutConfirmViewDto>(
                new ScoutConfirmViewDto { PachimonName = pachimonName });
            var confirmed = await screenNavigator.WaitForPopAsync<bool>(confirm, cancellation);
            if (!confirmed)
            {
                return;
            }

            if (!await scoutService.SelectAsync(roll.RollId, candidate.Index))
            {
                // 候補は残したままにし、もう一度タップすれば送り直せるようにする。
                return;
            }

            Debug.Log($"[Scout] {pachimonName}を入手しました");
            roll = null;
            selectedIndex = null;
            view.ClearCandidates();
            RefreshState();
        }

        private void RefreshState()
        {
            var gems = itemService.GetAmount(GemItemId);
            view.SetGems(gems);
            view.SetScoutButtonInteractable(banner is not null && roll is null && gems >= banner.CostPerRoll);
            view.SetBackButtonInteractable(roll is null);
        }

        private PachimonDto CreatePachimonDto(ScoutCandidate candidate, bool isNew)
        {
            // 候補はまだ所持していないためPlayerPachimonIdは無い。
            return new PachimonDto
            {
                Name = FindPachimonName(candidate.PachimonId),
                PachimonId = candidate.PachimonId,
                IsNew = isNew,
            };
        }

        private PachimonInfoDto CreatePachimonInfoDto(ScoutCandidate candidate)
        {
            var moves = candidate.MoveIds.Select((moveId, index) =>
                new PachimonInfoDtoBuilder.MoveInput(index + 1, moveId, database.MovesDataTable.FindByMoveId(moveId).MaxPp));
            return PachimonInfoDtoBuilder.Build(database, candidate.PachimonId, moves.ToArray());
        }

        private string FindPachimonName(int pachimonId)
        {
            return database.PachimonDataTable.FindByPachimonId(pachimonId).Name;
        }

        public void Dispose()
        {
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            disposables.Dispose();
        }
    }
}
