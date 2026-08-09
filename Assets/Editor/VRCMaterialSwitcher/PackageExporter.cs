#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    public static class PackageExporter
    {
        [MenuItem("Tools/VRC Material Switcher/Export UnityPackage")]
        public static void Export()
        {
            string[] assetPaths = new string[]
            {
                "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherData.cs",
                "Assets/Editor/VRCMaterialSwitcher/MaterialVariationDetector.cs",
                "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherSetup.cs",
                "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherWindow.cs",
                "Assets/Editor/VRCMaterialSwitcher/ParamResidueCleaner.cs",
                "Assets/Editor/VRCMaterialSwitcher/StreamingMipMapFixer.cs",
                "Assets/Editor/VRCMaterialSwitcher/PackageExporter.cs",
            };

            string outputPackageName = @"C:\Users\owner\Dev\VRCMaterialSwitcher.unitypackage";

            Debug.Log($"[Export] {outputPackageName} の作成を開始します...");
            AssetDatabase.ExportPackage(assetPaths, outputPackageName, ExportPackageOptions.Recurse);
            Debug.Log($"[Export] パッケージの作成が完了しました: {outputPackageName}");

            EditorUtility.RevealInFinder(outputPackageName);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
    }
}
#endif
