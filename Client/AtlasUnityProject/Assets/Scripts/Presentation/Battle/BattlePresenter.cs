using System;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Domain;
using Atlas.MasterData;
using Atlas.MasterData.Enums;
using Atlas.MasterData.Models;
using Atlas.Presentation.Common;
using Atlas.Presentation.Party;
using MasterMemory;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;
using ZLinq;

namespace Atlas.Presentation.Battle
{
    // 対戦画面。参加するとOnSelectionStartが届くのでSelectionModalで選出させ、選んだ内容を送る。
    // 相手の選出が済む(OnMatchStart)まではSelectionModalのまま待たせる。
    // 技はコマンドUIのボタン、交代は交代ボタン→SwitchSelectModalで選ぶ。
    // 瀕死による強制交代も同じModal(やめるボタン無し)で選ばせる。相手が強制交代中はOpponentSwitchingModalを出して待つ。
    // 決着(OnBattleEnd)後はBattleResultModalを出し、そこからHomeシーンへ戻る。
    // 行動を送るとコマンドを隠し、届いたターン結果は行動順に1文ずつメッセージ枠へ流す(HP等の表示もその文に合わせて更新する)。
    // 流し終えてからコマンドを戻し、強制交代・相手の交代待ち・結果Modalへ進む。
    // 自分の接続が切れたら入力を止めて参加し直し、再送されるOnMatchStartで盤面とターンの途中の状態を戻す。
    // 演出を流し終えて入力できるようになったらサーバーに準備を送り、サーバーが制限時間を数え始めたら(OnTurnStart)、
    // 行動を選べる間だけ残り時間を出す。
    [AssetAddress(AddressDefinition.BattlePage)]
    public sealed class BattlePresenter : IScreenWithArgs<BattleViewDto>
    {
        private readonly BattlePage view;
        private readonly IBattleConnection connection;
        private readonly MemoryDatabase database;
        private readonly IPachimonService pachimonService;
        private readonly IPachimonMoveMappingService pachimonMoveMappingService;
        private readonly BattleViewDto initialDto;
        private readonly IScreenNavigator screenNavigator;
        private readonly ISceneNavigator sceneNavigator;
        private readonly ISystemDialog systemDialog;
        private readonly CompositeDisposable disposables = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();

        private bool matchStarted;
        // 決着後(結果Modal表示中)にコマンドや投了ボタンが押されても何もしないようにする。
        private bool battleEnded;
        // 行動(技・交代)を送ってからターン結果が届くまで。この間は次の行動を受け付けない。
        private bool awaitingTurnResult;
        // 瀕死による強制交代が必要な状態。技は送れず(送ってもサーバー側でスキップ扱い)、交代先の選択待ちになる。
        private bool forcedSwitchRequired;
        // 表示中のSelectionModal。対戦の開始・決着(選出の時間切れ等)で閉じるために持つ。
        private SelectionPresenter openSelection;
        private bool selectionSubmitted;
        // 選出画面の内容と、選出の締め切り(ローカル時刻)。SelectionModalを開き直す時の残り時間の計算に使う。
        private SelectionStartPayload selectionPayload;
        private DateTime selectionDeadline;
        // 表示中のSwitchSelectModal。ターン結果や決着が届いた時に閉じるために持つ。
        private SwitchSelectPresenter openSwitchSelect;
        // 相手が強制交代で交代先を選んでいる状態(強制交代ターン)。相手の交代が済むまで何も送れない。
        private bool opponentSwitching;
        private OpponentSwitchingPresenter openOpponentSwitching;
        private bool pushingOpponentSwitchingModal;
        // OpponentSwitchingModalを開き終わったら完了する。開いている途中に閉じる側が待つのに使う。
        private UniTaskCompletionSource opponentSwitchingPushed;
        // ターン結果・決着の演出。届いた順に1つずつ流す(演出中に次の結果が届いても割り込ませない)。
        private UniTask eventPlayback = UniTask.CompletedTask;
        // 流し終えていない演出の数。0になるまで行動を受け付けない。
        private int pendingPlaybacks;
        // 次に届くターン結果が強制交代ターンのものか(前のターン結果で誰かが倒れて交代を求められた)。
        private bool nextTurnIsForcedSwitch;
        // 自分の接続が切れて参加し直している間。
        private bool reconnecting;
        // 参加し直した後、盤面の再送(OnMatchStart)を待っている間。それまでの手元の状態は古い可能性がある。
        private bool resyncPending;
        // 相手が切断して再接続の猶予中。サーバーはこの間どちらの行動も受け付けない。
        private bool opponentDisconnected;
        // ターンの締め切り(ローカル時刻)。サーバーが数え始めてから、ターン結果が届くか切断されるまで。
        private DateTime? turnDeadline;
        private int? displayedTurnSeconds;

