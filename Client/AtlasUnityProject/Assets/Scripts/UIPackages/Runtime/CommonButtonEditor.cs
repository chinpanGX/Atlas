#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

namespace UIPackages.Runtime
{
    [UnityEditor.CustomEditor(typeof(CommonButton))]
    class CommonButtonEditor : UnityEditor.UI.ButtonEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            DrawSynchronizeGraphics(target as CommonButton);
            serializedObject.ApplyModifiedProperties();
        }

        private void GUIPropertySafe(string propName, UnityEditor.SerializedObject serializedObject)
        {
            var prop = serializedObject.FindProperty(propName);
            if (prop != null)
            {
                UnityEditor.EditorGUILayout.PropertyField(prop);
            }
        }

        private void DrawSynchronizeGraphics<T>(T targetButton) where T : Button
        {
            var synchronizeTintColorGraphicsPropName = nameof(CommonButton.synchronizeTintColorGraphics);
            var synchronizeTintColorGraphicsProp = serializedObject.FindProperty(synchronizeTintColorGraphicsPropName);
            GUIPropertySafe(synchronizeTintColorGraphicsPropName, serializedObject);

            if (GUILayout.Button("子階層以下に存在する全てのGraphicを設定"))
            {
                if (synchronizeTintColorGraphicsProp == null)
                    return;

                var currentTargetGraphic = targetButton.targetGraphic;

                synchronizeTintColorGraphicsProp.arraySize = 0;

                var childGraphics = targetButton.GetComponentsInChildren<Graphic>(true);
                foreach (var graphic in childGraphics)
                {
                    if (graphic != currentTargetGraphic && graphic.gameObject != targetButton.gameObject)
                    {
                        if (synchronizeTintColorGraphicsProp != null)
                        {
                            synchronizeTintColorGraphicsProp.InsertArrayElementAtIndex(
                                synchronizeTintColorGraphicsProp.arraySize
                            );
                            synchronizeTintColorGraphicsProp
                                .GetArrayElementAtIndex(synchronizeTintColorGraphicsProp.arraySize - 1)
                                .objectReferenceValue = graphic;
                        }
                    }
                }

                UnityEditor.EditorUtility.SetDirty(targetButton);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
#endif