using UnityEditor;

namespace Atlas.Editor.Thumbnail
{
    // サムネイルのフォルダに初めて取り込まれた画像を、UIのImageで使うSpriteとして取り込む。
    // 初回だけ設定するため、後からInspectorで変えた設定はそのまま残る。
    public sealed class PachimonThumbnailImporter : AssetPostprocessor
    {
        private const int MaxSize = 256;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PachimonThumbnailGenerator.OutputRoot + "/") || !assetImporter.importSettingsMissing)
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = MaxSize;
        }
    }
}
