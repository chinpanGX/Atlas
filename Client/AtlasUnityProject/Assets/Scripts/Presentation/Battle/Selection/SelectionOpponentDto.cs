using System;
using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    // 相手のパーティの1体。選出画面では種族だけ見せる(技・選出するかどうかは分からない)。
    public sealed class SelectionOpponentDto
    {
        public string Name;
        public IReadOnlyList<string> TypeNames = Array.Empty<string>();
    }
}
