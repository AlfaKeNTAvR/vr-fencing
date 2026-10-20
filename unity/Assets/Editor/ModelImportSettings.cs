// Import settings for the generated models under Assets/VrFencing (PER-41: the épée hilts and the fencing mask,
// built by tools/models/build_models.py). The code gives every part its URP material at runtime, so the importer
// must not make materials of its own (they would be Standard-shader materials, magenta on the Quest), and the meshes
// need nothing but geometry: no animation, no blend shapes, no CPU copy. Normals are recomputed with a 50 degree
// smoothing angle, which keeps the bell and grips smooth and the rims and caps sharp.
using UnityEditor;

namespace VrFencing.EditorTools
{
    public sealed class ModelImportSettings : AssetPostprocessor
    {
        private const string GeneratedModelFolder = "Assets/VrFencing/";
        private const float NormalSmoothingAngleDegrees = 50.0f;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(GeneratedModelFolder) || !(assetImporter is ModelImporter importer))
                return;

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle = NormalSmoothingAngleDegrees;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
        }
    }
}
