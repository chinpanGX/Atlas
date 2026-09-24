using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Atlas.Editor.FontBaking
{
    /// <summary>
    /// NotoSansJPのTMPフォントアセットに、使う文字を焼き込んでStaticアセットにする。
    /// Dynamicのままだと、Play中に文字を追加するたびにアセットファイルが書き換わる。Multiplayer Play Modeの
    /// 追加インスタンスはAssetsを共有しているため、互いの書き換えを読み込んで文字テーブルとアトラスがずれ、文字化けする。
    /// 焼き込む文字: JIS第1水準(JisLevel1Characters.txt) + ASCII + マスターデータCSV・Assets内のプレハブ/シーン/C#に出てくる文字。
    /// UIやマスターデータに新しい文字を追加したら、このメニューで焼き直す。
    /// </summary>
    public static class StaticFontBaker
    {
        private const string FontAssetPath = "Assets/Addressables/Fonts/NotoSansJP-Medium SDF.asset";
        private const string BaseCharactersPath = "Assets/Scripts/Editor/FontBaking/JisLevel1Characters.txt";
        private const string MasterDataCsvDirectory = "../../Shared/master-data/csv";

        // 1枚2048x2048に約1700文字入る大きさ。足りない分はマルチアトラスで2枚目以降に入る
        private const int SamplingPointSize = 40;
        private const int AtlasPadding = 4;

        private static readonly string[] ScannedAssetPatterns = { "*.cs", "*.prefab", "*.unity" };
        private static readonly Regex UnicodeEscape = new(@"\\u([0-9A-Fa-f]{4})", RegexOptions.Compiled);

        [MenuItem("Tools/Bake Static Font")]
        public static void Bake()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError($"Font asset not found: {FontAssetPath}");
                return;
            }

            var characters = CollectCharacters();

            ResetAsDynamic(fontAsset);
            fontAsset.TryAddCharacters(characters, out var missing);

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            var serialized = new SerializedObject(fontAsset);
            serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            // フォントに無い文字(主にC#コメント中の記号等)は焼き込めない。UIで使う文字が含まれていないか確認する
            var missingChars = missing?.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray() ?? new char[0];
            Debug.Log($"Baked {fontAsset.characterTable.Count} characters into {fontAsset.atlasTextures.Length} atlas texture(s): {FontAssetPath}");
            if (missingChars.Length > 0)
            {
                Debug.LogWarning($"Not in source font ({missingChars.Length}): {new string(missingChars)}");
            }
        }

        // 焼き込みはDynamicのTryAddCharactersで行うため、一度Dynamicに戻してサンプリングサイズ・余白を設定し直して空にする
        private static void ResetAsDynamic(TMP_FontAsset fontAsset)
        {
            FontEngine.LoadFontFace(fontAsset.sourceFontFile, SamplingPointSize);
            fontAsset.faceInfo = FontEngine.GetFaceInfo();

            var serialized = new SerializedObject(fontAsset);
            serialized.FindProperty("m_AtlasPadding").intValue = AtlasPadding;
            serialized.FindProperty("m_CreationSettings.pointSize").intValue = SamplingPointSize;
            serialized.FindProperty("m_CreationSettings.padding").intValue = AtlasPadding;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // SDFのシェーダーは余白+1をGradientScaleとして使う(TMPがアセット作成時に設定するのと同じ値)
            fontAsset.material.SetFloat(ShaderUtilities.ID_GradientScale, AtlasPadding + 1);

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.ClearFontAssetData();
        }

        private static string CollectCharacters()
        {
            var set = new SortedSet<int>();
            AddText(set, File.ReadAllText(BaseCharactersPath, Encoding.UTF8));

            for (var c = 0x20; c <= 0x7E; c++)
            {
                set.Add(c);
            }

            var csvDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", MasterDataCsvDirectory));
            foreach (var path in Directory.GetFiles(csvDirectory, "*.csv"))
            {
                AddText(set, File.ReadAllText(path, Encoding.UTF8));
            }

            foreach (var pattern in ScannedAssetPatterns)
            {
                foreach (var path in Directory.GetFiles(Application.dataPath, pattern, SearchOption.AllDirectories))
                {
                    // シーン・プレハブのYAMLでは非ASCII文字が\uXXXXでエスケープされることがある
                    var text = UnicodeEscape.Replace(File.ReadAllText(path, Encoding.UTF8),
                        m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());
                    AddText(set, text);
                }
            }

            return string.Concat(set.Select(char.ConvertFromUtf32));
        }

        private static void AddText(SortedSet<int> set, string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var codePoint = char.ConvertToUtf32(text, i);
                if (char.IsSurrogatePair(text, i))
                {
                    i++;
                }

                if (codePoint >= 0x20 && !char.IsControl(char.ConvertFromUtf32(codePoint), 0))
                {
                    set.Add(codePoint);
                }
            }
        }
    }
}
