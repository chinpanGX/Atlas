using System;
using Atlas.MasterData.Enums;
using UnityEngine;

namespace Atlas.Presentation.Common
{
    // タイプごとの色。対戦画面の仮のパチモン(タイプ色の図形)と、特殊技のビームの色に使う。
    // UiPaletteの役割(UiColorRole)とは別に、マスタのPachimonTypeで引く。
    // Assets/Settings/PachimonTypePalette.assetを唯一のインスタンスとして使う。
    [CreateAssetMenu(fileName = "PachimonTypePalette", menuName = "Atlas/PachimonTypePalette")]
    public sealed class PachimonTypePalette : ScriptableObject
    {
        [SerializeField] private Color unknown = new(0.75f, 0.75f, 0.8f);
        [SerializeField] private Color normal = new(0.66f, 0.65f, 0.48f);
        [SerializeField] private Color fire = new(0.93f, 0.51f, 0.19f);
        [SerializeField] private Color water = new(0.39f, 0.56f, 0.94f);
        [SerializeField] private Color electric = new(0.97f, 0.82f, 0.17f);
        [SerializeField] private Color grass = new(0.48f, 0.78f, 0.3f);
        [SerializeField] private Color ice = new(0.59f, 0.85f, 0.84f);
        [SerializeField] private Color fighting = new(0.76f, 0.18f, 0.16f);
        [SerializeField] private Color poison = new(0.64f, 0.24f, 0.63f);
        [SerializeField] private Color ground = new(0.89f, 0.75f, 0.4f);
        [SerializeField] private Color flying = new(0.66f, 0.56f, 0.95f);
        [SerializeField] private Color psychic = new(0.98f, 0.33f, 0.53f);
        [SerializeField] private Color bug = new(0.65f, 0.73f, 0.1f);
        [SerializeField] private Color rock = new(0.71f, 0.63f, 0.21f);
        [SerializeField] private Color ghost = new(0.45f, 0.34f, 0.59f);
        [SerializeField] private Color dragon = new(0.44f, 0.21f, 0.99f);
        [SerializeField] private Color dark = new(0.44f, 0.34f, 0.27f);
        [SerializeField] private Color steel = new(0.72f, 0.72f, 0.81f);
        [SerializeField] private Color fairy = new(0.84f, 0.52f, 0.68f);

        // Noneは相手の未公開のパチモンのように、タイプが分からないときに使う。
        public Color Get(PachimonType type) => type switch
        {
            PachimonType.None => unknown,
            PachimonType.Normal => normal,
            PachimonType.Fire => fire,
            PachimonType.Water => water,
            PachimonType.Electric => electric,
            PachimonType.Grass => grass,
            PachimonType.Ice => ice,
            PachimonType.Fighting => fighting,
            PachimonType.Poison => poison,
            PachimonType.Ground => ground,
            PachimonType.Flying => flying,
            PachimonType.Psychic => psychic,
            PachimonType.Bug => bug,
            PachimonType.Rock => rock,
            PachimonType.Ghost => ghost,
            PachimonType.Dragon => dragon,
            PachimonType.Dark => dark,
            PachimonType.Steel => steel,
            PachimonType.Fairy => fairy,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
