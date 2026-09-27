using System;
using System.Linq;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Domain;
using Atlas.MasterData;
using Atlas.MasterData.Models;
using Atlas.Presentation.Common;
using Atlas.Presentation.Party;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Battle
{
    // design/battle.md「Stage 1」。技はコマンドUIのボタン、交代は交代ボタン→SwitchSelectModalで選ぶ。
    // 瀕死による強制交代も同じModal(やめるボタン無し)で選ばせる。相手が強制交代中はOpponentSwitchingModalを出して待つ。
    // 決着(OnBattleEnd)後はBattleResultModalを出し、そこからHomeシーンへ戻る。
    // 行動を送るとコマンドを隠し、届いたターン結果は行動順に1文ずつメッセージ枠へ流す(HP等の表示もその文に合わせて更新する)。
    // 流し終えてからコマンドを戻し、強制交代・相手の交代待ち・結果Modalへ進む。
    [AssetAddress(AddressDefinition.BattlePage)]
    public sealed class BattlePresenter : IScreenWithArgs<BattleViewDto>
    {
        private readonly BattlePage view;
        private readonly IBattleConnection connection;
        private readonly IMasterDataService masterDataService;
        private readonly BattleViewDto initialDto;
        private readonly IScreenNavigator screenNavigator;
        private readonly ISceneNavigator sceneNavigator;
        private readonly CompositeDisposable disposables = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();

        private bool matchStarted;
        // 決着後(結果Modal表示中)にコマンドや投了ボタンが押されても何もしないようにする。
        private bool battleEnded;
        // 行動(技・交代)を送ってからターン結果が届くまで。この間は次の行動を受け付けない。
        private bool awaitingTurnResult;
        // 瀕死による強制交代が必要な状態。技は送れず(送ってもサーバー側でスキップ扱い)、交代先の選択待ちになる。
        private bool forcedSwitchRequired;
        // 表示中のSwitchSelectModal。ターン結果や決着が届いた時に閉じるために持つ。
        private SwitchSelectPresenter openSwitchSelect;
        // 相手が強制交代で交代先を選んでいる状態(強制交代ターン)。相手の交代が済むまで何も送れない。
        private bool opponentSwitching;
        private OpponentSwitchingPresenter openOpponentSwitching;
        private bool pushingOpponentSwitchingModal;
        // ターン結果・決着の演出。届いた順に1つずつ流す(演出中に次の結果が届いても割り込ませない)。
        private UniTask eventPlayback = UniTask.CompletedTask;
        // 流し終えていない演出の数。0になるまで行動を受け付けない。
        private int pendingPlaybacks;
        // 次に届くターン結果が強制交代ターンのものか(前のターン結果で誰かが倒れて交代を求められた)。
        private bool nextTurnIsForcedSwitch;

        private string selfPlayerId;
        private string opponentPlayerId;

        private int selfActiveIndex;
        // 選出各枠のマスターデータのPachimonId・技。どちらもOnMatchStart(サーバーが判定に使う値)から取る。
        private int[] selfPachimonIdBySlot;
        private PachimonMoveSet[] selfMoveSets;
        // 選出各枠・各技の残りPP。OnMatchStartの値から、自分が技を使うたびに減らす(再接続時は再送値で戻す)。
        private int[][] selfCurrentPp;
        private int[] selfHpPercentBySlot;
        private bool[] selfFaintedBySlot;
        private MovesData[] selfMoves = Array.Empty<MovesData>();

        // 相手の選出各枠。種族は場に出て公開されるまで分からない(null)。交代で既に公開済みの枠へ戻る場合、
        // サーバーはRevealedPachimonを送らないため、枠ごとに覚えておいてNewActiveIndexで引き直す。
        private int opponentActiveIndex;
        private int?[] opponentPachimonIdBySlot;
        private int[] opponentHpPercentBySlot;

        public BattlePresenter(
            BattlePage view, IBattleConnection connection, IMasterDataService masterDataService, BattleViewDto initialDto,
            IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator)
        {
            this.view = view;
            this.connection = connection;
            this.masterDataService = masterDataService;
            this.initialDto = initialDto;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
        }

        public UniTask InitializeAsync()
        {
            connection.OnMatchStart += HandleMatchStart;
            connection.OnTurnResult += HandleTurnResult;
            connection.OnBattleEnd += HandleBattleEnd;

            // OnMatchStartが届くまでは行動できない。
            RefreshCommandsInteractable();

            view.OnMoveButtonClicked.Subscribe(SubmitMove).AddTo(disposables);

            view.OnSwitchButtonClicked
                .SubscribeAwait(async (_, ct) => await SelectAndSwitchAsync(isForced: false, ct), AwaitOperation.Drop)
                .AddTo(disposables);

            view.OnForfeitButtonClicked
                .SubscribeAwait(async (_, ct) => await ConfirmForfeitAsync(ct), AwaitOperation.Drop)
                .AddTo(disposables);

            // BattleServerへの参加を待たずに画面を開く。行動の入力はOnMatchStartが届くまで止めてある。
            JoinBattleAsync(lifetimeCancellation.Token).Forget();
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid JoinBattleAsync(CancellationToken cancellation)
        {
            var join = await connection.JoinAsync(initialDto.BattleToken, initialDto.MatchId);
            if (join.Status != JoinResultStatus.Success)
            {
                // トークン期限切れ(マッチ成立から30秒)やシークレットの食い違い等。対戦できないためHomeへ戻す。
                UnityEngine.Debug.LogError($"[Battle] BattleServerへの参加に失敗しました: {join.Status}");
                await sceneNavigator.ChangeSceneAsync(AddressDefinition.Home, cancellation);
                return;
            }

            await connection.SubmitSelectionAsync(initialDto.SelectedPlayerPachimonIds);
        }

        public void Dispose()
        {
            connection.OnMatchStart -= HandleMatchStart;
            connection.OnTurnResult -= HandleTurnResult;
            connection.OnBattleEnd -= HandleBattleEnd;
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            disposables.Dispose();
        }

        private bool CanAct => matchStarted && !battleEnded && !awaitingTurnResult && !forcedSwitchRequired && !opponentSwitching
                               && pendingPlaybacks == 0;

        private void RefreshCommandsInteractable()
        {
            view.SetCommandsInteractable(CanAct);
        }

        private void SubmitMove(int index)
        {
            if (!CanAct || index >= selfMoves.Length || selfCurrentPp[selfActiveIndex][index] <= 0)
            {
                return;
            }

            var moveId = selfMoves[index].MoveId.ToString();
            SendActionAsync(() => connection.SubmitMoveAsync(new MoveRequest(moveId))).Forget();
        }

        // Presenterはターン結果を待たない。結果はOnTurnResultイベント経由でHandleTurnResultに届き、
        // そこで入力ロックを解除する(design/client-architecture.mdのイベント駆動更新に合わせる)。
        // MockBattleConnectionは送信処理の中で同期的にOnTurnResultを発火するため、ロックは送信前に掛ける。
        private async UniTask SendActionAsync(Func<UniTask> send)
        {
            awaitingTurnResult = true;
            RefreshCommandsInteractable();
            view.ShowMessage(BattleMessageBuilder.WaitingForOpponent);
            try
            {
                await send();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 送れなかった場合はターン結果が届かないため、ロックを戻して再入力できるようにする。
                UnityEngine.Debug.LogError($"[Battle] 行動の送信に失敗しました: {e.Message}");
                awaitingTurnResult = false;
                view.ShowCommandPanel();
                RefreshCommandsInteractable();
            }
        }

        // 交代先をSwitchSelectModalで選ばせて送る。isForced=trueは瀕死による強制交代で、やめられない。
        private async UniTask SelectAndSwitchAsync(bool isForced, CancellationToken cancellation)
        {
            if (battleEnded || (!isForced && !CanAct))
            {
                return;
            }

            var switchSelect = await screenNavigator.PushModalAsync<SwitchSelectPresenter, SwitchSelectViewDto>(
                BuildSwitchSelectDto(isForced));
            openSwitchSelect = switchSelect;
            SwitchSelectResult result;
            try
            {
                result = await screenNavigator.WaitForPopAsync<SwitchSelectResult>(switchSelect, cancellation);
            }
            finally
            {
                if (openSwitchSelect == switchSelect)
                {
                    openSwitchSelect = null;
                }
            }

            // やめた、またはターンの進行・決着でこちらから閉じた(CloseSwitchModalAsync)場合。
            if (result.IsCanceled || battleEnded)
            {
                return;
            }

            if (isForced)
            {
                forcedSwitchRequired = false;
            }
            else if (!CanAct)
            {
                return;
            }

            await SendActionAsync(() => connection.SwitchAsync(result.PartySlot));
        }

        // 表示中のSwitchSelectModalをCanceledで閉じる。通信対戦ではターンの制限時間切れ(スキップ)で
        // 選択中にターンが進むことがあるため、ターン結果・決着の受信時に呼ぶ。
        private async UniTask CloseSwitchModalAsync()
        {
            if (openSwitchSelect is null)
            {
                return;
            }

            var switchSelect = openSwitchSelect;
            openSwitchSelect = null;
            // 既に交代先が選ばれて閉じている途中なら、その結果が優先され、閉じ終わるのを待つだけになる。
            await switchSelect.CloseAsync(SwitchSelectResult.Canceled);
        }

        private SwitchSelectViewDto BuildSwitchSelectDto(bool isForced)
        {
            return new SwitchSelectViewDto
            {
                IsForced = isForced,
                Candidates = selfPachimonIdBySlot
                    .Select((pachimonId, slot) =>
                    {
                        var master = masterDataService.Database.PachimonDataTable.FindByPachimonId(pachimonId);
                        var maxHp = PachimonStatCalculator.CalculateHp(master.BaseHp);
                        var moves = selfMoveSets[slot].Moves.Select((m, i) =>
                            new PachimonInfoDtoBuilder.MoveInput(i + 1, int.Parse(m.MoveId), selfCurrentPp[slot][i]));
                        return new SwitchCandidateDto
                        {
                            PartySlot = slot,
                            Name = master.Name,
                            CurrentHp = maxHp * selfHpPercentBySlot[slot] / 100,
                            MaxHp = maxHp,
                            IsActive = slot == selfActiveIndex,
                            IsFainted = selfFaintedBySlot[slot],
                            Info = PachimonInfoDtoBuilder.Build(masterDataService.Database, pachimonId, moves),
                        };
                    })
                    .ToList(),
            };
        }

        private void HandleMatchStart(BattleStartPayload payload)
        {
            selfPlayerId = payload.Self.PlayerId;
            opponentPlayerId = payload.Opponent.PlayerId;

            selfActiveIndex = payload.Self.ActivePachimonIndex;
            selfPachimonIdBySlot = payload.Self.SelectedPachimon.Select(s => s.State.PachimonId).ToArray();
            selfMoveSets = payload.SelfMoves;
            selfCurrentPp = payload.SelfMoves.Select(set => set.Moves.Select(m => m.CurrentPp).ToArray()).ToArray();
            selfHpPercentBySlot = payload.Self.SelectedPachimon.Select(s => s.State?.HpPercent ?? 100).ToArray();
            selfFaintedBySlot = payload.Self.SelectedPachimon.Select(s => s.State?.IsFainted ?? false).ToArray();
            RefreshSelfMoves();

            opponentActiveIndex = payload.Opponent.ActivePachimonIndex;
            opponentPachimonIdBySlot = payload.Opponent.SelectedPachimon.Select(s => s.State?.PachimonId).ToArray();
            opponentHpPercentBySlot = payload.Opponent.SelectedPachimon.Select(s => s.State?.HpPercent ?? 100).ToArray();

            matchStarted = true;
            view.Refresh(BuildUiState());
            RefreshCommandsInteractable();
        }

        // 投了確認Modalの結果(投了する=true)を待ち、投了する場合だけIBattleConnectionへ送る。
        // 投了による決着もOnBattleEnd経由で届くため、結果Modalの表示はHandleBattleEndに任せる。
        private async UniTask ConfirmForfeitAsync(CancellationToken cancellation)
        {
            if (battleEnded)
            {
                return;
            }

            var forfeitConfirm = await screenNavigator.PushModalAsync<ForfeitConfirmPresenter>();
            var forfeit = await screenNavigator.WaitForPopAsync<bool>(forfeitConfirm, cancellation);
            if (forfeit && !battleEnded)
            {
                await connection.ForfeitAsync();
            }
        }

        private void HandleBattleEnd(BattleEndPayload payload)
        {
            battleEnded = true;
            opponentSwitching = false;
            RefreshCommandsInteractable();
            // 決着したターンの演出を流し終えてから結果Modalを出す。
            EnqueuePlayback(_ => ShowBattleResultAsync(payload));
        }

        private async UniTask ShowBattleResultAsync(BattleEndPayload payload)
        {
            await CloseSwitchModalAsync();
            await CloseOpponentSwitchingModalAsync();

            var isWin = payload.WinnerId == selfPlayerId;
            await screenNavigator.PushModalAsync<BattleResultPresenter, BattleResultViewDto>(new BattleResultViewDto
            {
                ResultText = isWin ? "勝利!" : "敗北…",
                ReasonText = ToReasonText(payload.Reason, isWin),
            });
        }

        private static string ToReasonText(BattleEndReason reason, bool isWin) => reason switch
        {
            BattleEndReason.AllFainted => isWin ? "相手のパチモンをすべて倒した" : "自分のパチモンがすべて倒れた",
            BattleEndReason.Forfeit => isWin ? "相手が降参した" : "降参した",
            BattleEndReason.DisconnectTimeout => isWin ? "相手の接続が切れた" : "接続が切れた",
            _ => string.Empty,
        };

        private void HandleTurnResult(TurnResultPayload payload)
        {
            EnqueuePlayback(cancellation => PlayTurnResultAsync(payload, cancellation));
        }

        // 演出を前の演出の後ろに並べる。MockBattleConnectionは行動の送信中に同期的にターン結果を発火し、
        // 通信対戦でもターン結果の直後に決着が届くため、演出中に次の出来事が来ても順番どおりに流す。
        private void EnqueuePlayback(Func<CancellationToken, UniTask> play)
        {
            pendingPlaybacks++;
            RefreshCommandsInteractable();
            eventPlayback = PlayAfterAsync(eventPlayback, play, lifetimeCancellation.Token).Preserve();
        }

        private async UniTask PlayAfterAsync(UniTask previous, Func<CancellationToken, UniTask> play,
            CancellationToken cancellation)
        {
            await previous;
            try
            {
                await play(cancellation);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                pendingPlaybacks--;
                // 画面の破棄で止まった場合はViewに触らない。
                if (!cancellation.IsCancellationRequested)
                {
                    RefreshCommandsInteractable();
                }
            }
        }

        private async UniTask PlayTurnResultAsync(TurnResultPayload payload, CancellationToken cancellation)
        {
            // 選択中にターンが進んだ(制限時間切れ等)交代選択Modalは、演出の前にCanceledで閉じる。
            await CloseSwitchModalAsync();

            var isForcedSwitchTurn = nextTurnIsForcedSwitch;
            nextTurnIsForcedSwitch = payload.PlayersRequiringForcedSwitch.Length > 0;
            foreach (var action in payload.Actions)
            {
                await PlayActionAsync(action, isForcedSwitchTurn, cancellation);
            }

            awaitingTurnResult = false;
            forcedSwitchRequired = payload.PlayersRequiringForcedSwitch.Contains(selfPlayerId);
            opponentSwitching = payload.PlayersRequiringForcedSwitch.Contains(opponentPlayerId);

            // 決着したターンは最後の文を出したまま結果Modalへ進む。
            if (battleEnded)
            {
                return;
            }

            view.ShowCommandPanel();
            if (forcedSwitchRequired)
            {
                SelectAndSwitchAsync(isForced: true, cancellation).Forget();
            }

            SyncOpponentSwitchingModalAsync().Forget();
        }

        private async UniTask PlayActionAsync(ActionResult action, bool isForcedSwitchTurn, CancellationToken cancellation)
        {
            var actorIsSelf = action.PlayerId == selfPlayerId;
            switch (action.Type)
            {
                case ActionType.Move:
                    await PlayMoveAsync(action, actorIsSelf, cancellation);
                    break;
                case ActionType.Switch:
                    ApplyAction(action);
                    view.Refresh(BuildUiState());
                    await view.PlayMessageAsync(
                        BattleMessageBuilder.SentOut(actorIsSelf, ActivePachimonName(actorIsSelf)), cancellation);
                    break;
                case ActionType.Skip:
                    // 強制交代ターンで交代を待っていた側と、同じターンに先に倒れた側の非行動は出さない
                    // (前者は行動の機会が無く、後者は倒れた時点で伝えているため)。
                    if (!isForcedSwitchTurn && !IsActivePachimonFainted(actorIsSelf))
                    {
                        await view.PlayMessageAsync(
                            BattleMessageBuilder.CouldNotAct(actorIsSelf, ActivePachimonName(actorIsSelf)), cancellation);
                    }

                    break;
            }
        }

        // 技名 → (HPゲージが減り終わるまで待つ) → 外れ/急所/効果 → 倒れた、の順に流す。
        private async UniTask PlayMoveAsync(ActionResult action, bool actorIsSelf, CancellationToken cancellation)
        {
            var moveName = masterDataService.Database.MovesDataTable.FindByMoveId(int.Parse(action.MoveId)).Name;
            await view.PlayMessageAsync(
                BattleMessageBuilder.MoveUsed(actorIsSelf, ActivePachimonName(actorIsSelf), moveName), cancellation);

            ApplyAction(action);
            await view.RefreshAnimatedAsync(BuildUiState(), cancellation);

            if (!action.Hit)
            {
                await view.PlayMessageAsync(BattleMessageBuilder.Missed, cancellation);
                return;
            }

            if (action.Critical)
            {
                await view.PlayMessageAsync(BattleMessageBuilder.Critical, cancellation);
            }

            var targetIsSelf = !actorIsSelf;
            if (BattleMessageBuilder.Effectiveness(action.Effectiveness, targetIsSelf,
                    ActivePachimonName(targetIsSelf)) is { } effectiveness)
            {
                await view.PlayMessageAsync(effectiveness, cancellation);
            }

            if (action.TargetFainted)
            {
                await view.PlayMessageAsync(
                    BattleMessageBuilder.Fainted(targetIsSelf, ActivePachimonName(targetIsSelf)), cancellation);
            }
        }

        private string ActivePachimonName(bool isSelf)
        {
            if (isSelf)
            {
                return masterDataService.Database.PachimonDataTable.FindByPachimonId(selfPachimonIdBySlot[selfActiveIndex]).Name;
            }

            return opponentPachimonIdBySlot[opponentActiveIndex] is { } id
                ? masterDataService.Database.PachimonDataTable.FindByPachimonId(id).Name
                : "???";
        }

        private bool IsActivePachimonFainted(bool isSelf)
        {
            return isSelf ? selfFaintedBySlot[selfActiveIndex] : opponentHpPercentBySlot[opponentActiveIndex] <= 0;
        }

        // opponentSwitchingに合わせてOpponentSwitchingModalを出し入れする。MockBattleConnectionは相手の交代を
        // 同期的に続けて送ってくるため、Pushの途中で相手の交代が済むことがある。その場合はPushが終わってから閉じる。
        private async UniTaskVoid SyncOpponentSwitchingModalAsync()
        {
            if (opponentSwitching && openOpponentSwitching is null && !pushingOpponentSwitchingModal)
            {
                pushingOpponentSwitchingModal = true;
                try
                {
                    openOpponentSwitching = await screenNavigator.PushModalAsync<OpponentSwitchingPresenter>();
                }
                finally
                {
                    pushingOpponentSwitchingModal = false;
                }
            }

            if (!opponentSwitching)
            {
                await CloseOpponentSwitchingModalAsync();
            }
        }

        private async UniTask CloseOpponentSwitchingModalAsync()
        {
            if (openOpponentSwitching is null)
            {
                return;
            }

            var opponentSwitchingPresenter = openOpponentSwitching;
            openOpponentSwitching = null;
            await screenNavigator.PopModalAsync(opponentSwitchingPresenter);
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
                    opponentActiveIndex = newIndex;
                    opponentHpPercentBySlot[newIndex] = action.TargetRemainingHpPercent;
                    if (action.RevealedPachimon is { } revealed)
                    {
                        opponentPachimonIdBySlot[newIndex] = revealed.PachimonId;
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
                opponentHpPercentBySlot[opponentActiveIndex] = action.TargetRemainingHpPercent;
                ConsumeSelfPp(action.MoveId);
            }
            else
            {
                selfHpPercentBySlot[selfActiveIndex] = action.TargetRemainingHpPercent;
                selfFaintedBySlot[selfActiveIndex] = action.TargetFainted;
            }
        }

        // 技を選んだ時点で命中/外れに関わらずPPを1消費する(design/battle.md「PP消費」、サーバーと同じ規則)。
        private void ConsumeSelfPp(string moveId)
        {
            var moveIndex = Array.FindIndex(selfMoveSets[selfActiveIndex].Moves, m => m.MoveId == moveId);
            if (moveIndex >= 0 && selfCurrentPp[selfActiveIndex][moveIndex] > 0)
            {
                selfCurrentPp[selfActiveIndex][moveIndex]--;
            }
        }

        private void RefreshSelfMoves()
        {
            selfMoves = selfMoveSets[selfActiveIndex].Moves
                .Select(m => masterDataService.Database.MovesDataTable.FindByMoveId(int.Parse(m.MoveId)))
                .ToArray();
        }

        private BattleUIStateDto BuildUiState()
        {
            var selfPachimonId = selfPachimonIdBySlot[selfActiveIndex];
            var selfPachimon = masterDataService.Database.PachimonDataTable.FindByPachimonId(selfPachimonId);
            var selfHpPercent = selfHpPercentBySlot[selfActiveIndex];
            var selfMaxHp = PachimonStatCalculator.CalculateHp(selfPachimon.BaseHp);

            var opponentHpPercent = opponentHpPercentBySlot[opponentActiveIndex];
            var opponentName = ActivePachimonName(isSelf: false);

            return new BattleUIStateDto
            {
                SelfInfo = new SelfInfoDto
                {
                    PachimonName = selfPachimon.Name,
                    CurrentHp = selfMaxHp * selfHpPercent / 100,
                    MaxHp = selfMaxHp,
                    CurrentHpGauge = selfHpPercent / 100f,
                },
                OpponentInfo = new OpponentInfoDto
                {
                    PachimonName = opponentName,
                    CurrentHpPercent = opponentHpPercent,
                    CurrentHpGauge = opponentHpPercent / 100f,
                },
                Commands = selfMoves
                    .Select((m, i) => new CommandDto
                    {
                        SlotNo = i,
                        Name = m.Name,
                        TypeName = PachimonTypeNames.ToDisplayName(m.MoveType),
                        CurrentPp = selfCurrentPp[selfActiveIndex][i],
                        MaxPp = selfMoveSets[selfActiveIndex].Moves[i].MaxPp,
                    })
                    .ToList(),
            };
        }
    }
}
