using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Training
{
    // ステータスタブ全体の表示。確定するまでは通信せず、画面の中で計算した値だけを出す。
    public sealed class TrainingStatsViewDto
    {
        public int RemainingPoints;
        // HP・こうげき・ぼうぎょ・とくこう・とくぼう・すばやさの順。
        public IReadOnlyList<TrainingStatDto> Stats = Array.Empty<TrainingStatDto>();
        // 増やしたポイントの合計(1ポイントにつき1枚)。
        public int RequiredTickets;
        public int TicketsBefore;
        // 足りないときは負になる。
        public int TicketsAfter;
        // 調整が無いときは「元に戻す」「確定する」を押せなくする。
        public bool IsDirty;
    }
}
