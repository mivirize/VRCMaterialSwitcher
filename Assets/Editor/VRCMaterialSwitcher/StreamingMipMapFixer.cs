#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// アバターで使用されている全テクスチャの Streaming Mip Maps を一括で有効にするユーティリティ
    /// </summary>
    public static class StreamingMipMapFixer
    {
        [MenuItem("MIVI/VRC Material Switcher/Fix Streaming Mip Maps (Project-wide)", false, 40)]
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

        // v1.2.2: "Fix Streaming Mip Maps (Scene Avatars)" は Project-wide と役割が重複し
        // 使われていなかったためメニューごと削除した（履歴は git 参照）。
    }
}
#endif
