using System.Linq;
using UIPackages.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UIPackages.Editor
{
    /// <summary>
    /// UnityEngine.UI.Button を CommonButton に置換する。
    /// </summary>
    /// <remarks>
    /// コンポーネントを作り直すのではなく m_Script だけを差し替えるので、CommonButton が Button の派生である限り
    /// シリアライズ済みの設定(onClick・Transition・Navigation 等)も、他コンポーネントからの参照も維持される。
    /// </remarks>
    internal static class CommonButtonReplacer
    {
        private const string ContextMenuPath = "CONTEXT/Button/CommonButton に置換";
        private const string HierarchyMenuPath = "GameObject/UI Packages/子階層以下の Button を CommonButton に置換";

        [MenuItem(ContextMenuPath)]
        private static void ReplaceFromContextMenu(MenuCommand command)
        {
            ReplaceAll(new[] { (Button)command.context });
        }

        [MenuItem(ContextMenuPath, true)]
        private static bool ValidateReplaceFromContextMenu(MenuCommand command)
        {
            return command.context != null && command.context.GetType() == typeof(Button);
        }

        [MenuItem(HierarchyMenuPath, false, 0)]
        private static void ReplaceInSelection(MenuCommand command)
        {
            // Hierarchy の右クリックから複数選択で実行すると、選択オブジェクトの数だけ呼ばれるため先頭の1回だけ処理する
            if (command.context != null && Selection.gameObjects.Length > 1 && command.context != Selection.gameObjects[0])
                return;

            var buttons = Selection.gameObjects
                .SelectMany(go => go.GetComponentsInChildren<Button>(true))
                .Where(button => button.GetType() == typeof(Button))
                .Distinct()
                .ToArray();

            if (buttons.Length == 0)
            {
                Debug.Log("置換対象の Button が見つかりませんでした。");
                return;
            }

            ReplaceAll(buttons);
        }

        [MenuItem(HierarchyMenuPath, true)]
        private static bool ValidateReplaceInSelection()
        {
            return Selection.gameObjects.Length > 0;
        }

        private static void ReplaceAll(Button[] buttons)
        {
            var script = FindCommonButtonScript();
            if (script == null)
            {
                Debug.LogError("CommonButton の MonoScript が見つかりません。");
                return;
            }

            Undo.SetCurrentGroupName("Replace Button with CommonButton");
            var undoGroup = Undo.GetCurrentGroup();

            var replacedCount = buttons.Count(button => TryReplace(button, script));

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"{replacedCount}/{buttons.Length} 件の Button を CommonButton に置換しました。");
        }

        private static bool TryReplace(Button button, MonoScript script)
        {
            // プレハブインスタンス上では元プレハブ由来のコンポーネントのスクリプトを差し替えられない
            if (PrefabUtility.IsPartOfPrefabInstance(button) && !PrefabUtility.IsAddedComponentOverride(button))
            {
                Debug.LogWarning(
                    $"{button.name} の Button はプレハブ由来のため置換できません。元のプレハブを開いて置換してください。",
                    button
                );
                return false;
            }

            var serializedObject = new SerializedObject(button);
            serializedObject.FindProperty("m_Script").objectReferenceValue = script;
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        private static MonoScript FindCommonButtonScript()
        {
            return AssetDatabase.FindAssets($"t:MonoScript {nameof(CommonButton)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(script => script != null && script.GetClass() == typeof(CommonButton));
        }
    }
}
