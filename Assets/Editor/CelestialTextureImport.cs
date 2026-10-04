using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the planet and Moon maps under Resources/Earth and
/// Resources/Moon: keep their full resolution (Unity's default caps textures
/// at 2048), wrap around in longitude, clamp at the poles, and filter well
/// at grazing angles.
/// </summary>
public sealed class CelestialTextureImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Earth/") && !assetPath.StartsWith("Assets/Resources/Moon/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.maxTextureSize = 8192;
        importer.mipmapEnabled = true;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.anisoLevel = 8;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.npotScale = TextureImporterNPOTScale.None;
        // Water mask lives in the day map's alpha; height maps are linear data.
        importer.alphaSource = assetPath.Contains("earth_day") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.sRGBTexture = !assetPath.Contains("height") && !assetPath.Contains("normal");
    }
}