        private string selfPlayerId;
        private string opponentPlayerId;

        private int selfActiveIndex;
        // 選出各枠のマスターデータのPachimonId・技。どちらもOnMatchStart(サーバーが判定に使う値)から取る。
        private int[] selfPachimonIdBySlot;
        private PachimonMoveSet[] selfMoveSets;
        // 選出各枠のステータスポイント。OnMatchStartには含まれないため手元の所持データから取る
        // (対戦中は育成できないため、サーバーが判定に使う値と食い違わない)。
        private StatPoints[] selfStatPointsBySlot;
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

        private TypeMatchupTable typeMatchupTable;

        public BattlePresenter(
            BattlePage view, IBattleConnection connection, MemoryDatabase database,
            IPachimonService pachimonService, IPachimonMoveMappingService pachimonMoveMappingService,
            BattleViewDto initialDto,
            IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator, ISystemDialog systemDialog)
        {
            this.view = view;
            this.connection = connection;
            this.database = database;
            this.pachimonService = pachimonService;
            this.pachimonMoveMappingService = pachimonMoveMappingService;
            this.initialDto = initialDto;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
            this.systemDialog = systemDialog;
        }

        public UniTask InitializeAsync()
        {
            connection.OnSelectionStart += HandleSelectionStart;
            connection.OnMatchStart += HandleMatchStart;
            connection.OnTurnResult += HandleTurnResult;
            connection.OnBattleEnd += HandleBattleEnd;
            connection.OnTurnStart += HandleTurnStart;
            connection.OnOpponentDisconnected += HandleOpponentDisconnected;
            connection.OnOpponentReconnected += HandleOpponentReconnected;
            connection.OnReconnecting += HandleReconnecting;
            connection.OnReconnected += HandleReconnected;
            connection.OnConnectionLost += HandleConnectionLost;

            // OnMatchStartが届くまでは行動できない。
            view.HideBattlefield();
            RefreshCommandsInteractable();
            view.SetConnectionStatus(null);
            RunTurnTimerAsync(lifetimeCancellation.Token).Forget();

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

        // 参加できなければ対戦できないため、知らせてからHomeへ戻す。
        private async UniTaskVoid JoinBattleAsync(CancellationToken cancellation)
        {
            JoinResultStatus? status;
            try
            {
                status = (await connection.JoinAsync(initialDto.BattleToken, initialDto.MatchId)).Status;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                UnityEngine.Debug.LogWarning($"[Battle] BattleServerに接続できませんでした: {e.Message}");
                status = null;
            }

            if (status == JoinResultStatus.Success || cancellation.IsCancellationRequested)
            {
                return;
            }

            UnityEngine.Debug.LogWarning($"[Battle] BattleServerへの参加に失敗しました: {status}");
            await systemDialog.ShowAsync("対戦に 参加できませんでした", JoinFailedMessage(status), "ホームへ");
            await sceneNavigator.ChangeSceneAsync(AddressDefinition.Home, cancellation);
        }

        // InvalidTokenはトークンの期限切れ(マッチ成立から30秒)やシークレットの食い違い。nullは接続そのものの失敗。
        private static string JoinFailedMessage(JoinResultStatus? status) => status switch
        {
            JoinResultStatus.InvalidToken => "マッチングから 時間が たちすぎました。\nもう一度 対戦相手を 探してください。",
            JoinResultStatus.MatchNotFound => "対戦は すでに 終わっています。",
            JoinResultStatus.AlreadyJoined => "この対戦には すでに 参加しています。",
            _ => "対戦サーバーに つながりませんでした。\n通信環境の 良いところで もう一度 お試しください。",
        };

        // 参加し直せなかった(猶予が切れた・対戦が終わった)。結果も届かないため、知らせてからHomeへ戻す。
        private async UniTaskVoid ShowConnectionLostAsync(CancellationToken cancellation)
        {
            await systemDialog.ShowAsync(
                "通信が 切れました", "通信が 切れたため、対戦を 続けられませんでした。", "ホームへ");
            await sceneNavigator.ChangeSceneAsync(AddressDefinition.Home, cancellation);
        }

        // 送信済み(選出中に再接続した場合)なら相手の選出を待つだけ。
        private void HandleSelectionStart(SelectionStartPayload payload)
        {
            selectionPayload = payload;
            selectionDeadline = DateTime.UtcNow.AddSeconds(payload.RemainingSeconds);
            if (payload.SelectionSubmitted)
            {
                view.ShowMessage(BattleMessageBuilder.WaitingForOpponentSelection);
                return;
            }

            ShowSelectionAsync(lifetimeCancellation.Token).Forget();
        }

        // SelectionModalを開き、「けってい」のたびに選出を送る。Modalは対戦の開始・決着でこちらから閉じる。
        private async UniTaskVoid ShowSelectionAsync(CancellationToken cancellation)
        {
            if (openSelection is not null || matchStarted || battleEnded)
            {
                return;
            }

            var selection = await screenNavigator.PushModalAsync<SelectionPresenter, SelectionViewDto>(BuildSelectionDto());
            openSelection = selection;
            selection.OnConfirmed.Subscribe(ids => SubmitSelectionAsync(selection, ids).Forget());
            try
            {
                await screenNavigator.WaitForPopAsync<object>(selection, cancellation);
            }
            finally
            {
                if (openSelection == selection)
                {
                    openSelection = null;
                }
            }

            // こちらから閉じる前に(背景タップ等で)閉じられた場合は、選出しないと敗北になるため開き直す。
            // 送信済みなら相手の選出を待つだけ。
            if (!matchStarted && !battleEnded)
            {
                if (selectionSubmitted)
                {
                    view.ShowMessage(BattleMessageBuilder.WaitingForOpponentSelection);
                }
                else
                {
                    ShowSelectionAsync(cancellation).Forget();
                }
            }
        }

        // 送れなかった場合(サーバー側で選出は未確定のまま)は、SelectionModalで「けってい」を押し直せるようにする。
        private async UniTaskVoid SubmitSelectionAsync(SelectionPresenter selection, string[] playerPachimonIds)
        {
            try
            {
                await connection.SubmitSelectionAsync(playerPachimonIds);
                selectionSubmitted = true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                UnityEngine.Debug.LogWarning($"[Battle] 選出の送信に失敗しました: {e.Message}");
                if (openSelection == selection)
                {
                    selection.CancelWaiting();
                }

                // 自分の接続が切れた場合は、参加し直した後の再送で選出画面を戻す。
                if (!reconnecting)
                {
                    await systemDialog.ShowAsync("送信できませんでした", "選出を 送れませんでした。\nもう一度 「けってい」を 押してください。");
                }
            }
        }

        private async UniTask CloseSelectionModalAsync()
        {
            if (openSelection is null)
            {
                return;
            }

            var selection = openSelection;
            openSelection = null;
            await selection.CloseAsync();
        }

        // 選出画面のPayloadには技が含まれないため、技は手元の所持データから出す(対戦前なので残りPPは最大値)。
        // 選出中にパーティ編成・技の付け替えはできないため、サーバーが対戦に使う技と食い違わない。
        private PachimonInfoDto BuildSelectionInfo(PartyPachimon pachimon)
        {
            var moves = pachimonMoveMappingService.GetByPlayerPachimonId(pachimon.PlayerPachimonId)
                .Select(moveMap =>
                {
                    var moveId = (int)moveMap.MoveId;
                    return new PachimonInfoDtoBuilder.MoveInput(
                        moveMap.Slot, moveId, database.MovesDataTable.FindByMoveId(moveId).MaxPp);
                });
            return PachimonInfoDtoBuilder.Build(
                database, pachimon.PachimonId, FindStatPoints(pachimon.PlayerPachimonId), moves.ToArray());
        }

        // 手元の所持データに無い個体(通常は起きない)は、割り振っていない扱いにする。
        private StatPoints FindStatPoints(string playerPachimonId)
        {
            return pachimonService.Get(playerPachimonId)?.StatPoints ?? StatPoints.Zero;
        }

        private SelectionViewDto BuildSelectionDto()
        {
            return new SelectionViewDto
            {
                RemainingSeconds = Math.Max(0, (int)Math.Ceiling((selectionDeadline - DateTime.UtcNow).TotalSeconds)),
                SelectionCount = Math.Min(selectionPayload.MaxSelectionCount, selectionPayload.SelfParty.Length),
                SelfParty = selectionPayload.SelfParty
                    .Select(p =>
                    {
                        var master = database.PachimonDataTable.FindByPachimonId(p.PachimonId);
                        return new SelectionCandidateDto
                        {
                            PlayerPachimonId = p.PlayerPachimonId,
                            Name = master.Name,
                            TypeNames = PachimonTypeNames.ToDisplayNames(master.PrimaryType, master.SecondaryType),
                            Info = BuildSelectionInfo(p),
                        };
                    })
                    .ToList(),
                OpponentParty = selectionPayload.OpponentPartyPachimonIds
                    .Select(id =>
                    {
                        var master = database.PachimonDataTable.FindByPachimonId(id);
                        return new SelectionOpponentDto
                        {
                            Name = master.Name,
                            TypeNames = PachimonTypeNames.ToDisplayNames(master.PrimaryType, master.SecondaryType),
                        };
                    })
                    .ToList(),
            };
        }

        public void Dispose()
        {
            connection.OnSelectionStart -= HandleSelectionStart;
            connection.OnMatchStart -= HandleMatchStart;
            connection.OnTurnResult -= HandleTurnResult;
            connection.OnBattleEnd -= HandleBattleEnd;
            connection.OnTurnStart -= HandleTurnStart;
            connection.OnOpponentDisconnected -= HandleOpponentDisconnected;
            connection.OnOpponentReconnected -= HandleOpponentReconnected;
            connection.OnReconnecting -= HandleReconnecting;
            connection.OnReconnected -= HandleReconnected;
            connection.OnConnectionLost -= HandleConnectionLost;
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            disposables.Dispose();
        }

        private bool CanAct => matchStarted && !battleEnded && !awaitingTurnResult && !forcedSwitchRequired && !opponentSwitching
                               && pendingPlaybacks == 0 && !reconnecting && !resyncPending && !opponentDisconnected;

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
        // そこで入力ロックを解除する(イベントで届いた結果から画面を更新する)。
        // ターン結果は送信の応答より先に届くことがあるため、ロックは送信前に掛ける。
        // 送れたらtrue。
        private async UniTask<bool> SendActionAsync(Func<UniTask> send)
        {
            awaitingTurnResult = true;
            RefreshCommandsInteractable();
            view.ShowMessage(BattleMessageBuilder.WaitingForOpponent);
            try
            {
                await send();
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 送れなかった場合はターン結果が届かないため、ロックを戻して再入力できるようにする。
                // 自分の接続が切れた場合は、参加し直した後の再送(OnMatchStart)で状態を戻す。
                UnityEngine.Debug.LogWarning($"[Battle] 行動の送信に失敗しました: {e.Message}");
                awaitingTurnResult = false;
                if (reconnecting || battleEnded)
                {
                    return false;
                }

                view.ShowCommandPanel();
                RefreshCommandsInteractable();
                await systemDialog.ShowAsync("送信できませんでした", "行動を 送れませんでした。\nもう一度 選んでください。");
                return false;
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

            var sent = await SendActionAsync(() => connection.SwitchAsync(result.PartySlot));
            // 強制交代は交代しないと進まないため、送れなかったら選び直させる。
            if (!sent && isForced && !reconnecting && !battleEnded)
            {
                forcedSwitchRequired = true;
                RefreshCommandsInteractable();
                SelectAndSwitchAsync(isForced: true, cancellation).Forget();
            }
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
                        var master = database.PachimonDataTable.FindByPachimonId(pachimonId);
                        var maxHp = PachimonStatCalculator.CalculateHp(master.BaseHp, selfStatPointsBySlot[slot].Hp);
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
                            Info = PachimonInfoDtoBuilder.Build(database, pachimonId, selfStatPointsBySlot[slot], moves.ToArray()),
                        };
                    })
                    .ToList(),
            };
        }

        // 対戦の開始では、選出画面を閉じてから両者の繰り出しを見せ、流し終えてから入力を始める。
        // 対戦中の再送(参加し直した後)は、流している演出の後ろに並べてから戻す。
        private void HandleMatchStart(BattleStartPayload payload)
        {
            if (!matchStarted)
            {
                ApplyMatchStart(payload);
                EnqueuePlayback(async cancellation =>
                {
                    await CloseSelectionModalAsync();
                    await PlayOpeningAsync(cancellation);
                    BeginTurnInput(cancellation);
                });
                return;
            }

            EnqueuePlayback(async cancellation =>
            {
                // 交代先の選択は、戻した状態から選び直させる。
                await CloseSwitchModalAsync();
                ApplyMatchStart(payload);
                view.Refresh(BuildUiState());
                BeginTurnInput(cancellation);
            });
        }

        // 相手、自分の順に、最初のパチモンを繰り出す。
        private async UniTask PlayOpeningAsync(CancellationToken cancellation)
        {
            var state = BuildUiState();
            foreach (var isSelf in new[] { false, true })
            {
                await view.PlayOpeningSendOutAsync(isSelf, state, cancellation);
                await view.PlayMessageAsync(BattleMessageBuilder.SentOut(isSelf, ActivePachimonName(isSelf)), cancellation);
            }
        }

        // 盤面と、ターンの途中の状態(強制交代・送信済み・相手の切断中)を、届いた内容に置き換える。
        // 画面への反映と入力の開始は呼び出し側で行う(対戦の開始では、繰り出しの演出の後に始めるため)。
        private void ApplyMatchStart(BattleStartPayload payload)
        {
            selfPlayerId = payload.Self.PlayerId;
            opponentPlayerId = payload.Opponent.PlayerId;

            selfActiveIndex = payload.Self.ActivePachimonIndex;
            selfPachimonIdBySlot = payload.Self.SelectedPachimon.Select(s => s.State.PachimonId).ToArray();
            selfMoveSets = payload.SelfMoves;
            selfStatPointsBySlot = payload.Self.SelectedPachimon.Select(s => FindStatPoints(s.State.PlayerPachimonId)).ToArray();
            selfCurrentPp = payload.SelfMoves.Select(set => set.Moves.Select(m => m.CurrentPp).ToArray()).ToArray();
            selfHpPercentBySlot = payload.Self.SelectedPachimon.Select(s => s.State?.HpPercent ?? 100).ToArray();
            selfFaintedBySlot = payload.Self.SelectedPachimon.Select(s => s.State?.IsFainted ?? false).ToArray();
            RefreshSelfMoves();

            opponentActiveIndex = payload.Opponent.ActivePachimonIndex;
            opponentPachimonIdBySlot = payload.Opponent.SelectedPachimon.Select(s => s.State?.PachimonId).ToArray();
            opponentHpPercentBySlot = payload.Opponent.SelectedPachimon.Select(s => s.State?.HpPercent ?? 100).ToArray();
            typeMatchupTable ??= new TypeMatchupTable(database);

            opponentDisconnected = payload.OpponentDisconnected;
            awaitingTurnResult = payload.ActionSubmitted;
            nextTurnIsForcedSwitch = payload.PlayersRequiringForcedSwitch.Length > 0;
            forcedSwitchRequired = payload.PlayersRequiringForcedSwitch.Contains(selfPlayerId) && !payload.ActionSubmitted;
            opponentSwitching = payload.PlayersRequiringForcedSwitch.Contains(opponentPlayerId);
            matchStarted = true;
            resyncPending = false;

            // サーバーは対戦の開始・参加し直した後、準備がそろうまで数え始めない。
            turnDeadline = null;
            RefreshConnectionStatus();
            RefreshCommandsInteractable();
        }

        // ターンの入力を始める。送信済みなら結果を待つ文を出し、強制交代なら交代先を選ばせる。
        private void BeginTurnInput(CancellationToken cancellation)
        {
            if (awaitingTurnResult)
            {
                view.ShowMessage(BattleMessageBuilder.WaitingForOpponent);
            }
            else
            {
                view.ShowCommandPanel();
            }

            if (forcedSwitchRequired && openSwitchSelect is null)
            {
                SelectAndSwitchAsync(isForced: true, cancellation).Forget();
            }

            SyncOpponentSwitchingModalAsync().Forget();

            // 後ろに演出が並んでいる(続けて届いたターン結果・再送)なら、それを流し終えてから送る。
            // 先に送ると、まだ見せていないターンの制限時間を数え始めさせてしまう。
            if (pendingPlaybacks <= 1)
            {
                SendReadyAsync().Forget();
            }
        }

        // 送れなかった場合、接続が切れていれば参加し直した後の再送でもう一度送る。そうでなくてもサーバーは
        // 準備待ちの上限で数え始めるため、知らせずに続ける。
        private async UniTaskVoid SendReadyAsync()
        {
            try
            {
                await connection.ReadyForTurnAsync();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                UnityEngine.Debug.LogWarning($"[Battle] 準備の送信に失敗しました: {e.Message}");
            }
        }

        private void HandleTurnStart(int timeLimitSeconds)
        {
            turnDeadline = DateTime.UtcNow.AddSeconds(timeLimitSeconds);
        }

        private void HandleOpponentDisconnected()
        {
            opponentDisconnected = true;
            turnDeadline = null;
            RefreshConnectionStatus();
            RefreshCommandsInteractable();
        }

        // ターンの制限時間は、サーバーが戻った側の準備を待って数え直す(OnTurnStart)。
        private void HandleOpponentReconnected()
        {
            opponentDisconnected = false;
            RefreshConnectionStatus();
            RefreshCommandsInteractable();
        }

        private void HandleReconnecting()
        {
            reconnecting = true;
            resyncPending = matchStarted;
            turnDeadline = null;
            RefreshConnectionStatus();
            RefreshCommandsInteractable();
        }

        // 入力は、盤面の再送(OnMatchStart)が届くまで止めたままにする。
        private void HandleReconnected()
        {
            reconnecting = false;
            RefreshConnectionStatus();
            RefreshCommandsInteractable();
        }

        private void HandleConnectionLost()
        {
            turnDeadline = null;
            ShowConnectionLostAsync(lifetimeCancellation.Token).Forget();
        }

        private void RefreshConnectionStatus()
        {
            view.SetConnectionStatus(battleEnded ? null
                : reconnecting ? BattleMessageBuilder.Reconnecting
                : opponentDisconnected && matchStarted ? BattleMessageBuilder.OpponentDisconnected
                : null);
        }

        // 技・交代(強制交代を含む)を選べる間。行動を送った後・演出中・相手の交代待ちは含まない。
        private bool IsChoosingAction => CanAct
                                         || (forcedSwitchRequired && matchStarted && !battleEnded && pendingPlaybacks == 0
                                             && !reconnecting && !resyncPending && !opponentDisconnected);

        // 残り時間はクライアントで数えて表示するだけで、時間切れの判定はサーバーが行う。行動を選べる間だけ出す。
        private async UniTaskVoid RunTurnTimerAsync(CancellationToken cancellation)
        {
            while (true)
            {
                int? seconds = turnDeadline is { } deadline && IsChoosingAction
                    ? Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds))
                    : null;
                if (seconds != displayedTurnSeconds)
                {
                    displayedTurnSeconds = seconds;
                    view.SetTurnRemainingSeconds(seconds);
                }

                if (await UniTask.Delay(TimeSpan.FromSeconds(0.2), cancellationToken: cancellation)
                        .SuppressCancellationThrow())
                {
                    return;
                }
            }
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
                try
                {
                    await connection.ForfeitAsync();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    UnityEngine.Debug.LogWarning($"[Battle] 投了の送信に失敗しました: {e.Message}");
                    if (!reconnecting)
                    {
                        await systemDialog.ShowAsync("送信できませんでした", "降参を 送れませんでした。");
                    }
                }
            }
        }

        private void HandleBattleEnd(BattleEndPayload payload)
        {
            battleEnded = true;
            opponentSwitching = false;
            turnDeadline = null;
            RefreshConnectionStatus();
            RefreshCommandsInteractable();
            // 決着したターンの演出を流し終えてから結果Modalを出す。
            EnqueuePlayback(_ => ShowBattleResultAsync(payload));
        }

        private async UniTask ShowBattleResultAsync(BattleEndPayload payload)
        {
            await CloseSelectionModalAsync();
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

        // 次のターンの制限時間は、演出を流し終えて準備を送った後にサーバーが数え始める。
        private void HandleTurnResult(TurnResultPayload payload)
        {
            turnDeadline = null;
            EnqueuePlayback(cancellation => PlayTurnResultAsync(payload, cancellation));
        }

        // 演出を前の演出の後ろに並べる。ターン結果の直後に決着が届く等、演出中に次の出来事が来ても順番どおりに流す。
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
            // 相手の交代待ちのModalも、演出の前に閉じる。開いたままだと、相手が繰り出したパチモンがModalの裏で見えてしまう
            // (ボットのようにすぐ交代する相手では、Modalを開き終わる前に交代のターン結果が届く)。
            opponentSwitching = false;
            await CloseOpponentSwitchingModalAsync();

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

            BeginTurnInput(cancellation);
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
                    await view.PlaySendOutAsync(actorIsSelf, BuildUiState(), cancellation);
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

        // 技名 → 技の演出(HPゲージが減り終わるまで待つ) → 外れ/急所/効果 → 倒れる演出 → 倒れた、の順に流す。
        private async UniTask PlayMoveAsync(ActionResult action, bool actorIsSelf, CancellationToken cancellation)
        {
            var move = database.MovesDataTable.FindByMoveId(int.Parse(action.MoveId));
            await view.PlayMessageAsync(
                BattleMessageBuilder.MoveUsed(actorIsSelf, ActivePachimonName(actorIsSelf), move.Name), cancellation);

            ApplyAction(action);
            var effect = new MoveEffectDto
            {
                AttackerIsSelf = actorIsSelf,
                Category = move.Category,
                MoveType = move.MoveType,
                Hit = action.Hit,
                Critical = action.Critical,
                Effectiveness = action.Effectiveness,
            };
            await view.PlayMoveAsync(effect, BuildUiState(), cancellation);

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
                await view.PlayFaintAsync(targetIsSelf, cancellation);
                await view.PlayMessageAsync(
                    BattleMessageBuilder.Fainted(targetIsSelf, ActivePachimonName(targetIsSelf)), cancellation);
            }
        }

        private string ActivePachimonName(bool isSelf)
        {
            if (isSelf)
            {
                return database.PachimonDataTable.FindByPachimonId(selfPachimonIdBySlot[selfActiveIndex]).Name;
            }

            return opponentPachimonIdBySlot[opponentActiveIndex] is { } id
                ? database.PachimonDataTable.FindByPachimonId(id).Name
                : "???";
        }

        // 相手のパチモンは場に出て公開されるまで種族が分からないため、タイプもNone(色が決まらない)にする。
        private PachimonFigureDto BuildFigure(bool isSelf)
        {
            var pachimonId = isSelf ? selfPachimonIdBySlot[selfActiveIndex] : opponentPachimonIdBySlot[opponentActiveIndex];
            return new PachimonFigureDto
            {
                PachimonId = pachimonId,
                PrimaryType = pachimonId is { } id
                    ? database.PachimonDataTable.FindByPachimonId(id).PrimaryType
                    : PachimonType.None,
                IsFainted = IsActivePachimonFainted(isSelf),
            };
        }

        private bool IsActivePachimonFainted(bool isSelf)
        {
            return isSelf ? selfFaintedBySlot[selfActiveIndex] : opponentHpPercentBySlot[opponentActiveIndex] <= 0;
        }

        // opponentSwitchingに合わせてOpponentSwitchingModalを出し入れする。相手がすぐに交代すると、
        // Pushの途中で相手の交代が済むことがある。その場合はPushが終わってから閉じる。
        private async UniTaskVoid SyncOpponentSwitchingModalAsync()
        {
            if (opponentSwitching && openOpponentSwitching is null && !pushingOpponentSwitchingModal)
            {
                pushingOpponentSwitchingModal = true;
                var pushed = new UniTaskCompletionSource();
                opponentSwitchingPushed = pushed;
                try
                {
                    openOpponentSwitching = await screenNavigator.PushModalAsync<OpponentSwitchingPresenter>();
                }
                finally
                {
                    pushingOpponentSwitchingModal = false;
                    pushed.TrySetResult();
                }
            }

            if (!opponentSwitching)
            {
                await CloseOpponentSwitchingModalAsync();
            }
        }

        // 開いている途中なら、開き終わってから閉じる。
        private async UniTask CloseOpponentSwitchingModalAsync()
        {
            // 開く側(SyncOpponentSwitchingModalAsync)と演出の側から同時に待たれるため、複数で待てるUniTaskCompletionSourceで待つ
            // (async UniTaskのタスクは、終わる前に2か所からawaitすると例外になる)。
            if (opponentSwitchingPushed is { } pushed)
            {
                await pushed.Task;
            }

            if (openOpponentSwitching is null)
            {
                return;
            }

            var opponentSwitchingPresenter = openOpponentSwitching;
            openOpponentSwitching = null;
            await screenNavigator.PopModalAsync(opponentSwitchingPresenter);
        }

        // Move: 対象は行動側の相手。Switch: 対象は行動側自身の交代先。
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

        // 技を選んだ時点で命中/外れに関わらずPPを1消費する(Atlas.BattleCoreと同じ規則)。
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
                .Select(m => database.MovesDataTable.FindByMoveId(int.Parse(m.MoveId)))
                .ToArray();
        }

        // 変化技はダメージを与えないため相性を出さない。
        private string BuildMatchupText(MovesData move)
        {
            if (move.Category == MoveCategory.Status || opponentPachimonIdBySlot[opponentActiveIndex] is not { } opponentId)
            {
                return string.Empty;
            }

            var opponent = database.PachimonDataTable.FindByPachimonId(opponentId);
            return typeMatchupTable.Resolve(move.MoveType, opponent.PrimaryType, opponent.SecondaryType) switch
            {
                EffectivenessResult.DoubleSuperEffective => "超バツグン",
                EffectivenessResult.SuperEffective => "バツグン",
                EffectivenessResult.NotVeryEffective => "いまひとつ",
                EffectivenessResult.DoubleNotVeryEffective => "超いまひとつ",
                EffectivenessResult.Immune => "効果なし",
                _ => string.Empty,
            };
        }

        private BattleUIStateDto BuildUiState()
        {
            var selfPachimonId = selfPachimonIdBySlot[selfActiveIndex];
            var selfPachimon = database.PachimonDataTable.FindByPachimonId(selfPachimonId);
            var selfHpPercent = selfHpPercentBySlot[selfActiveIndex];
            var selfMaxHp = PachimonStatCalculator.CalculateHp(selfPachimon.BaseHp, selfStatPointsBySlot[selfActiveIndex].Hp);

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
                SelfFigure = BuildFigure(isSelf: true),
                OpponentFigure = BuildFigure(isSelf: false),
                Commands = selfMoves
                    .Select((m, i) => new CommandDto
                    {
                        SlotNo = i,
                        Name = m.Name,
                        TypeName = PachimonTypeNames.ToDisplayName(m.MoveType),
                        CurrentPp = selfCurrentPp[selfActiveIndex][i],
                        MaxPp = selfMoveSets[selfActiveIndex].Moves[i].MaxPp,
                        MatchupText = BuildMatchupText(m),
                    })
                    .ToList(),
            };
        }
    }
}
