using UnityEditor;
using UnityEngine;

namespace NoClue.EditorTools
{
    /// <summary>
    /// Import rules for the game's own resources: card art becomes 512px sprites,
    /// the long intro track streams from disk instead of loading 36 MB into memory.
    /// </summary>
    internal class NoClueAssetImporter : AssetPostprocessor
    {
        private const string CardsPath = "Assets/Resources/Cards/";
        private const string DicePath = "Assets/Resources/Dice/";
        private const string ThemePath = "Assets/Resources/Theme/";
        private const string AudioPath = "Assets/Resources/Audio/";

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(ThemePath))
            {
                // UI theme art: full-size sprites; frames and buttons are 9-sliced so they stretch cleanly.
                var theme = (TextureImporter)assetImporter;
                theme.textureType = TextureImporterType.Sprite;
                theme.spriteImportMode = SpriteImportMode.Single;
                theme.mipmapEnabled = false;
                theme.maxTextureSize = 2048;
                theme.textureCompression = TextureImporterCompression.Compressed;
                theme.alphaIsTransparency = true;
                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                theme.spriteBorder = file switch
                {
                    "panel_frame" => new Vector4(110, 110, 110, 110),   // left, bottom, right, top (px)
                    "button" => new Vector4(48, 48, 48, 48),
                    _ => Vector4.zero,
                };
                return;
            }

            if (!assetPath.StartsWith(CardsPath) && !assetPath.StartsWith(DicePath)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaIsTransparency = true;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioPath)) return;

            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            if (assetPath.Contains("introbackground") || assetPath.StartsWith(AudioPath + "Game/"))
            {
                // music: stream from disk instead of holding tens of MB in memory
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
            }
            else
            {
                // short sound effects: decoded up front so they play with no delay
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.ADPCM;
            }
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
        }
    }
}
