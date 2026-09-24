using System;
using System.Threading;
using Atlas.Application;
using Atlas.Navigation;
using Atlas.Presentation.Party;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;
using ZLinq;

namespace Atlas.Presentation.Scout
{
    // 候補は1回目のタップで選択(詳細表示)、選択中の候補をもう一度タップすると確認Modalを出し、
    // 「はい」でPOST /scout/rolls/{rollId}/selectを送って入手を確定する。
    // ジェムはロールした時点で消費済みのため、候補を選ぶまでは戻るボタンを押せなくする
    // (ロール中の候補を取り直すAPIが無く、離れると消費したジェムが無駄になるため)。
    public sealed class ScoutPresenter : IAsyncStartable, IDisposable
    {
        // itemsマスタのitem_id=1(ジェム)。
        private const int GemItemId = 1;

        private readonly ScoutPage view;
        private readonly IScoutConnection scoutConnection;
        private readonly IItemFetchService itemFetchService;
        private readonly IMasterDataService masterDataService;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        // 開催中バナーのうち先頭の1件だけを使う(バナーを選ぶUIは未作成)。取得前・取得失敗時はnull。
        private ScoutBanner banner;
        // 候補を選んでいないロール。選択が確定するとnullに戻る。
        private ScoutRoll roll;
        private int? selectedIndex;

        public ScoutPresenter(
            ScoutPage view,
            IScoutConnection scoutConnection,
            IItemFetchService itemFetchService,
            IMasterDataService masterDataService,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.scoutConnection = scoutConnection;
            this.itemFetchService = itemFetchService;
            this.masterDataService = masterDataService;
            this.screenNavigator = screenNavigator;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
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
                .SubscribeAwait(async (_, _) => await screenNavigator.PopPageAsync(), AwaitOperation.Drop)
                .AddTo(disposables);

            try
            {
                banner = (await scoutConnection.GetBannersAsync()).FirstOrDefault();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // エラーModalの仕組みが未実装のため、ログのみ(スカウトボタンは押せないまま)。
                Debug.LogError($"[Scout] バナーの取得に失敗しました: {e.Message}");
                return;
            }

            cancellation.ThrowIfCancellationRequested();
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
            try
            {
                roll = await scoutConnection.RollAsync(banner.BannerId);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Debug.LogError($"[Scout] スカウトに失敗しました: {e.Message}");
                RefreshState();
                return;
            }

            cancellation.ThrowIfCancellationRequested();
            selectedIndex = null;
            view.ShowCandidates(roll.Candidates.Select(CreatePachimonDto).ToList());
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
            var modal = await screenNavigator.PushModalAsync<ScoutConfirmModal, ScoutConfirmViewDto>(
                new ScoutConfirmViewDto { PachimonName = pachimonName });
            var confirmed = await modal.WaitForResultAsync(cancellation);
            if (!confirmed)
            {
                return;
            }

            try
            {
                await scoutConnection.SelectAsync(roll.RollId, candidate.Index);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 候補は残したままにし、もう一度タップすれば再送できるようにする。
                Debug.LogError($"[Scout] 候補の確定に失敗しました: {e.Message}");
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
            var gems = itemFetchService.GetAmount(GemItemId);
            view.SetGems(gems);
            view.SetScoutButtonInteractable(banner is not null && roll is null && gems >= banner.CostPerRoll);
            view.SetBackButtonInteractable(roll is null);
        }

        private PachimonDto CreatePachimonDto(ScoutCandidate candidate)
        {
            // 候補はまだ所持していないためPlayerPachimonIdは無い。
            return new PachimonDto { Name = FindPachimonName(candidate.PachimonId), Thumbnail = null };
        }

        private PachimonInfoDto CreatePachimonInfoDto(ScoutCandidate candidate)
        {
            var database = masterDataService.Database;
            var moves = candidate.MoveIds.Select((moveId, index) =>
                new PachimonInfoDtoBuilder.MoveInput(index + 1, moveId, database.MovesDataTable.FindByMoveId(moveId).MaxPp))
                .ToArray();
            return PachimonInfoDtoBuilder.Build(database, candidate.PachimonId, moves);
        }

        private string FindPachimonName(int pachimonId)
        {
            return masterDataService.Database.PachimonDataTable.FindByPachimonId(pachimonId).Name;
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
