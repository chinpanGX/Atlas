// UnityのC#コンパイラ(LangVersion 9固定)はrecordのinitアクセサに必要な
// System.Runtime.CompilerServices.IsExternalInitを自動合成しないため、アセンブリごとに
// 自前で用意する必要がある(record構文を使うアセンブリすべてで必要な既知のワークアラウンド)。
// dotnet build(Atlas.BattleCore.csproj、net10.0)側では標準ライブラリに既に存在するため
// 実害は無い。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
