using System;
using System.Collections.Generic;
using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Common
{
    // マスタのPachimonTypeは英語の識別子しか持たないため、画面表示用の名前はここで対応させる。
    public static class PachimonTypeNames
    {
        // パチモンのタイプ(1つまたは2つ)の表示名。SecondaryTypeがNoneなら1つだけ。
        public static IReadOnlyList<string> ToDisplayNames(PachimonType primaryType, PachimonType secondaryType)
        {
            return secondaryType == PachimonType.None
                ? new[] { ToDisplayName(primaryType) }
                : new[] { ToDisplayName(primaryType), ToDisplayName(secondaryType) };
        }

        public static string ToDisplayName(PachimonType type) => type switch
        {
            PachimonType.Normal => "ノーマル",
            PachimonType.Fire => "ほのお",
            PachimonType.Water => "みず",
            PachimonType.Electric => "でんき",
            PachimonType.Grass => "くさ",
            PachimonType.Ice => "こおり",
            PachimonType.Fighting => "かくとう",
            PachimonType.Poison => "どく",
            PachimonType.Ground => "じめん",
            PachimonType.Flying => "ひこう",
            PachimonType.Psychic => "エスパー",
            PachimonType.Bug => "むし",
            PachimonType.Rock => "いわ",
            PachimonType.Ghost => "ゴースト",
            PachimonType.Dragon => "ドラゴン",
            PachimonType.Dark => "あく",
            PachimonType.Steel => "はがね",
            PachimonType.Fairy => "フェアリー",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
