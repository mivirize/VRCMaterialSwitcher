#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// テクスチャ収集・サイズ計算・解像度ダウンを担当する静的クラス。
    /// VRChat の Uncompressed Size 目安を計算するために内部リフレクションを使用する。
    /// アバターが変わるとサイズキャッシュを自動的に無効化する（Fix #5）。
    /// </summary>
    internal static class MaterialSwitcherTextureAnalyzer
    {
        // ---- サイズキャッシュ（-1: 未計算）----
        private static long      _cachedSwitcherTexBytes = -1;
        private static long      _cachedAvatarTexBytes   = -1;
        private static long      _cachedTotalTexBytes    = -1;
        private static long      _cachedMeshBytes        = -1;
        private static GameObject _sizeCacheAvatar;

        // ---- TextureUtil.GetStorageMemorySizeLong リフレクション ----
        private static MethodInfo _texSizeMethod;
        private static bool       _texSizeMethodResolved;

        // ---- キャッシュ読み取り用プロパティ ----
        public static long CachedSwitcherTexBytes => _cachedSwitcherTexBytes;
        public static long CachedAvatarTexBytes   => _cachedAvatarTexBytes;
        public static long CachedTotalTexBytes    => _cachedTotalTexBytes;
        public static long CachedMeshBytes        => _cachedMeshBytes;

        /// <summary>サイズキャッシュをすべて無効化する。</summary>
        public static void InvalidateSizeCache()
        {
            _cachedSwitcherTexBytes = -1;
            _cachedAvatarTexBytes   = -1;
            _cachedTotalTexBytes    = -1;
            _cachedMeshBytes        = -1;
        }

        /// <summary>
        /// 容量キャッシュを必要に応じて再計算する（テクスチャ＋メッシュ）。
        /// Fix #5: avatarObject が変わった場合はキャッシュを自動的に無効化してから再計算する。
        /// </summary>
        public static void EnsureSizeCache(GameObject avatarObject, List<MaterialGroup> enabledGroups)
        {
            // Fix #5: アバターが変わったらキャッシュを無効化
            if (avatarObject != _sizeCacheAvatar)
            {
                _sizeCacheAvatar = avatarObject;
                InvalidateSizeCache();
            }

            if (_cachedTotalTexBytes >= 0) return;

            var switcherTex = CollectSwitcherTextures(enabledGroups);
            _cachedSwitcherTexBytes = SumTextureBytes(switcherTex);

            var avatarTex = CollectAvatarTextures(avatarObject);
            _cachedAvatarTexBytes = SumTextureBytes(avatarTex);

            var union = new HashSet<Texture>(switcherTex);
            union.UnionWith(avatarTex);
            _cachedTotalTexBytes = SumTextureBytes(union);

            // メッシュ容量
            long meshBytes = 0;
            if (avatarObject != null)
            {
                var meshes = new HashSet<Mesh>();
                foreach (var r in avatarObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                        meshes.Add(smr.sharedMesh);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        meshes.Add(mf.sharedMesh);
                }
                foreach (var m in meshes)
                    meshBytes += Profiler.GetRuntimeMemorySizeLong(m);
            }
            _cachedMeshBytes = meshBytes;
        }

        /// <summary>マテリアルが参照する全テクスチャを set に追加する。</summary>
        public static void CollectTextures(Material m, HashSet<Texture> set)
        {
            if (m == null || m.shader == null) return;
            int count = ShaderUtil.GetPropertyCount(m.shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;
                string prop = ShaderUtil.GetPropertyName(m.shader, i);
                var tex = m.GetTexture(prop);
                if (tex != null) set.Add(tex);
            }
        }

        /// <summary>有効グループのバリエーションが同梱する全テクスチャを集める。</summary>
        public static HashSet<Texture> CollectSwitcherTextures(List<MaterialGroup> enabledGroups)
        {
            var set = new HashSet<Texture>();
            foreach (var g in enabledGroups)
                foreach (var v in g.variations)
                    if (v.includeInMenu && v.material != null)
                        CollectTextures(v.material, set);
            return set;
        }

        /// <summary>アバターの全レンダラーが参照するテクスチャを集める（本体・装着物すべて）。</summary>
        public static HashSet<Texture> CollectAvatarTextures(GameObject avatarObject)
        {
            var set = new HashSet<Texture>();
            if (avatarObject == null) return set;
            foreach (var r in avatarObject.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.sharedMaterials)
                    CollectTextures(mat, set);
            return set;
        }

        /// <summary>
        /// テクスチャの GPU メモリサイズ（VRChat の Uncompressed Size 計算に一致）をバイトで返す。
        /// TextureUtil.GetStorageMemorySizeLong をリフレクション経由で使い、
        /// 取得できない場合は Profiler.GetRuntimeMemorySizeLong にフォールバックする。
        /// </summary>
        public static long TextureStorageBytes(Texture t)
        {
            if (t == null) return 0;
            try
            {
                if (!_texSizeMethodResolved)
                {
                    _texSizeMethodResolved = true;
                    var util = typeof(UnityEditor.Editor).Assembly
                        .GetType("UnityEditor.TextureUtil");
                    _texSizeMethod = util?.GetMethod("GetStorageMemorySizeLong",
                        BindingFlags.Public | BindingFlags.Static);
                }
                if (_texSizeMethod != null)
                {
                    var r = _texSizeMethod.Invoke(null, new object[] { t });
                    if (r is long l) return l;
                }
            }
            catch { }
            return Profiler.GetRuntimeMemorySizeLong(t);
        }

        /// <summary>テクスチャセットの合計バイト数を返す。</summary>
        public static long SumTextureBytes(HashSet<Texture> texs)
        {
            long total = 0;
            foreach (var t in texs) total += TextureStorageBytes(t);
            return total;
        }

        /// <summary>
        /// 指定テクスチャ集合の最大解像度を下げる（＋任意で Crunch 圧縮）。
        /// TextureImporter の import 設定を書き換えて再インポートする。
        /// 戻り値は変更したテクスチャ数。
        /// </summary>
        public static int ReduceTextures(IEnumerable<Texture> textures, int maxSize, bool useCrunch)
        {
            var list = new HashSet<Texture>(textures).ToList();
            int changed = 0;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    EditorUtility.DisplayProgressBar(
                        "テクスチャ解像度を調整中",
                        $"{i + 1}/{list.Count}: {t.name}",
                        (float)(i + 1) / Mathf.Max(1, list.Count));

                    string assetPath = AssetDatabase.GetAssetPath(t);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (imp == null) continue;

                    bool dirty = false;
                    if (imp.maxTextureSize > maxSize)
                    {
                        imp.maxTextureSize = maxSize;
                        dirty = true;
                    }
                    if (useCrunch && !imp.crunchedCompression)
                    {
                        imp.crunchedCompression = true;
                        imp.compressionQuality = 50;
                        dirty = true;
                    }
                    if (dirty)
                    {
                        imp.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return changed;
        }
    }
}
#endif
