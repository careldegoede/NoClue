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
        private const string AudioPath = "Assets/Resources/Audio/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CardsPath)) return;

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
            if (assetPath.Contains("introbackground"))
            {
                // long music: stream from disk instead of holding ~36 MB in memory
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
