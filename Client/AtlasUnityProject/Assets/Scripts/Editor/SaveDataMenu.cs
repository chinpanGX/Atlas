using System.IO;
using UnityEditor;
using UnityEngine;

namespace Atlas.Editor
{
    public static class SaveDataMenu
    {
        // Supplement FileStorageServiceの既定保存先(DefaultDirectoryName)。デバイス認証情報もここに入るため、
        // 削除すると次回起動時に新しいdevice_idでサインアップし直しになる。
        private const string SaveDataDirectoryName = "SaveData";

        [MenuItem("Tools/Delete SaveData")]
        private static void DeleteSaveData()
        {
            var path = Path.Combine(Application.persistentDataPath, SaveDataDirectoryName);
            if (!Directory.Exists(path))
            {
                Debug.Log($"SaveData not found: {path}");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Delete SaveData",
                    $"以下を削除します。次回起動時は新規プレイヤーとしてサインアップされます。\n{path}",
                    "削除",
                    "キャンセル"))
            {
                return;
            }

            Directory.Delete(path, true);
            Debug.Log($"SaveData deleted: {path}");
        }
    }
}
