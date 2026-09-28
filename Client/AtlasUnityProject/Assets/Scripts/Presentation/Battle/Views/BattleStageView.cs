using System.Threading;
using Atlas.MasterData.Enums;
using Atlas.Presentation.Common;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    // 自分と相手のパチモンの姿を並べ、技の演出(物理は体当たり、特殊はビーム)と攻撃を受けた側の反応を見せる。
    // 体当たりとビームは相手に届いた時点で一度返し、戻りの動きと攻撃を受けた側の反応はその後に同時に流す
    // (HPゲージを当たった瞬間から減らし始めるため)。
    public sealed class BattleStageView : MonoBehaviour
    {
        private const float TackleSeconds = 0.15f;
        private const float TackleReturnSeconds = 0.3f;
        // 体当たりで相手の図形にどれだけ重なるまで近づくか(両者の大きさの平均に対する割合)。
        private const float TackleContactRatio = 0.6f;
        private const float BeamExtendSeconds = 0.25f;
        private const float BeamFadeSeconds = 0.3f;

        [SerializeField] private PachimonFigureView selfFigure;
        [SerializeField] private PachimonFigureView opponentFigure;
        // 左端を根元にして伸ばすため、pivotは(0, 0.5)にしておく。
        [SerializeField] private Image beam;
        [SerializeField] private PachimonTypePalette typePalette;

        private void Awake()
        {
            beam.gameObject.SetActive(false);
        }

        public void Refresh(PachimonFigureDto self, PachimonFigureDto opponent)
        {
            selfFigure.Refresh(self);
            opponentFigure.Refresh(opponent);
        }

        public UniTask PlayAppearAsync(bool isSelf, PachimonFigureDto dto, CancellationToken cancellation)
        {
            return Figure(isSelf).PlayAppearAsync(dto, cancellation);
        }

        public UniTask PlayFaintAsync(bool isSelf, CancellationToken cancellation)
        {
            return Figure(isSelf).PlayFaintAsync(cancellation);
        }

        // 技が相手に届くまで(体当たりがぶつかる・ビームが届く)を流す。
        public UniTask PlayAttackAsync(MoveEffectDto effect, CancellationToken cancellation)
        {
            return effect.Category switch
            {
                MoveCategory.Physical => TackleAsync(effect.AttackerIsSelf, cancellation),
                MoveCategory.Special => ExtendBeamAsync(effect, cancellation),
                _ => UniTask.CompletedTask,
            };
        }

        // 攻撃側の戻り(体当たりから戻る・ビームが消える)を流す。
        public UniTask PlayAttackRecoveryAsync(MoveEffectDto effect, CancellationToken cancellation)
        {
            return effect.Category switch
            {
                MoveCategory.Physical => ReturnFromTackleAsync(effect.AttackerIsSelf, cancellation),
                MoveCategory.Special => FadeBeamAsync(cancellation),
                _ => UniTask.CompletedTask,
            };
        }

        public UniTask PlayHitAsync(bool targetIsSelf, float amplitude, CancellationToken cancellation)
        {
            return Figure(targetIsSelf).PlayHitAsync(amplitude, cancellation);
        }

        private PachimonFigureView Figure(bool isSelf) => isSelf ? selfFigure : opponentFigure;

        private UniTask TackleAsync(bool attackerIsSelf, CancellationToken cancellation)
        {
            var attacker = Figure(attackerIsSelf);
            var target = Figure(!attackerIsSelf);
            var from = attacker.HomePosition;
            var toTarget = target.HomePosition - from;
            var contactDistance = (attacker.RectTransform.rect.width + target.RectTransform.rect.width) * 0.5f * TackleContactRatio;
            var to = from + toTarget.normalized * Mathf.Max(0f, toTarget.magnitude - contactDistance);
            // ぶつかる側を相手の図形より手前に描く。
            attacker.transform.SetAsLastSibling();
            return LMotion.Create(from, to, TackleSeconds)
                .WithEase(Ease.InQuad)
                .WithOnCancel(() => attacker.RectTransform.anchoredPosition = from)
                .Bind(position => attacker.RectTransform.anchoredPosition = position)
                .AddTo(this)
                .ToUniTask(cancellation);
        }

        private UniTask ReturnFromTackleAsync(bool attackerIsSelf, CancellationToken cancellation)
        {
            var attacker = Figure(attackerIsSelf);
            var home = attacker.HomePosition;
            return LMotion.Create(attacker.RectTransform.anchoredPosition, home, TackleReturnSeconds)
                .WithEase(Ease.OutQuad)
                .WithOnCancel(() => attacker.RectTransform.anchoredPosition = home)
                .Bind(position => attacker.RectTransform.anchoredPosition = position)
                .AddTo(this)
                .ToUniTask(cancellation);
        }

        // 攻撃側から相手へ向けて、技のタイプの色の帯を伸ばす。
        private UniTask ExtendBeamAsync(MoveEffectDto effect, CancellationToken cancellation)
        {
            var from = Figure(effect.AttackerIsSelf).HomePosition;
            var toTarget = Figure(!effect.AttackerIsSelf).HomePosition - from;
            var beamTransform = beam.rectTransform;
            beamTransform.anchoredPosition = from;
            beamTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg);
            beam.color = typePalette.Get(effect.MoveType);
            beam.gameObject.SetActive(true);

            var height = beamTransform.sizeDelta.y;
            return LMotion.Create(0f, toTarget.magnitude, BeamExtendSeconds)
                .WithEase(Ease.OutQuad)
                .WithOnCancel(() => beam.gameObject.SetActive(false))
                .Bind(width => beamTransform.sizeDelta = new Vector2(width, height))
                .AddTo(this)
                .ToUniTask(cancellation);
        }

        private UniTask FadeBeamAsync(CancellationToken cancellation)
        {
            var color = beam.color;
            return LMotion.Create(1f, 0f, BeamFadeSeconds)
                .WithOnComplete(() => beam.gameObject.SetActive(false))
                .WithOnCancel(() => beam.gameObject.SetActive(false))
                .Bind(alpha =>
                {
                    color.a = alpha;
                    beam.color = color;
                })
                .AddTo(this)
                .ToUniTask(cancellation);
        }
    }
}
