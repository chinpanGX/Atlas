// このUnityプロジェクトのC#コンパイラはrecordのinitアクセサに必要な
// System.Runtime.CompilerServices.IsExternalInitを自動合成しないため、アセンブリごとに
// 自前で用意する(record/init専用構文を使うアセンブリすべてで必要な既知のワークアラウンド)。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
