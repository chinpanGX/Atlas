using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Atlas.MasterData.Enums;
using Atlas.Presentation.Common;
using SmartAddresser.Editor.Core.Models.LayoutRules;
using SmartAddresser.Editor.Core.Models.Services;
using SmartAddresser.Editor.Core.Tools.Shared;
using SmartAddresser.Editor.Foundation.AddressableAdapter;
using SmartAddresser.Editor.Foundation.AssetDatabaseAdapter;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Editor.Thumbnail
{
    // パチモンの画像が用意されるまでの仮のサムネイル(主タイプの色の円にpachimon_idを書いたもの)を作る。
    // pachimonマスタのCSV(正本)にあるIDのうち、サムネイルがまだ無いものだけを作る(既存の画像は上書きしない)。
    // 文字はプロジェクトのTMPフォントで描くため、UIをカメラでRenderTextureへ描いて読み出す。
    public static class PachimonThumbnailGenerator
    {
        public const string OutputRoot = "Assets/Addressables/Thumbnail";

        private const int Size = 256;
        private const float FontSize = 84f;
        // 明るいタイプ色(でんき等)の上では白い文字が読めないため、黒い文字にする境目の輝度。
        private const float DarkTextLuminance = 0.6f;
        private const int RenderLayer = 31;
        // プロジェクトのルートから見たpachimonマスタのCSV。
        private const string PachimonCsvPath = "../../Shared/master-data/csv/pachimon_master.csv";
        private const string CircleSpritePath = "Assets/Addressables/Texture/circle_white_256.png";
        private const string FontAssetPath = "Assets/Addressables/Fonts/NotoSansJP-Medium SDF.asset";
        private const string TypePalettePath = "Assets/Settings/PachimonTypePalette.asset";
        private const string LayoutRuleDataPath = "Assets/LayoutRuleData.asset";

        public static string ThumbnailPath(int pachimonId) => $"{OutputRoot}/{pachimonId}/pachimon_{pachimonId}_thumb.png";

        [MenuItem("Tools/Generate Pachimon Thumbnails")]
        private static void GenerateMissingFromMenu()
        {
            Debug.Log(GenerateMissing());
        }

        // uloop execute-dynamic-codeから呼ぶ入口。作ったIDと飛ばした数を返す。
        public static string GenerateMissing()
        {
            return Generate(overwrite: false);
        }

        // 見た目を変えたときなどに、既存のサムネイルも作り直す。
        public static string RegenerateAll()
        {
            return Generate(overwrite: true);
        }

        private static string Generate(bool overwrite)
        {
            var pachimon = LoadPachimonFromCsv();
            var targets = pachimon.Where(p => overwrite || !File.Exists(ThumbnailPath(p.Id))).ToList();
            if (targets.Count > 0)
            {
                WriteThumbnails(targets);
            }

            // 手で置いた画像も登録されるよう、生成の有無に関わらずサムネイルのフォルダ全体に当てる。
            var entryCount = ApplyAddressableLayout();
            var generated = targets.Count == 0
                ? "No thumbnails to generate"
                : $"Generated {targets.Count} thumbnails: {string.Join(", ", targets.Select(t => t.Id))}";
            return $"{generated} (skipped {pachimon.Count - targets.Count} existing, {entryCount} addressable entries)";
        }

        private static void WriteThumbnails(IReadOnlyList<PachimonEntry> targets)
        {
            var palette = AssetDatabase.LoadAssetAtPath<PachimonTypePalette>(TypePalettePath);
            var circle = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (palette == null || circle == null || font == null)
            {
                throw new InvalidOperationException(
                    $"Missing asset: palette={palette != null}, circle={circle != null}, font={font != null}");
            }

            using (var renderer = new ThumbnailRenderer(circle, font))
            {
                foreach (var target in targets)
                {
                    var path = ThumbnailPath(target.Id);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, renderer.Render(target.Id.ToString(), palette.Get(target.PrimaryType)));
                }
            }

            // 取り込み時の設定(Sprite等)はPachimonThumbnailImporterが行う。
            AssetDatabase.Refresh();
        }

        // SmartAddresserのレイアウトルール(アドレス=ファイル名、ラベル=thumbnail_{id}でIDごとのバンドル)を当てる。
        // このプロジェクトはSmartAddresserのPrimary Dataを設定しておらず取り込み時に自動では当たらないため、
        // サムネイルの画像にだけ明示的に当てる(他のアセットの登録は変えない)。
        private static int ApplyAddressableLayout()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var layoutRule = AssetDatabase.LoadAssetAtPath<LayoutRuleData>(LayoutRuleDataPath).LayoutRule;
            layoutRule.Setup();
            var applyService = new ApplyLayoutRuleService(layoutRule, new VersionExpressionParserRepository().Load(),
                new AddressableAssetSettingsAdapter(settings), new AssetDatabaseAdapter());

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { OutputRoot });
            foreach (var guid in guids)
            {
                applyService.Apply(guid, false, false);
            }

            applyService.InvokeBatchModificationEvent();
            AssetDatabase.SaveAssets();
            return guids.Length;
        }

        private readonly struct PachimonEntry
        {
            public readonly int Id;
            public readonly PachimonType PrimaryType;

            public PachimonEntry(int id, PachimonType primaryType)
            {
                Id = id;
                PrimaryType = primaryType;
            }
        }

        // CSVは先頭にタイトル・出典・型の行があり、その後に列名の行が来る。タイプは表示名(ほのお等)で書かれている。
        private static List<PachimonEntry> LoadPachimonFromCsv()
        {
            var lines = File.ReadAllLines(PachimonCsvPath);
            var headerIndex = Array.FindIndex(lines, line => line.StartsWith("pachimon_id,", StringComparison.Ordinal));
            if (headerIndex < 0)
            {
                throw new InvalidOperationException($"Header row not found: {PachimonCsvPath}");
            }

            var header = lines[headerIndex].Split(',');
            var idColumn = Array.IndexOf(header, "pachimon_id");
            var typeColumn = Array.IndexOf(header, "primary_type");
            var typeByName = Enum.GetValues(typeof(PachimonType)).Cast<PachimonType>()
                .Where(type => type != PachimonType.None)
                .ToDictionary(PachimonTypeNames.ToDisplayName);

            return lines.Skip(headerIndex + 1)
                .Where(line => !string.IsNullOrWhiteSpace(line.Replace(",", string.Empty)))
                .Select(line => line.Split(','))
                .Select(columns => new PachimonEntry(
                    int.Parse(columns[idColumn]),
                    typeByName.TryGetValue(columns[typeColumn], out var type) ? type : PachimonType.None))
                .ToList();
        }

        // 円の画像とIDの文字をワールド空間のCanvasに置き、正射影のカメラで1枚ずつ描く。
        private sealed class ThumbnailRenderer : IDisposable
        {
            private readonly GameObject root;
            private readonly Camera camera;
            private readonly Image circleImage;
            private readonly TextMeshProUGUI idText;
            private readonly RenderTexture renderTexture;
            private readonly Texture2D readback;

            public ThumbnailRenderer(Sprite circle, TMP_FontAsset font)
            {
                root = new GameObject("PachimonThumbnailRenderer") { hideFlags = HideFlags.HideAndDontSave };
                // シーンにあるものが写り込まないよう、離れた位置の専用レイヤーだけを写す。
                root.transform.position = new Vector3(0f, -10000f, 0f);

                camera = CreateChild("Camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Size / 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << RenderLayer;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.transform.localPosition = new Vector3(0f, 0f, -10f);

                var canvasObject = CreateChild("Canvas", typeof(RectTransform));
                var canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(Size, Size);

                circleImage = CreateFullRect("Circle", canvasObject.transform).AddComponent<Image>();
                circleImage.sprite = circle;

                idText = CreateFullRect("Id", canvasObject.transform).AddComponent<TextMeshProUGUI>();
                idText.font = font;
                idText.fontSize = FontSize;
                idText.alignment = TextAlignmentOptions.Center;
                idText.textWrappingMode = TextWrappingModes.NoWrap;

                renderTexture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = renderTexture;
                readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            }

            public byte[] Render(string text, Color circleColor)
            {
                circleImage.color = circleColor;
                idText.text = text;
                idText.color = Luminance(circleColor) > DarkTextLuminance ? Color.black : Color.white;
                Canvas.ForceUpdateCanvases();
                idText.ForceMeshUpdate();
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = previous;

                // 透明な背景に重ねて描くと縁の色が黒に寄る(アルファを掛けた色になる)ため、アルファで割り戻す。
                var pixels = readback.GetPixels();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var a = pixels[i].a;
                    if (a > 0f && a < 1f)
                    {
                        pixels[i] = new Color(pixels[i].r / a, pixels[i].g / a, pixels[i].b / a, a);
                    }
                }

                readback.SetPixels(pixels);
                readback.Apply();
                return readback.EncodeToPNG();
            }

            public void Dispose()
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(root);
            }

            private GameObject CreateChild(string name, params Type[] components)
            {
                var child = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave, layer = RenderLayer };
                child.transform.SetParent(root.transform, false);
                return child;
            }

            private GameObject CreateFullRect(string name, Transform parent)
            {
                var child = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave, layer = RenderLayer };
                var rect = (RectTransform)child.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return child;
            }

            private static float Luminance(Color color) => 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
        }
    }
}
