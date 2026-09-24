using System.Collections.Generic;
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
        // 種族値の上限。ステータスゲージの長さは種族値/この値で表す(実効値はレベル固定で種族値に比例するため)。
        private const float MaxBaseStat = 255f;

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

        public static PachimonInfoDto Build(MemoryDatabase database, int pachimonId, IEnumerable<MoveInput> moves)
        {
            var master = database.PachimonDataTable.FindByPachimonId(pachimonId);

            var typeNames = new List<string> { PachimonTypeNames.ToDisplayName(master.PrimaryType) };
            if (master.SecondaryType != PachimonType.None)
            {
                typeNames.Add(PachimonTypeNames.ToDisplayName(master.SecondaryType));
            }

            return new PachimonInfoDto
            {
                Name = master.Name,
                TypeNames = typeNames,
                Stats = new[]
                {
                    CreateStatDto("HP", PachimonStatCalculator.CalculateHp(master.BaseHp), master.BaseHp),
                    CreateStatDto("こうげき", PachimonStatCalculator.CalculateOther(master.BaseAtk), master.BaseAtk),
                    CreateStatDto("ぼうぎょ", PachimonStatCalculator.CalculateOther(master.BaseDef), master.BaseDef),
                    CreateStatDto("とくこう", PachimonStatCalculator.CalculateOther(master.BaseSpatk), master.BaseSpatk),
                    CreateStatDto("とくぼう", PachimonStatCalculator.CalculateOther(master.BaseSpdef), master.BaseSpdef),
                    CreateStatDto("すばやさ", PachimonStatCalculator.CalculateOther(master.BaseSpeed), master.BaseSpeed),
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

        private static PachimonStatDto CreateStatDto(string label, int value, int baseStat)
        {
            return new PachimonStatDto { Label = label, Value = value, Ratio = baseStat / MaxBaseStat };
        }
    }
}
