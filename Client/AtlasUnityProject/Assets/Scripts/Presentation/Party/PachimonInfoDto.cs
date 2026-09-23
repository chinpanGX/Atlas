using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Party
{
    // 画面右側の詳細パネルに表示する、選択中パチモンの情報。
    public sealed class PachimonInfoDto
    {
        public string Name;
        // 単タイプなら1要素、複合タイプなら2要素。
        public IReadOnlyList<string> TypeNames = Array.Empty<string>();
        // HP・こうげき・ぼうぎょ・とくこう・とくぼう・すばやさの順。
        public IReadOnlyList<PachimonStatDto> Stats = Array.Empty<PachimonStatDto>();
        // 技が設定済みのスロットのみを持つ(未設定のslotは要素自体が無く、ブランク表示になる)。
        public IReadOnlyList<PachimonMoveDto> Moves = Array.Empty<PachimonMoveDto>();
    }
}
