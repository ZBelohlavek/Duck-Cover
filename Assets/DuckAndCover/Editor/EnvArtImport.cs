using UnityEditor;

namespace DuckAndCover.EditorTools
{
    /// <summary>
    /// Import settings for the modelled environment pieces in Data/Resources/EnvArt (see Game/Env/EnvArt):
    /// the *_n textures are tangent-space normal maps, the *_c textures painted colour (sRGB).
    /// </summary>
    public class EnvArtImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/EnvArt/")) return;
            var importer = (TextureImporter)assetImporter;
            // A plain 2D texture: the first imports on the owner's PC came in as cubemaps, which
            // Resources.Load<Texture2D> can't return, and every piece drew white.
            importer.textureShape = TextureImporterShape.Texture2D;
            if (assetPath.EndsWith("_n.jpg") || assetPath.EndsWith("_n.png"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            }
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
        }
    }
}
