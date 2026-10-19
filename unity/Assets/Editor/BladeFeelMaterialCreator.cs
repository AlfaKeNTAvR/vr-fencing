// Creates the URP Lit material the PER-22 blade feel prototype colours its
// primitives with. It lives under Resources/ so the player build includes it and
// its shader; a runtime CreatePrimitive material is the built-in Standard shader,
// which URP renders as magenta error (and breaks single-pass stereo).
// Headless: Unity.exe -batchmode -quit -projectPath unity
//           -executeMethod VrFencing.EditorTools.BladeFeelMaterialCreator.CreateOrUpdate
using UnityEditor;
using UnityEngine;

namespace VrFencing.EditorTools
{
    public static class BladeFeelMaterialCreator
    {
        public const string MaterialPath = "Assets/Resources/BladeFeel/BladeFeelLit.mat";
        private const string ShaderName = "Universal Render Pipeline/Lit";

        [MenuItem("VR Fencing/Create blade feel material")]
        public static void CreateOrUpdate()
        {
            var shader = Shader.Find(ShaderName)
                ?? throw new System.InvalidOperationException($"Shader '{ShaderName}' not found.");

            if (!AssetDatabase.IsValidFolder("Assets/Resources/BladeFeel"))
                AssetDatabase.CreateFolder("Assets/Resources", "BladeFeel");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.4f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BladeFeelMaterialCreator] Material ready at {MaterialPath}");
        }
    }
}
