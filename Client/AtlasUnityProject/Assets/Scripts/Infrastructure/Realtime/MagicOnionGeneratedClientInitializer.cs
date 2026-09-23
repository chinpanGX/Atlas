using MagicOnion.Client;

namespace Atlas.Infrastructure.Realtime
{
    // MagicOnion.ClientのSource GeneratorにIBattleHubのクライアント実装を生成させる(IL2CPPでは実行時の
    // 動的生成が使えないため、Unityではこの方式が推奨)。生成されたStreamingHubClientFactoryProviderを
    // StreamingHubClient.ConnectAsyncに渡す。
    [MagicOnionClientGeneration(typeof(Atlas.BattleContracts.IBattleHub))]
    internal partial class MagicOnionGeneratedClientInitializer
    {
    }
}
