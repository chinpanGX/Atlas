using System;
using System.Linq;
using System.Threading;
using Atlas.Application;
using Atlas.Domain;
using Atlas.MasterData;
using Atlas.MasterData.Models;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    // design/battle.md「Stage 1」。技はコマンドUIのボタン(View)から選ぶが、強制交代は
    // UIを持たず自動で生存している先頭の枠に交代する(design/battle.mdの簡易AIと同じ方針を
    // 自分側にも適用したもの。バトル全体を自動進行させる用途で使うための簡略化)。
    public sealed class BattlePresenter : IAsyncStartable, IDisposable
    {
        private readonly BattlePage view;
        private readonly IBattleConnection connection;
        private readonly IMasterDataService masterDataService;
        private readonly BattleViewDto initialDto;
        private readonly CompositeDisposable disposables = new();

        private string selfPlayerId;
        private string opponentPlayerId;

        private int selfActiveIndex;
        private int[] selfHpPercentBySlot;
        private bool[] selfFaintedBySlot;
        private MovesData[] selfMoves = Array.Empty<MovesData>();

        private int? opponentActivePachimonId;
        private int opponentHpPercent = 100;

        public BattlePresenter(
            BattlePage view, IBattleConnection connection, IMasterDataService masterDataService, BattleViewDto initialDto)
        {
            this.view = view;
            this.connection = connection;
            this.masterDataService = masterDataService;
            this.initialDto = initialDto;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            connection.OnMatchStart += HandleMatchStart;
            connection.OnTurnResult += HandleTurnResult;

            view.OnMoveButton1Clicked.Subscribe(_ => SubmitMove(0)).AddTo(disposables);
            view.OnMoveButton2Clicked.Subscribe(_ => SubmitMove(1)).AddTo(disposables);
            view.OnMoveButton3Clicked.Subscribe(_ => SubmitMove(2)).AddTo(disposables);
            view.OnMoveButton4Clicked.Subscribe(_ => SubmitMove(3)).AddTo(disposables);

            await connection.JoinAsync("dummy-token", "dummy-match");
            await connection.SubmitSelectionAsync(initialDto.SelfPachimonIds);
        }

        public void Dispose()
        {
            connection.OnMatchStart -= HandleMatchStart;
            connection.OnTurnResult -= HandleTurnResult;
            disposables.Dispose();
        }

        private void SubmitMove(int index)
        {
            if (index >= selfMoves.Length)
            {
                return;
            }

            // Presenterは結果を待たない。結果はOnTurnResultイベント経由でHandleTurnResultに届く
            // (design/client-architecture.mdのイベント駆動更新に合わせる)。
            connection.SubmitMoveAsync(new MoveRequest(selfMoves[index].MoveId.ToString())).Forget();
        }

        private void HandleMatchStart(BattleStartPayload payload)
        {
            selfPlayerId = payload.Self.PlayerId;
            opponentPlayerId = payload.Opponent.PlayerId;

            selfActiveIndex = payload.Self.ActivePachimonIndex;
            selfHpPercentBySlot = payload.Self.SelectedPachimon.Select(s => s.State?.HpPercent ?? 100).ToArray();
            selfFaintedBySlot = payload.Self.SelectedPachimon.Select(s => s.State?.IsFainted ?? false).ToArray();
            RefreshSelfMoves();

            opponentActivePachimonId = payload.Opponent.SelectedPachimon[payload.Opponent.ActivePachimonIndex].State?.PachimonId;
            opponentHpPercent = payload.Opponent.SelectedPachimon[payload.Opponent.ActivePachimonIndex].State?.HpPercent ?? 100;

            view.Refresh(BuildUiState());
        }

        private void HandleTurnResult(TurnResultPayload payload)
        {
            foreach (var action in payload.Actions)
            {
                ApplyAction(action);
            }

            view.Refresh(BuildUiState());

            if (payload.PlayersRequiringForcedSwitch.Contains(selfPlayerId))
            {
                AutoSwitchSelfAsync().Forget();
            }
        }

        // Move: 対象は行動側の相手。Switch: 対象は行動側自身の交代先
        // (MockBattleConnection.ResolveTargetHpPercentと同じ解釈、design/battle.md参照)。
        private void ApplyAction(ActionResult action)
        {
            var actorIsSelf = action.PlayerId == selfPlayerId;

            if (action.Type == ActionType.Switch && action.NewActiveIndex is { } newIndex)
            {
                if (actorIsSelf)
                {
                    selfActiveIndex = newIndex;
                    selfHpPercentBySlot[newIndex] = action.TargetRemainingHpPercent;
                    RefreshSelfMoves();
                }
                else
                {
                    opponentHpPercent = action.TargetRemainingHpPercent;
                    if (action.RevealedPachimon is { } revealed)
                    {
                        opponentActivePachimonId = revealed.PachimonId;
                    }
                }

                return;
            }

            if (action.Type != ActionType.Move)
            {
                return;
            }

            if (actorIsSelf)
            {
                opponentHpPercent = action.TargetRemainingHpPercent;
            }
            else
            {
                selfHpPercentBySlot[selfActiveIndex] = action.TargetRemainingHpPercent;
                selfFaintedBySlot[selfActiveIndex] = action.TargetFainted;
            }
        }

        private async UniTaskVoid AutoSwitchSelfAsync()
        {
            for (var slot = 0; slot < selfFaintedBySlot.Length; slot++)
            {
                if (!selfFaintedBySlot[slot])
                {
                    await connection.SwitchAsync(slot);
                    return;
                }
            }
        }

        private void RefreshSelfMoves()
        {
            var pachimonId = int.Parse(initialDto.SelfPachimonIds[selfActiveIndex]);
            selfMoves = PachimonMoveLookup.GetInitialMoves(masterDataService.Database, pachimonId).ToArray();
        }

        private BattleUiState BuildUiState()
        {
            var selfPachimonId = int.Parse(initialDto.SelfPachimonIds[selfActiveIndex]);
            var selfName = masterDataService.Database.PachimonDataTable.FindByPachimonId(selfPachimonId).Name;
            var opponentName = opponentActivePachimonId is { } id
                ? masterDataService.Database.PachimonDataTable.FindByPachimonId(id).Name
                : "???";

            return new BattleUiState
            {
                SelfName = selfName,
                SelfHpPercent = selfHpPercentBySlot[selfActiveIndex],
                OpponentName = opponentName,
                OpponentHpPercent = opponentHpPercent,
                SelfMoveNames = selfMoves.Select(m => m.Name).ToList(),
            };
        }
    }
}
