#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// アバターで使用されている全テクスチャの Streaming Mip Maps を一括で有効にするユーティリティ
    /// </summary>
    public static class StreamingMipMapFixer
    {
        [MenuItem("Tools/VRC Material Switcher/Fix Streaming Mip Maps (Project-wide)")]
        public static void FixProjectWide()
        {
            if (!EditorUtility.DisplayDialog("Fix Streaming Mip Maps (Project-wide)",
                "プロジェクト内のすべてのテクスチャをスキャンし、Mipmapが有効で'Streaming Mip Maps'が無効になっているものをすべて一括で有効にしますか？\n(アセット量が多い場合、再インポートに時間がかかる場合があります)", "はい", "いいえ"))
            {
                return;
            }

            // プロジェクト内のすべてのテクスチャのGUIDを取得
            string[] guids = AssetDatabase.FindAssets("t:Texture2D");
            int fixedCount = 0;
            int totalScanned = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    // Packagesフォルダ以下のものはスキップ
                    if (path.StartsWith("Packages/")) continue;

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    totalScanned++;

                    if (importer.mipmapEnabled && !importer.streamingMipmaps)
                    {
                        importer.streamingMipmaps = true;
                        importer.SaveAndReimport();
                        fixedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            string msg = fixedCount > 0
                ? $"{fixedCount} 個のテクスチャで Streaming Mip Maps を有効にしました。(スキャン数: {totalScanned})"
                : $"修正が必要なテクスチャはありませんでした。(スキャン数: {totalScanned})";
            Debug.Log($"[StreamingMipMapFixer] {msg}");
            EditorUtility.DisplayDialog("Fix Streaming Mip Maps", msg, "OK");
        }

        [MenuItem("Tools/VRC Material Switcher/Fix Streaming Mip Maps (Active Avatar)")]
        public static void FixActiveAvatar()
        {
            // シーン内のアバタールートを探す
            var descriptors = Object.FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (descriptors.Length == 0)
            {
                EditorUtility.DisplayDialog("Fix Streaming Mip Maps", "シーンにアバターが見つかりません。", "OK");
                return;
            }

            int fixedCount = 0;
            var processed = new HashSet<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var desc in descriptors)
                {
                    var renderers = desc.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat == null) continue;

                            var shader = mat.shader;
                            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
                            {
                                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                                    continue;

                                string propName = ShaderUtil.GetPropertyName(shader, i);
                                var tex = mat.GetTexture(propName) as Texture2D;
                                if (tex == null) continue;

                                string path = AssetDatabase.GetAssetPath(tex);
                                if (string.IsNullOrEmpty(path) || processed.Contains(path)) continue;
                                processed.Add(path);

                                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                                importer.streamingMipmaps = true;
                                importer.SaveAndReimport();
                                fixedCount++;
                            }
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            string msg = fixedCount > 0
                ? $"{fixedCount} 個のテクスチャで Streaming Mip Maps を有効にしました。"
                : "修正が必要なテクスチャはありませんでした。";
            Debug.Log($"[StreamingMipMapFixer] {msg}");
            EditorUtility.DisplayDialog("Fix Streaming Mip Maps", msg, "OK");
        }
    }
}
#endif
