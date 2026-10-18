using System.Threading;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Battle
{
    public sealed class BattlePage : Page
    {
        private const float ScreenShakeSeconds = 0.35f;

        [SerializeField] private BattleStageView stageView;
        [SerializeField] private SelfInfoView selfInfoView;
        [SerializeField] private OpponentInfoView opponentInfoView;
        [SerializeField] private CommandView commandView;
        [SerializeField] private BattleMessageView messageView;
        [SerializeField] private CommonButton forfeitButton;
        // 効果抜群・急所のときに画面ごと揺らす対象。
        [SerializeField] private RectTransform screenShakeTarget;
        [SerializeField] private GameObject turnTimer;
        [SerializeField] private TextMeshProUGUI turnTimerText;
        // 自分の再接続中・相手の切断中に出す帯。
        [SerializeField] private GameObject connectionStatus;
        [SerializeField] private TextMeshProUGUI connectionStatusText;

        // 押された技のSlotNoを流す。
        public Observable<int> OnMoveButtonClicked => commandView.OnMoveClicked;
        public Observable<Unit> OnSwitchButtonClicked => commandView.OnSwitchClicked;
        public Observable<Unit> OnForfeitButtonClicked => forfeitButton.OnClickAsObservable();

        // 対戦が始まるまで(選出中)は、HP等の表示とコマンドを隠す。prefabに置いた仮の表示が選出画面の裏に見えないようにするため。
        // 場のパチモンの姿は、表示を反映するまで透明になっている。
        public void HideBattlefield()
        {
            selfInfoView.gameObject.SetActive(false);
            opponentInfoView.gameObject.SetActive(false);
            commandView.SetVisible(false);
        }

        public void Refresh(BattleUIStateDto stateDto)
        {
            stageView.Refresh(stateDto.SelfFigure, stateDto.OpponentFigure);
            selfInfoView.Refresh(stateDto.SelfInfo);
            opponentInfoView.Refresh(stateDto.OpponentInfo);
            selfInfoView.gameObject.SetActive(true);
            opponentInfoView.gameObject.SetActive(true);
            commandView.Refresh(stateDto.Commands);
        }

        // 対戦の開始時の繰り出し。繰り出した側の姿とHP等の表示だけを出し、まだ繰り出していない側は隠したままにする。
        public UniTask PlayOpeningSendOutAsync(bool isSelf, BattleUIStateDto stateDto, CancellationToken cancellation)
        {
            if (isSelf)
            {
                selfInfoView.Refresh(stateDto.SelfInfo);
                selfInfoView.gameObject.SetActive(true);
            }
            else
            {
                opponentInfoView.Refresh(stateDto.OpponentInfo);
                opponentInfoView.gameObject.SetActive(true);
            }

            commandView.Refresh(stateDto.Commands);
            return stageView.PlayAppearAsync(isSelf, isSelf ? stateDto.SelfFigure : stateDto.OpponentFigure, cancellation);
        }

        // HPの変化をゲージが減る(増える)演出で見せ、動き終わるまで待つ。
        private UniTask RefreshAnimatedAsync(BattleUIStateDto stateDto, CancellationToken cancellation)
        {
            commandView.Refresh(stateDto.Commands);
            return UniTask.WhenAll(
                selfInfoView.AnimateAsync(stateDto.SelfInfo, cancellation),
                opponentInfoView.AnimateAsync(stateDto.OpponentInfo, cancellation));
        }

        // 技の演出(体当たり・ビーム)を流し、届いた瞬間から攻撃を受けた側の反応とHPゲージの減少を同時に見せる。
        // 外れた・効果が無いときは反応を出さない。
        public async UniTask PlayMoveAsync(MoveEffectDto effect, BattleUIStateDto stateDto, CancellationToken cancellation)
        {
            await stageView.PlayAttackAsync(effect, cancellation);

            var targetIsSelf = !effect.AttackerIsSelf;
            var reacts = effect.Hit && effect.Effectiveness != EffectivenessResult.Immune;
            await UniTask.WhenAll(
                stageView.PlayAttackRecoveryAsync(effect, cancellation),
                reacts ? stageView.PlayHitAsync(targetIsSelf, HitAmplitude(effect), cancellation) : UniTask.CompletedTask,
                reacts ? ShakeScreenAsync(effect, cancellation) : UniTask.CompletedTask,
                RefreshAnimatedAsync(stateDto, cancellation));
        }

        // 繰り出したパチモンを登場させる。場のパチモンが入れ替わるため、HP等の表示はすぐ反映する。
        public UniTask PlaySendOutAsync(bool isSelf, BattleUIStateDto stateDto, CancellationToken cancellation)
        {
            Refresh(stateDto);
            return stageView.PlayAppearAsync(isSelf, isSelf ? stateDto.SelfFigure : stateDto.OpponentFigure, cancellation);
        }

        public UniTask PlayFaintAsync(bool isSelf, CancellationToken cancellation)
        {
            return stageView.PlayFaintAsync(isSelf, cancellation);
        }

        // 行動(技・交代)の入力可否。行動送信後にターン結果が届くまでの間や、強制交代の選択中は押せなくする。
        // 投了はいつでもできるよう対象外。
        public void SetCommandsInteractable(bool interactable)
        {
            commandView.SetInteractable(interactable);
        }

        // 新しいターンの入力は「たたかう/こうたい」から始める。メッセージ枠は隠す。
        public void ShowCommandPanel()
        {
            messageView.Hide();
            commandView.SetVisible(true);
            commandView.ShowCommandPanel();
        }

        // コマンドを隠してメッセージを出したままにする(相手の行動待ち等、次の出来事が来るまで出し続ける文)。
        public void ShowMessage(string message)
        {
            commandView.SetVisible(false);
            messageView.Show(message);
        }

        // コマンドを隠してメッセージを出し、読み終わる(一定時間経つかタップされる)まで待つ。
        public UniTask PlayMessageAsync(string message, CancellationToken cancellation)
        {
            commandView.SetVisible(false);
            return messageView.ShowAndWaitAsync(message, cancellation);
        }

        // ターンの残り時間。nullなら隠す(対戦の開始前・決着後・接続の待ち中)。
        public void SetTurnRemainingSeconds(int? seconds)
        {
            turnTimer.SetActive(seconds.HasValue);
            if (seconds is { } value)
            {
                turnTimerText.text = $"のこり {value}秒";
            }
        }

        // nullなら隠す。
        public void SetConnectionStatus(string message)
        {
            connectionStatus.SetActive(message is not null);
            if (message is not null)
            {
                connectionStatusText.text = message;
            }
        }

        // 相性が良いほど・急所ほど、攻撃を受けた側を大きく揺らす。
        private static float HitAmplitude(MoveEffectDto effect)
        {
            var amplitude = effect.Effectiveness switch
            {
                EffectivenessResult.DoubleNotVeryEffective => 5f,
                EffectivenessResult.NotVeryEffective => 8f,
                EffectivenessResult.SuperEffective => 24f,
                EffectivenessResult.DoubleSuperEffective => 32f,
                _ => 14f,
            };
            return effect.Critical ? amplitude * 1.5f : amplitude;
        }

        // 画面ごとの揺れは効果抜群・急所のときだけ。
        private UniTask ShakeScreenAsync(MoveEffectDto effect, CancellationToken cancellation)
        {
            var amplitude = effect.Effectiveness switch
            {
                EffectivenessResult.DoubleSuperEffective => 20f,
                EffectivenessResult.SuperEffective => 12f,
                _ => effect.Critical ? 10f : 0f,
            };
            return amplitude > 0f
                ? ShakeMotion.PlayAsync(screenShakeTarget, amplitude, ScreenShakeSeconds, cancellation)
                : UniTask.CompletedTask;
        }
    }
}
