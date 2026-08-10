#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// SwitcherConfig のファイル I/O（保存・ロード）を担当する静的クラス。
    /// GUID 変換・マテリアル参照復元・旧形式移行をまとめて処理し、
    /// EditorWindow がこれらの詳細を持たなくて済むようにする。
    /// </summary>
    internal static class MaterialSwitcherConfigIO
    {
        /// <summary>設定 JSON の保存先パス。</summary>
        public const string ConfigPath = "ProjectSettings/VRCMaterialSwitcherSettings.json";

        /// <summary>
        /// 設定をディスクに保存する。
        /// JsonUtility.ToJson の前に GUID を記録し、エディタ再起動をまたぐ復元を保証する。
        /// </summary>
        public static void SaveConfig(SwitcherConfig config)
        {
            try
            {
                // Fix #1: 保存前にマテリアル参照を GUID に変換する
                SwitcherConfigPersistence.CaptureAssetReferences(config);
                string json = JsonUtility.ToJson(config, true);
                System.IO.File.WriteAllText(ConfigPath, json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VRC Material Switcher] 設定の保存に失敗しました: {e.Message}");
            }
        }

        /// <summary>
        /// ディスクから設定をロードする。
        /// GUID からマテリアル参照を復元し、旧形式の renderTargets 移行を行う。
        /// </summary>
        /// <returns>scanFolderPath に対応する DefaultAsset（存在しない場合は null）。</returns>
        public static DefaultAsset LoadConfig(SwitcherConfig config)
        {
            try
            {
                string path = ConfigPath;
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    JsonUtility.FromJsonOverwrite(json, config);

                    // Fix #2: ロード直後に GUID からマテリアル参照を復元する
                    SwitcherConfigPersistence.ResolveAssetReferences(config);

                    // Fix #6: 旧形式（単一 rendererPath）から renderTargets への移行を一度だけ行う
                    MigrateLegacyRenderTargets(config);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VRC Material Switcher] 設定のロードに失敗しました: {e.Message}");
            }

            // Fix #2: scanFolder を scanFolderPath から復元して返す
            if (!string.IsNullOrEmpty(config.scanFolderPath))
                return AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.scanFolderPath);
            return null;
        }

        /// <summary>
        /// 旧形式（単一 rendererPath フィールド）から renderTargets リストへの一括移行。
        /// DrawGroupRenderTargets での毎フレーム移行を防ぐためロード直後に一度だけ実行する。
        /// </summary>
        private static void MigrateLegacyRenderTargets(SwitcherConfig config)
        {
            if (config?.materialGroups == null) return;
            foreach (var group in config.materialGroups)
            {
                if (group.renderTargets == null)
                    group.renderTargets = new List<MaterialRenderTarget>();

                if (group.renderTargets.Count == 0 && !string.IsNullOrEmpty(group.rendererPath))
                {
                    group.renderTargets.Add(
                        new MaterialRenderTarget(group.rendererPath, group.materialSlotIndex));
                }
            }
        }
    }
}
#endif
