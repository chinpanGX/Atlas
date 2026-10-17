using System.Collections.Generic;
using Atlas.Domain;
using Atlas.MasterData;
using Atlas.MasterData.Enums;
using Atlas.Presentation.Common;
using MasterMemory;
using ZLinq;

namespace Atlas.Presentation.Party
{
    // 詳細パネル(PachimonInfoView)の表示データを、マスターデータと技の割り当てから組み立てる。
    // パーティ編成(所持パチモンの技)とバトルの交代Modal(BattleServerから届いた技と残りPP)の両方で使う。
    public static class PachimonInfoDtoBuilder
    {
        // ステータスゲージの長さは、能力の値/取り得る最大値(種族値の上限255にステータスポイントを最大まで振った値)で表す。
        private const int MaxBaseStat = 255;
        private static readonly float MaxHp = PachimonStatCalculator.CalculateHp(MaxBaseStat, StatPoints.MaxTotal);
        private static readonly float MaxOther = PachimonStatCalculator.CalculateOther(MaxBaseStat, StatPoints.MaxTotal);

        public readonly struct MoveInput
        {
            // 1始まり(技スロット1〜4)。
            public readonly int Slot;
            public readonly int MoveId;
            public readonly int CurrentPp;

            public MoveInput(int slot, int moveId, int currentPp)
            {
                Slot = slot;
                MoveId = moveId;
                CurrentPp = currentPp;
            }
        }

        public static PachimonInfoDto Build(
            MemoryDatabase database, int pachimonId, StatPoints statPoints, IEnumerable<MoveInput> moves)
        {
            var master = database.PachimonDataTable.FindByPachimonId(pachimonId);

            return new PachimonInfoDto
            {
                Name = master.Name,
                TypeNames = PachimonTypeNames.ToDisplayNames(master.PrimaryType, master.SecondaryType),
                Stats = new[]
                {
                    CreateHpDto(PachimonStatCalculator.CalculateHp(master.BaseHp, statPoints.Hp)),
                    CreateOtherDto("こうげき", PachimonStatCalculator.CalculateOther(master.BaseAtk, statPoints.Atk)),
                    CreateOtherDto("ぼうぎょ", PachimonStatCalculator.CalculateOther(master.BaseDef, statPoints.Def)),
                    CreateOtherDto("とくこう", PachimonStatCalculator.CalculateOther(master.BaseSpatk, statPoints.Spatk)),
                    CreateOtherDto("とくぼう", PachimonStatCalculator.CalculateOther(master.BaseSpdef, statPoints.Spdef)),
                    CreateOtherDto("すばやさ", PachimonStatCalculator.CalculateOther(master.BaseSpeed, statPoints.Speed)),
                },
                Moves = moves
                    .Select(m =>
                    {
                        var move = database.MovesDataTable.FindByMoveId(m.MoveId);
                        return new PachimonMoveDto
                        {
                            Slot = m.Slot,
                            Name = move.Name,
                            TypeName = PachimonTypeNames.ToDisplayName(move.MoveType),
                            CurrentPp = m.CurrentPp,
                            MaxPp = move.MaxPp,
                        };
                    })
                    .ToList(),
            };
        }

        private static PachimonStatDto CreateHpDto(int value)
        {
            return new PachimonStatDto { Label = "HP", Value = value, Ratio = value / MaxHp };
        }

        private static PachimonStatDto CreateOtherDto(string label, int value)
        {
            return new PachimonStatDto { Label = label, Value = value, Ratio = value / MaxOther };
        }
    }
}
