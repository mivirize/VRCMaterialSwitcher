#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// アセットフォルダ内のマテリアルを解析し、カラーバリエーションを自動検出するクラス。
    ///
    /// 検出スキーマ（優先順）:
    ///   1. UVトークン（UV1, UV5 など）: マテリアルスロット＝オブジェクト単位でグループ化（最優先）
    ///   2. パーツフォルダ: フォルダ名がパーツ名（色でも汎用でもない）ならフォルダ＝1グループ
    ///   3. ファイルパート: 色フォルダ／汎用フォルダ内はファイル名の非色トークンでグループ化
    ///   バリエーション名は「グループ内で変化する軸（ファイル名 or フォルダ名）」を自動判定して生成する。
    /// </summary>
    public static class MaterialVariationDetector
    {
        // 色・パターン（バリエーション）を表すキーワード。
        // これらが「変化する側」＝バリエーション、そうでない側＝パーツ（オブジェクト）と判定する。
        private static readonly HashSet<string> ColorKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 基本色
            "black", "white", "red", "blue", "green", "yellow", "purple", "pink",
            "orange", "brown", "gray", "grey", "navy", "gold", "silver",
            // 応用色
            "deepsea", "mustard", "lightgrey", "lightgray", "darkgrey", "darkgray",
            "ivory", "cream", "wine", "rose", "coral", "mint", "olive", "teal",
            "cyan", "magenta", "beige", "khaki", "charcoal", "crimson", "scarlet",
            "indigo", "darkblue", "darkgreen", "darkred", "lightblue",
            // 日本語色名（ローマ字）
            "kuro", "shiro", "aka", "ao", "midori", "murasaki",
            // 柄・素材・状態（バリエーションを表す語）
            "check", "line", "wool", "stripe", "sheer", "silk", "denim", "camo",
            "military", "plain", "muji", "partial", "rogo", "seethrough", "see_through",
            "dark", "light", "basic", "normal", "matte", "glossy", "clear",
            "kenjo", "shima", "tunagi", "rangiku", "shippo", "kuroneko",
            // その他パターン
            "default", "original", "alt", "ver", "type", "allblack", "allwhite",
            "zap", "tex", "no_pattern", "nopattern",
        };

        // 一般的すぎてパーツ名に使わないフォルダ名（この場合はファイルパートで判定する）
        private static readonly HashSet<string> GenericFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "material", "materials", "mat", "mats", "_material", "_materials",
            "common", "_common", "resources", "resource",
            "materialpack", "_materialpack", "material pack",
        };

        // UVトークン検出（UV1, uv_2, UV 3 など）
        private static readonly Regex UvRegex = new Regex(@"[Uu][Vv]\s*_?\s*(\d+)", RegexOptions.Compiled);

        // トークン分割（アンダースコア・空白・ハイフン・ドット・括弧）
        private static readonly Regex TokenSplit = new Regex(@"[ _\-\.\(\)（）]+", RegexOptions.Compiled);

        /// <summary>
        /// 指定フォルダ内のマテリアルをスキャンし、バリエーショングループを検出する
        /// </summary>
        /// <param name="folderPath">Assetsフォルダからの相対パス（例: "Assets/ZAPZAProps/Effortlechic"）</param>
        /// <param name="recursive">サブフォルダも含めるか</param>
        /// <returns>検出されたマテリアルグループのリスト</returns>
        public static List<MaterialGroup> DetectVariations(string folderPath, bool recursive = true)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogWarning($"[VRC Material Switcher] 無効なフォルダパス: {folderPath}");
                return new List<MaterialGroup>();
            }

            // フォルダ内の全マテリアルを取得
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });

            if (!recursive)
            {
                // 非再帰の場合、直下のみ取得
                guids = guids.Where(g =>
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    return Path.GetDirectoryName(p).Replace("\\", "/") == folderPath.Replace("\\", "/");
                }).ToArray();
            }

            if (guids.Length == 0)
            {
                Debug.LogWarning($"[VRC Material Switcher] マテリアルが見つかりません: {folderPath}");
                return new List<MaterialGroup>();
            }

            // マテリアルをロード
            var materials = new List<(string path, string name, Material mat)>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                {
                    materials.Add((path, mat.name, mat));
                }
            }

            var allGroups = BuildGroups(materials);

            Debug.Log($"[VRC Material Switcher] {allGroups.Count}個のマテリアルグループを検出しました");
            return allGroups;
        }

        // グループ構築用の内部バケット
        private class GroupBucket
        {
            public string display;
            public readonly List<(string rawName, string folder, Material mat)> items
                = new List<(string, string, Material)>();
        }

        /// <summary>
        /// マテリアル一覧をパートキーでグルーピングし、MaterialGroupリストを構築する。
        /// </summary>
        private static List<MaterialGroup> BuildGroups(List<(string path, string name, Material mat)> materials)
        {
            var order = new List<string>();
            var buckets = new Dictionary<string, GroupBucket>();

            foreach (var (path, name, mat) in materials)
            {
                if (mat == null) continue;

                string folder = Path.GetDirectoryName(path).Replace("\\", "/");
                string fl = GetLeafFolderName(folder);

                string key;
                string display;

                string uv = UvNum(name) ?? UvNum(fl);
                if (uv != null)
                {
                    // 戦略1: UVトークン＝スロット単位
                    key = "uv:" + uv;
                    display = (UvNum(fl) != null) ? fl : uv; // フォルダにUVがあれば説明的な名前を使う
                }
                else if (!string.IsNullOrEmpty(fl) && !IsColorLike(fl) && !GenericFolderNames.Contains(fl))
                {
                    // 戦略2: パーツフォルダ（フォルダ名が色でも汎用でもない）
                    key = "folder:" + folder;
                    display = fl;
                }
                else
                {
                    // 戦略3: 色フォルダ／汎用フォルダ → ファイル名の非色トークンでパーツ判定
                    var partToks = NormTokens(name)
                        .Where(t => !IsColorLike(t) && !IsAllDigits(t))
                        .ToList();
                    if (partToks.Count > 0)
                    {
                        string part = string.Join(" ", partToks);
                        key = "file:" + part.ToLowerInvariant();
                        display = part;
                    }
                    else
                    {
                        key = "folder:" + folder;
                        display = string.IsNullOrEmpty(fl) ? name : fl;
                    }
                }

                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new GroupBucket { display = display };
                    buckets[key] = bucket;
                    order.Add(key);
                }
                bucket.items.Add((name, folder, mat));
            }

            var result = new List<MaterialGroup>();
            foreach (var key in order)
            {
                var bucket = buckets[key];
                if (bucket.items.Count < 2) continue; // 単独パーツ（切替不能）は除外

                var group = BuildGroupFromBucket(bucket);
                if (group != null && group.variations.Count >= 2)
                    result.Add(group);
            }
            return result;
        }

        /// <summary>
        /// バケットから MaterialGroup を構築。バリエーション名は「グループ内で変化する軸
        /// （ファイル名 or フォルダ名）」を自動判定して付与する。
        /// </summary>
        private static MaterialGroup BuildGroupFromBucket(GroupBucket bucket)
        {
            int n = bucket.items.Count;

            // ファイル側の候補文字列（UV除去 → グループ共通プレフィックス除去）
            var fileRaw = bucket.items.Select(it => StripUv(it.rawName)).ToList();
            string lcp = LongestCommonTokenPrefix(fileRaw);
            var fileParts = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                string s = fileRaw[i];
                if (!string.IsNullOrEmpty(lcp) && s.Length > lcp.Length)
                    s = s.Substring(lcp.Length);
                fileParts.Add(s.Trim(' ', '_', '-', '.'));
            }

            // フォルダ側の候補文字列（共通祖先からの相対パス。ネスト構造で色＋柄を保持）
            var folderParts = BuildFolderLabels(bucket.items.Select(it => it.folder).ToList());

            bool fileVaries = fileParts.Where(s => !string.IsNullOrEmpty(s))
                                       .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            bool folderVaries = folderParts.Where(s => !string.IsNullOrEmpty(s))
                                           .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

            var group = new MaterialGroup
            {
                groupName = bucket.display,
                variations = new List<MaterialVariation>()
            };

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ラベルとマテリアルの組を作りソート
            var labeled = new List<(string label, Material mat)>(n);
            for (int i = 0; i < n; i++)
            {
                string fp = fileParts[i];
                string dp = folderParts[i];
                string label;

                if (fileVaries && folderVaries)
                    label = string.IsNullOrEmpty(dp) ? fp : $"{fp} ({dp})";
                else if (fileVaries)
                    label = fp;
                else if (folderVaries)
                    label = dp;
                else
                    label = !string.IsNullOrEmpty(fp) ? fp : dp;

                if (string.IsNullOrEmpty(label)) label = "default";
                labeled.Add((label, bucket.items[i].mat));
            }

            foreach (var (label, mat) in labeled.OrderBy(x => x.label, StringComparer.OrdinalIgnoreCase))
            {
                string v = label;
                int k = 2;
                while (used.Contains(v)) { v = $"{label} ({k})"; k++; }
                used.Add(v);
                group.variations.Add(new MaterialVariation(v, mat));
            }

            if (group.variations.Count > 0)
                group.variations[0].isDefault = true;

            return group;
        }

        /// <summary>
        /// フォルダ群から「共通祖先より下の相対パス」をラベル化する。
        /// ネスト構造（色/柄）で色と柄の両方を保持する（例: "Black/All Check"）。
        /// UVトークンや汎用フォルダ名（Materials等）は除去する。
        /// </summary>
        private static List<string> BuildFolderLabels(List<string> folders)
        {
            int n = folders.Count;
            var split = folders.Select(f => f.Replace("\\", "/").Split('/')).ToList();

            // 共通の先頭セグメント数を求める
            int minLen = split.Min(a => a.Length);
            int common = 0;
            for (; common < minLen; common++)
            {
                string seg = split[0][common];
                if (!split.All(a => a[common] == seg)) break;
            }

            var result = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                var rel = new List<string>();
                for (int j = common; j < split[i].Length; j++)
                {
                    string seg = StripUv(split[i][j]).Trim(' ', '_', '-', '.');
                    if (string.IsNullOrEmpty(seg)) continue;
                    if (GenericFolderNames.Contains(seg)) continue;
                    rel.Add(seg);
                }
                result.Add(rel.Count > 0 ? string.Join("/", rel) : GetLeafFolderName(folders[i]));
            }
            return result;
        }

        /// <summary>名前からUV番号（"UV5"等）を取得。無ければnull。</summary>
        private static string UvNum(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = UvRegex.Match(s);
            return m.Success ? "UV" + m.Groups[1].Value : null;
        }

        /// <summary>名前からUVトークンを除去する。</summary>
        private static string StripUv(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return UvRegex.Replace(s, "").Trim(' ', '_', '-', '.');
        }

        /// <summary>区切り文字でトークン分割する。</summary>
        private static IEnumerable<string> NormTokens(string s)
        {
            if (string.IsNullOrEmpty(s)) return Enumerable.Empty<string>();
            return TokenSplit.Split(s).Where(t => !string.IsNullOrEmpty(t));
        }

        private static bool IsAllDigits(string s)
        {
            return !string.IsNullOrEmpty(s) && s.All(char.IsDigit);
        }

        /// <summary>
        /// 文字列が色・パターン（＝バリエーション）を表すかを判定。
        /// トークンの過半数が色キーワードまたは数字なら true。
        /// </summary>
        private static bool IsColorLike(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (IsAllDigits(name.Trim())) return true;

            var tokens = NormTokens(name).ToList();
            if (tokens.Count == 0) return false;

            int hits = tokens.Count(t => ColorKeywords.Contains(t) || IsAllDigits(t));
            return hits >= 1 && hits * 2 >= tokens.Count;
        }

        /// <summary>
        /// 複数の名前から「_ 区切りの共通トークンプレフィックス」を求める。
        /// 例: ["Kaku_Obi_Kenjo_Brown","Kaku_Obi_Shima_Red","Kaku_Obi_Tunagi"] → "Kaku_Obi"
        /// 共通部分が無ければ空文字。全トークンが一致する場合は末尾1トークンをバリアント側に残す。
        /// </summary>
        private static string LongestCommonTokenPrefix(List<string> names)
        {
            if (names == null || names.Count < 2) return "";

            var tokenLists = names.Select(n => n.Split('_')).ToList();
            int minLen = tokenLists.Min(t => t.Length);

            var prefixTokens = new List<string>();
            for (int i = 0; i < minLen; i++)
            {
                string token = tokenLists[0][i];
                if (tokenLists.All(t => t[i] == token))
                    prefixTokens.Add(token);
                else
                    break;
            }

            // 全トークン一致（＝ほぼ同名）の場合は最後の1トークンをバリアントに残す
            if (prefixTokens.Count >= minLen && prefixTokens.Count > 0)
                prefixTokens.RemoveAt(prefixTokens.Count - 1);

            return string.Join("_", prefixTokens);
        }

        /// <summary>
        /// パス末尾のフォルダ名を取得する（例: "Assets/.../Obi" → "Obi"）。
        /// </summary>
        private static string GetLeafFolderName(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return "";
            folderPath = folderPath.Replace("\\", "/").TrimEnd('/');
            int idx = folderPath.LastIndexOf('/');
            return idx >= 0 ? folderPath.Substring(idx + 1) : folderPath;
        }

        /// <summary>
        /// レンダラーから使用中のマテリアルを取得し、マテリアルグループにスロット情報を自動マッピングする
        /// </summary>
        /// <param name="avatarRoot">アバターのルートGameObject</param>
        /// <param name="groups">マッピング対象のマテリアルグループリスト</param>
        /// <returns>レンダラーへのマッピングに成功したグループ数</returns>
        public static int AutoMapRenderers(GameObject avatarRoot, List<MaterialGroup> groups)
        {
            if (avatarRoot == null || groups == null) return 0;

            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int mappedCount = 0;

            foreach (var group in groups)
            {
                if (group.variations.Count == 0) continue;

                // グループ内の全バリエーションマテリアルを検索対象にする
                var groupMaterials = new HashSet<Material>();
                foreach (var v in group.variations)
                {
                    if (v.material != null)
                    {
                        groupMaterials.Add(v.material);
                    }
                }

                if (groupMaterials.Count == 0) continue;

                // このグループのマテリアルが使われている (renderer, slot) を「全て」収集する。
                // 浴衣の上下（Yukata A/B）のように複数メッシュへ適用されるケースに対応。
                var targets = new List<MaterialRenderTarget>();
                var seen = new HashSet<string>();
                foreach (var renderer in renderers)
                {
                    var sharedMats = renderer.sharedMaterials;
                    for (int i = 0; i < sharedMats.Length; i++)
                    {
                        if (sharedMats[i] != null && groupMaterials.Contains(sharedMats[i]))
                        {
                            string path = GetRelativePath(avatarRoot.transform, renderer.transform);
                            string key = path + "#" + i;
                            if (seen.Add(key))
                            {
                                targets.Add(new MaterialRenderTarget(path, i));
                            }
                        }
                    }
                }

                if (targets.Count > 0)
                {
                    group.renderTargets = targets;
                    // 後方互換: 単一フィールドにも先頭ターゲットを反映
                    group.rendererPath = targets[0].rendererPath;
                    group.materialSlotIndex = targets[0].materialSlotIndex;
                    mappedCount++;
                }
            }

            return mappedCount;
        }

        /// <summary>
        /// あるTransformから別のTransformへの相対パスを計算
        /// </summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return "";

            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", parts);
        }

        /// <summary>
        /// 指定フォルダから全サブフォルダを含むAssetパスリストを返す（UI用）
        /// </summary>
        public static List<string> GetSubFolders(string rootFolder)
        {
            var result = new List<string> { rootFolder };
            var subFolders = AssetDatabase.GetSubFolders(rootFolder);
            foreach (var sub in subFolders)
            {
                result.AddRange(GetSubFolders(sub));
            }
            return result;
        }
    }
}
#endif
