using System;
using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Common
{
    // マスタのPachimonTypeは英語の識別子しか持たないため、画面表示用の名前はここで対応させる。
    public static class PachimonTypeNames
    {
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
