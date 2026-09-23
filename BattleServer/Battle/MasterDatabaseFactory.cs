using Atlas.MasterData;

namespace Atlas.BattleServer.Battle
{
    // master-data-pipelineが配置したmasterdata.bytes(MasterData/Atlas.MasterData.csprojが出力先へコピー)を
    // 読み込んでMemoryDatabaseを構築する。起動時に1回だけ読み込み、更新の反映は再起動で行う
    // (Rust側・Clientと同じ方針、architecture.md「マスターデータ運用」参照)。
    public static class MasterDatabaseFactory
    {
        public const string PathConfigKey = "MasterData:Path";

        // 相対パスはAppContext.BaseDirectory(出力先)基準で解決する。
        public const string DefaultPath = "MasterData/masterdata.bytes";

        public static MemoryDatabase Load(IConfiguration configuration)
        {
            var path = configuration[PathConfigKey];
            if (string.IsNullOrEmpty(path))
            {
                path = DefaultPath;
            }
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, path);
            }
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"masterdata.bytes not found: {path}");
            }

            // スキーマ変更後にLoader(ContentHash)とbytesの配置がずれていると、ここで復号に失敗する。
            return MasterDataLoader.Load(File.ReadAllBytes(path));
        }
    }
}
