#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// 配布用 unitypackage を書き出す開発者向けユーティリティ。
    /// このファイル自体は配布パッケージに含めない。
    /// batchmode では -vmsExportPath &lt;出力パス&gt; で出力先を指定できる。
    /// </summary>
    public static class PackageExporter
    {
        // 配布対象（開発専用の PackageExporter.cs は含めない）
        private static readonly string[] DistributedAssets =
        {
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherData.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialVariationDetector.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherSetup.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherWindow.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherWindow.Groups.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherWindow.Renderers.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherWindow.Setup.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherConfigIO.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherCostCalculator.cs",
            "Assets/Editor/VRCMaterialSwitcher/MaterialSwitcherTextureAnalyzer.cs",
            "Assets/Editor/VRCMaterialSwitcher/ParamResidueCleaner.cs",
            "Assets/Editor/VRCMaterialSwitcher/StreamingMipMapFixer.cs",
        };

        [MenuItem("Tools/VRC Material Switcher/Export UnityPackage (Dev)")]
        public static void Export()
        {
            string outputPath;

            if (Application.isBatchMode)
            {
                outputPath = GetArgValue("-vmsExportPath")
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "VRCMaterialSwitcher.unitypackage");
            }
            else
            {
                outputPath = EditorUtility.SaveFilePanel(
                    "Export VRCMaterialSwitcher UnityPackage",
                    "", "VRCMaterialSwitcher", "unitypackage");
                if (string.IsNullOrEmpty(outputPath)) return;
            }

            Debug.Log($"[Export] {outputPath} の作成を開始します...");
            AssetDatabase.ExportPackage(DistributedAssets, outputPath, ExportPackageOptions.Default);
            Debug.Log($"[Export] パッケージの作成が完了しました: {outputPath}");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
            else
            {
                EditorUtility.RevealInFinder(outputPath);
            }
        }

        private static string GetArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag) return args[i + 1];
            }
            return null;
        }
    }
}
#endif
