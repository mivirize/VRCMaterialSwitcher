#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const string SourceDir = "Assets/Editor/VRCMaterialSwitcher";

        /// <summary>配布に含めないファイル（開発専用）</summary>
        private static readonly HashSet<string> ExcludedFiles = new HashSet<string>
        {
            "PackageExporter.cs",
        };

        /// <summary>
        /// 配布対象を「ソースフォルダ内の .cs から開発専用を除いたもの」として自動導出する。
        /// 手書きのリストはファイル追加時に更新を忘れやすく、
        /// 実際に v1.2.1 で新規クラスが梱包から抜ける事故が起きたため列挙方式にした。
        /// </summary>
        private static string[] CollectDistributedAssets()
        {
            var paths = Directory
                .GetFiles(SourceDir, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(p => p.Replace("\\", "/"))
                .Where(p => !ExcludedFiles.Contains(Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            if (paths.Length == 0)
                throw new InvalidOperationException($"配布対象が見つかりません: {SourceDir}");

            return paths;
        }

        [MenuItem("MIVI Works/VRC Material Switcher/Export UnityPackage (Dev)", false, 100)]
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

            var assets = CollectDistributedAssets();
            Debug.Log($"[Export] {outputPath} の作成を開始します（{assets.Length} ファイル）...");
            foreach (var a in assets) Debug.Log($"[Export]   {a}");
            AssetDatabase.ExportPackage(assets, outputPath, ExportPackageOptions.Default);
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
