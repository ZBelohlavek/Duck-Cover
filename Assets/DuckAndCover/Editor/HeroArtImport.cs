using UnityEditor;

namespace DuckAndCover.Editor
{
    /// <summary>
    /// Import settings for hero models under Assets/DuckAndCover/Art, so nobody has to set them by hand:
    /// rigged models import as Generic with their own avatar and every clip unchanged (no keyframe
    /// reduction, so hands stay on the rifle); first-person models import as plain meshes. Materials are
    /// not imported: the game puts every hero on its painted texture (<c>HeroArt.ApplyPaint</c>).
    /// </summary>
    public class HeroArtImport : AssetPostprocessor
    {
        const string Root = "Assets/DuckAndCover/Art/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = assetImporter as ModelImporter;
            if (importer == null) return;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            bool firstPerson = assetPath.Contains("FirstPerson");
            importer.animationType = firstPerson ? ModelImporterAnimationType.None : ModelImporterAnimationType.Generic;
            importer.importAnimation = !firstPerson;
            if (!firstPerson)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
            }
        }
    }
}
