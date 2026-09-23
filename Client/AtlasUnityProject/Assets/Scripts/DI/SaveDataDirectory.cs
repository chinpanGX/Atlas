using System;

namespace Atlas.DI
{
    // セーブデータ(deviceCredentials等)の保存先ディレクトリ名を、起動しているインスタンスごとに分ける。
    // 同じ保存先を読むと同じdevice_id(=同じプレイヤー)になり、自分自身とマッチングしようとしてしまうため、
    // 同じPCで2人分を動かす確認方法(Multiplayer Play Mode / ビルド+Editor)ではインスタンスごとに分ける必要がある。
    //
    // - Editor本体: 既定のまま(nullを返す)。既存の開発用アカウントをそのまま使う
    // - Multiplayer Play Modeの追加Editorインスタンス: プロジェクトのLibrary/VP/<id>/配下で動くため、その<id>ごと
    // - スタンドアロンビルド: Editorと同じpersistentDataPathを使うためSaveData_Buildにし、起動引数
    //   「-saveSlot N」を付けるとSaveData_Build_Nにする(ビルドを複数同時に起動する場合)
    internal static class SaveDataDirectory
    {
        private const string VirtualPlayerFolder = "/Library/VP/";
        private const string SaveSlotArgument = "-saveSlot";

        public static string Resolve()
        {
            if (UnityEngine.Application.isEditor)
            {
                var dataPath = UnityEngine.Application.dataPath.Replace('\\', '/');
                var index = dataPath.IndexOf(VirtualPlayerFolder, StringComparison.Ordinal);
                if (index < 0)
                {
                    return null;
                }

                var virtualPlayerId = dataPath.Substring(index + VirtualPlayerFolder.Length).Split('/')[0];
                return $"SaveData_VP_{virtualPlayerId}";
            }

            var args = Environment.GetCommandLineArgs();
            var slotIndex = Array.IndexOf(args, SaveSlotArgument);
            return slotIndex >= 0 && slotIndex + 1 < args.Length
                ? $"SaveData_Build_{args[slotIndex + 1]}"
                : "SaveData_Build";
        }
    }
}
