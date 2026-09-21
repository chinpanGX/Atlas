using System.Runtime.CompilerServices;

// EditModeテスト(Atlas.BattleCore.Tests)から内部状態(BattleSide.RequiresForcedSwitch等)を
// 直接検証できるようにする。
[assembly: InternalsVisibleTo("Atlas.BattleCore.Tests")]
