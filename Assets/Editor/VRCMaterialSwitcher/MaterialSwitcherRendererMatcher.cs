#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>マッピングの確度。</summary>
    public enum MappingOutcome
    {
        /// <summary>スキャンしたマテリアルがアバター上でそのまま使われていた（最も確実）</summary>
        ExactMatch,

        /// <summary>参照は一致しないが、証拠が十分そろって適用先を特定できた</summary>
        Inferred,

        /// <summary>候補は見つかったが決め手が無い。ユーザーの確認が必要</summary>
        NeedsConfirmation,

        /// <summary>候補が全く見つからなかった</summary>
        NoCandidate,
    }

    /// <summary>アバター上の1スロット（レンダラー＋マテリアルスロット）。</summary>
    public class SlotRef
    {
        public Renderer renderer;
        public string path;
        public int slot;
        public Material material;

        public string Label =>
            $"{(path == MaterialRenderTarget.AvatarRootPath || string.IsNullOrEmpty(path) ? "(アバタールート)" : path)} " +
            $"[{slot}] {(material != null ? material.name : "null")}";
    }

    /// <summary>1スロットに対するスコアリング結果。</summary>
    public class MatchCandidate
    {
        public SlotRef slot;
        public int score;
        public List<string> reasons = new List<string>();

        /// <summary>部位キーが「完全トークン一致」したか（前方一致だけの場合は false）</summary>
        public bool partKeyExact;

        /// <summary>バリエーションと shader が違うか（自動採用を禁止する条件）</summary>
        public bool shaderMismatch;

        public string ReasonText => reasons.Count > 0 ? string.Join(" / ", reasons) : "根拠なし";
    }

    /// <summary>1グループのマッピング結果。</summary>
    public class GroupMappingResult
    {
        public MaterialGroup group;
        public MappingOutcome outcome;
        public List<MaterialRenderTarget> adopted = new List<MaterialRenderTarget>();
        public List<MatchCandidate> candidates = new List<MatchCandidate>();

        /// <summary>この結果になった理由（UI 表示用の日本語）</summary>
        public string diagnosis = "";

        /// <summary>今回は決められず、以前の設定をそのまま残した場合 true</summary>
        public bool keptPrevious;
    }

    /// <summary>スキャン全体のマッピングレポート。</summary>
    public class MappingReport
    {
        public int rendererCount;
        public int slotCount;
        public int exactHitSlots;
        public List<GroupMappingResult> groups = new List<GroupMappingResult>();
        public List<string> warnings = new List<string>();

        public int AdoptedGroupCount =>
            groups.Count(g => g.outcome == MappingOutcome.ExactMatch || g.outcome == MappingOutcome.Inferred);
        public int NeedsConfirmationCount => groups.Count(g => g.outcome == MappingOutcome.NeedsConfirmation);
        public int NoCandidateCount => groups.Count(g => g.outcome == MappingOutcome.NoCandidate);
    }

    /// <summary>
    /// マテリアルグループを「アバターのどのレンダラー・スロットに適用するか」を推定するクラス。
    ///
    /// 設計上の前提（v1.2.0 の失敗要因）:
    ///   旧実装は「スキャンしたマテリアルアセットがアバター上でそのまま使われている」ことを
    ///   暗黙の前提に、参照等価だけで照合していた。しかし実際の衣装は
    ///   prefab に割り当てられた既定マテリアルと、別フォルダの色バリエーションが
    ///   別アセットであることが多く、その場合ヒット 0 件になる。
    ///
    /// 本クラスは参照等価を最優先に残したうえで、外れた場合に
    ///   マテリアル名 / 部位キー / テクスチャ資産の共有 / shader / レンダラー名
    /// を証拠として重み付けし、十分な確度があるものだけ自動採用、
    /// 決め手が無いものは候補として UI に提示する（誤マッチより手動誘導を優先）。
    /// </summary>
    public static class MaterialSwitcherRendererMatcher
    {
        // ---- スコア重み ----
        // 単独で採用に至れるのは「名前完全一致」「不変テクスチャ共有」のみ。
        // shader / スロット番号 / 汎用名は単独では採用しない（誤マッチ源）。
        private const int W_NAME_EQUAL      = 50; // 別アセットだが同名（複製・別フォルダ配置）
        private const int W_INVARIANT_TEX   = 40; // 色で変わらないテクスチャ（normal/mask 等）の共有
        private const int W_PART_KEY        = 35; // 色トークンを除いた部位キーの一致
        private const int W_MAIN_TEX        = 25; // _MainTex の共有（色替えでは変わることも多い）
        private const int W_RENDERER_NAME   = 20; // レンダラー名が部位キーと一致
        private const int W_PART_KEY_PREFIX = 15; // 部位キーが前方一致
        private const int W_SHADER_EQUAL    = 5;  // 補助証拠
        private const int W_SHADER_DIFFER   = -40; // 強い減点（別部位の可能性が高い）
        private const int W_UNIQUE_PART_KEY = 25; // 部位キー一致がアバター内で唯一だった場合の加点

        /// <summary>自動採用に必要な最低スコア</summary>
        private const int AUTO_ADOPT_SCORE = 60;

        /// <summary>次点との必要差（これ未満なら曖昧として確認に回す）</summary>
        private const int AUTO_ADOPT_MARGIN = 20;

        /// <summary>同一部位の複数メッシュと見なすスコア差（浴衣の上下など）</summary>
        private const int SAME_PART_EPSILON = 5;

        /// <summary>部位キーとして意味を持たない汎用語</summary>
        private static readonly HashSet<string> GenericPartWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "material", "mat", "mesh", "object", "obj", "body", "main", "base", "default",
            "new", "copy", "lilToon", "toon", "standard", "skin", "cloth", "common",
            "renderer", "skinnedmeshrenderer", "armature", "root", "avatar",
        };

        // ==================================================================
        // エントリポイント
        // ==================================================================

        /// <summary>
        /// 全グループの適用先を推定する。group.renderTargets は
        /// 自動採用できたグループのみ書き換える（決められなかったグループの既存設定は壊さない）。
        /// </summary>
        public static MappingReport MapAll(GameObject avatarRoot, List<MaterialGroup> groups)
        {
            var report = new MappingReport();
            if (avatarRoot == null || groups == null) return report;

            var slots = EnumerateSlots(avatarRoot).ToList();
            report.rendererCount = avatarRoot.GetComponentsInChildren<Renderer>(true).Length;
            report.slotCount = slots.Count;
            report.warnings.AddRange(CollectHierarchyWarnings(avatarRoot, slots));

            foreach (var group in groups)
            {
                var result = MapGroup(group, slots);
                report.groups.Add(result);
                if (result.outcome == MappingOutcome.ExactMatch)
                    report.exactHitSlots += result.adopted.Count;
            }

            return report;
        }

        /// <summary>1グループ分の推定を行い、採用できる場合は group に反映する。</summary>
        private static GroupMappingResult MapGroup(MaterialGroup group, List<SlotRef> slots)
        {
            var result = new GroupMappingResult { group = group };

            var variants = group.variations
                .Where(v => v.material != null)
                .Select(v => v.material)
                .Distinct()
                .ToList();

            if (variants.Count == 0)
            {
                result.outcome = MappingOutcome.NoCandidate;
                result.diagnosis = "このグループにマテリアルが設定されていません。";
                return result;
            }

            // ---- 第1段: 参照等価（最も確実） ----
            var exact = slots.Where(s => variants.Any(v => v == s.material)).ToList();
            if (exact.Count > 0)
            {
                Adopt(group, result, exact, MappingOutcome.ExactMatch);
                result.diagnosis = $"スキャンしたマテリアルがアバター上で使われています（{exact.Count}スロット）。";

                // 誤マッチの疑い: 同一マテリアルが無関係な部位に共有されている場合
                var distinctRenderers = exact.Select(s => s.path).Distinct().Count();
                if (distinctRenderers >= 3)
                {
                    result.diagnosis += $"\n※ {distinctRenderers}個のメッシュで共有されています。" +
                                        "共通マテリアルの場合、意図しない部位まで切り替わる可能性があります。";
                }

                AlignDefaultToCurrent(group, exact[0].material);
                return result;
            }

            // ---- 第2段: 証拠のスコアリング ----
            var signature = GroupSignature.Build(group, variants);
            var scored = slots.Select(s => Score(s, signature)).ToList();

            // 部位キーの完全一致がアバター全体で1スロットだけなら、その一意性自体が強い証拠になる。
            // （実測: バリエーションのテクスチャが総入れ替えされている衣装では、
            //   テクスチャ照合が別部位を指す一方、部位キーだけが正解を一意に指した）
            // 前方一致だけの候補は曖昧（"Top" と "TopHat" 等）なので対象外にする。
            var partKeyHits = scored.Where(c => c.partKeyExact).ToList();
            if (partKeyHits.Count == 1)
            {
                partKeyHits[0].score += W_UNIQUE_PART_KEY;
                partKeyHits[0].reasons.Add("アバター内で唯一の一致");
            }

            result.candidates = scored
                .Where(c => c.score > 0)
                .OrderByDescending(c => c.score)
                .ToList();

            if (result.candidates.Count == 0)
            {
                result.outcome = MappingOutcome.NoCandidate;
                result.keptPrevious = group.GetEffectiveTargets().Count > 0;
                result.diagnosis =
                    "アバター上に手がかりのあるスロットが見つかりませんでした。\n" +
                    "この衣装がアバターに着せられているか、対象レンダラーを手動指定してください。";
                return result;
            }

            int best = result.candidates[0].score;

            // 最上位群（誤差内）を同一部位の複数メッシュ候補として扱う
            var top = result.candidates.Where(c => c.score >= best - SAME_PART_EPSILON).ToList();
            int second = result.candidates.Count > top.Count ? result.candidates[top.Count].score : 0;

            bool topSharesMaterial = top.Select(c => c.slot.material).Distinct().Count() == 1;

            // shader が違うスロットは「同じ部位の別バリエーション」ではない可能性が高いので自動採用しない。
            // （同名・共有テクスチャの加点だけで閾値を超えてしまうため、明示的な拒否条件にする）
            bool shaderMismatch = top.Any(c => c.shaderMismatch);

            if (best >= AUTO_ADOPT_SCORE && (best - second) >= AUTO_ADOPT_MARGIN
                && topSharesMaterial && !shaderMismatch)
            {
                Adopt(group, result, top.Select(c => c.slot).ToList(), MappingOutcome.Inferred);
                result.diagnosis =
                    $"参照は一致しませんでしたが、{top.Count}スロットを適用先と推定しました（確度 {best}）。\n" +
                    $"根拠: {result.candidates[0].ReasonText}\n" +
                    "意図と違う場合は手動で修正してください。";
                return result;
            }

            result.outcome = MappingOutcome.NeedsConfirmation;
            result.keptPrevious = group.GetEffectiveTargets().Count > 0;
            result.diagnosis = BuildAmbiguityDiagnosis(
                best, second, topSharesMaterial, shaderMismatch, result.candidates);
            return result;
        }

        private static string BuildAmbiguityDiagnosis(
            int best, int second, bool topSharesMaterial, bool shaderMismatch,
            List<MatchCandidate> candidates)
        {
            string why;
            if (shaderMismatch)
                why = "候補のシェーダーがバリエーションと異なるため（別部位の可能性）";
            else if (best < AUTO_ADOPT_SCORE)
                why = $"最有力候補の確度が低いため（{best} < {AUTO_ADOPT_SCORE}）";
            else if (!topSharesMaterial)
                why = "最有力候補が複数あり、それぞれ別のマテリアルを使っているため";
            else
                why = $"次点との差が小さいため（{best} vs {second}）";

            var lines = new List<string>
            {
                $"適用先を自動決定できませんでした（{why}）。候補から選んでください:"
            };
            foreach (var c in candidates.Take(5))
                lines.Add($"  ・{c.slot.Label}  確度{c.score}  ({c.ReasonText})");

            return string.Join("\n", lines);
        }

        /// <summary>採用したスロットを group に書き込む。</summary>
        private static void Adopt(MaterialGroup group, GroupMappingResult result,
            List<SlotRef> slots, MappingOutcome outcome)
        {
            var targets = new List<MaterialRenderTarget>();
            var seen = new HashSet<string>();
            foreach (var s in slots)
            {
                // 同名兄弟でも取りこぼさないよう、重複判定はインスタンス ID + スロットで行う
                string key = s.renderer.GetInstanceID() + "#" + s.slot;
                if (seen.Add(key))
                    targets.Add(new MaterialRenderTarget(s.path, s.slot));
            }

            group.renderTargets = targets;
            group.rendererPath = targets.Count > 0 ? targets[0].rendererPath : "";
            group.materialSlotIndex = targets.Count > 0 ? targets[0].materialSlotIndex : 0;

            result.adopted = targets;
            result.outcome = outcome;
        }

        /// <summary>既定バリエーションを、アバターが現在着ているマテリアルに合わせる。</summary>
        private static void AlignDefaultToCurrent(MaterialGroup group, Material current)
        {
            if (current == null) return;
            var match = group.variations.FirstOrDefault(v => v.material == current);
            if (match == null || match.isDefault) return;
            foreach (var v in group.variations) v.isDefault = false;
            match.isDefault = true;
        }

        // ==================================================================
        // スロット列挙と階層の警告
        // ==================================================================

        public static IEnumerable<SlotRef> EnumerateSlots(GameObject avatarRoot)
        {
            foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    // ルート自身のレンダラーは空パスになるが、空パスは「未設定」を意味するため
                    // 専用の番兵値に置き換える（MA 側もルートには専用値を使う）
                    string path = r.transform == avatarRoot.transform
                        ? MaterialRenderTarget.AvatarRootPath
                        : MaterialVariationDetector.GetRelativePath(avatarRoot.transform, r.transform);

                    yield return new SlotRef
                    {
                        renderer = r,
                        path = path,
                        slot = i,
                        material = mats[i],
                    };
                }
            }
        }

        /// <summary>
        /// パス解決を壊しうる階層状態を検出する。
        /// Modular Avatar は最終的に名前パス（または直接参照）でレンダラーを解決するため、
        /// 同名兄弟や名前中の '/' は適用先の取り違えを起こす。
        /// </summary>
        private static List<string> CollectHierarchyWarnings(GameObject avatarRoot, List<SlotRef> slots)
        {
            var warnings = new List<string>();

            // 同じ相対パスに解決される別レンダラー
            var byPath = slots
                .GroupBy(s => s.path)
                .Where(g => g.Select(s => s.renderer.GetInstanceID()).Distinct().Count() > 1)
                .ToList();
            foreach (var g in byPath.Take(5))
            {
                warnings.Add(
                    $"同名のオブジェクトが複数あり、パス '{g.Key}' が一意に解決できません。" +
                    "対象レンダラーを手動で指定し直すか、オブジェクト名を変更してください。");
            }

            // 名前に '/' を含むオブジェクト（パス区切りと解釈される）
            foreach (var r in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                if (r != avatarRoot.transform && r.name.Contains("/"))
                {
                    warnings.Add($"オブジェクト名に '/' が含まれています: '{r.name}'。" +
                                 "Modular Avatar のパス解決が壊れるため、名前を変更してください。");
                    break;
                }
            }

            return warnings;
        }

        // ==================================================================
        // グループのシグネチャとスコアリング
        // ==================================================================

        private class GroupSignature
        {
            public List<Material> variants;
            public HashSet<string> variantNames;    // バリエーションのマテリアル名
            public HashSet<string> partKey;         // 色トークンを除いた共通トークン（部位キー）
            public HashSet<string> invariantTex;    // 全バリエーションで共通のテクスチャ GUID（色以外）
            public HashSet<string> mainTex;         // バリエーションの _MainTex GUID 集合
            public HashSet<Shader> shaders;

            public static GroupSignature Build(MaterialGroup group, List<Material> variants)
            {
                var sig = new GroupSignature
                {
                    variants = variants,
                    variantNames = new HashSet<string>(variants.Select(v => v.name), StringComparer.OrdinalIgnoreCase),
                    shaders = new HashSet<Shader>(variants.Where(v => v.shader != null).Select(v => v.shader)),
                    mainTex = new HashSet<string>(variants.Select(MainTexGuid).Where(g => !string.IsNullOrEmpty(g))),
                };

                // 部位キー: 各バリエーション名から色トークンを除いたトークン集合の共通部分。
                // 例: ["Onepiece_Black", "Onepiece_White"] → {"onepiece"}
                HashSet<string> common = null;
                foreach (var v in variants)
                {
                    var toks = MeaningfulTokens(v.name);
                    if (common == null) common = toks;
                    else common.IntersectWith(toks);
                }
                sig.partKey = common ?? new HashSet<string>();

                // グループ名由来のトークンも部位キーに加える（フォルダ名で分類された場合の手がかり）
                foreach (var t in MeaningfulTokens(group.groupName)) sig.partKey.Add(t);

                // 不変テクスチャ: 全バリエーションが共有するテクスチャ GUID から _MainTex を除いたもの。
                // 色替えで差し替わらない normal/mask 等が残るため、部位同定の強い証拠になる。
                HashSet<string> texCommon = null;
                foreach (var v in variants)
                {
                    var t = TextureGuids(v);
                    if (texCommon == null) texCommon = t;
                    else texCommon.IntersectWith(t);
                }
                sig.invariantTex = texCommon ?? new HashSet<string>();
                if (sig.mainTex.Count == 1) sig.invariantTex.ExceptWith(sig.mainTex);

                return sig;
            }
        }

        private static MatchCandidate Score(SlotRef slot, GroupSignature sig)
        {
            var c = new MatchCandidate { slot = slot };
            var slotMat = slot.material;

            // shader 不一致は強い減点（別部位・別用途の可能性が高い）
            bool shaderMatch = slotMat.shader != null && sig.shaders.Contains(slotMat.shader);
            if (sig.shaders.Count > 0 && !shaderMatch)
            {
                c.score += W_SHADER_DIFFER;
                c.shaderMismatch = true;
            }
            else if (shaderMatch)
            {
                c.score += W_SHADER_EQUAL;
                c.reasons.Add("shader一致");
            }

            // 名前完全一致（別アセットだが同名 = 複製・別フォルダ配置）
            if (sig.variantNames.Contains(slotMat.name))
            {
                c.score += W_NAME_EQUAL;
                c.reasons.Add($"同名マテリアル '{slotMat.name}'");
            }

            // 部位キー
            var slotTokens = MeaningfulTokens(slotMat.name);
            var effectivePartKey = sig.partKey.Where(t => !GenericPartWords.Contains(t)).ToList();
            if (effectivePartKey.Count > 0)
            {
                int hits = effectivePartKey.Count(t => slotTokens.Contains(t));
                if (hits > 0)
                {
                    c.score += W_PART_KEY;
                    c.partKeyExact = true;
                    c.reasons.Add($"部位キー一致 '{string.Join(" ", effectivePartKey.Where(t => slotTokens.Contains(t)))}'");
                }
                else if (effectivePartKey.Any(t => StartsWithToken(slotMat.name, t)))
                {
                    c.score += W_PART_KEY_PREFIX;
                    c.reasons.Add("部位キー前方一致");
                }

                // レンダラー名も部位キーの手がかりになる
                var rendererTokens = MeaningfulTokens(slot.renderer.gameObject.name);
                if (effectivePartKey.Any(t => rendererTokens.Contains(t)))
                {
                    c.score += W_RENDERER_NAME;
                    c.reasons.Add($"レンダラー名一致 '{slot.renderer.gameObject.name}'");
                }
            }

            // テクスチャ資産の共有
            var slotTex = TextureGuids(slotMat);
            if (sig.invariantTex.Count > 0 && slotTex.Overlaps(sig.invariantTex))
            {
                c.score += W_INVARIANT_TEX;
                c.reasons.Add("共通テクスチャ共有");
            }
            string slotMain = MainTexGuid(slotMat);
            if (!string.IsNullOrEmpty(slotMain) && sig.mainTex.Contains(slotMain))
            {
                c.score += W_MAIN_TEX;
                c.reasons.Add("メインテクスチャ共有");
            }

            if (c.score < 0) c.score = 0;
            return c;
        }

        // ==================================================================
        // ヘルパー
        // ==================================================================

        /// <summary>マテリアル名を意味のあるトークン集合にする（色・UV・数字・汎用語を除く）。</summary>
        private static HashSet<string> MeaningfulTokens(string name)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(name)) return result;

            foreach (var t in MaterialVariationDetector.SplitTokens(MaterialVariationDetector.StripUvToken(name)))
            {
                if (t.Length <= 1) continue;
                if (t.All(char.IsDigit)) continue;
                if (MaterialVariationDetector.IsColorToken(t)) continue;
                result.Add(t);
            }
            return result;
        }

        private static bool StartsWithToken(string name, string token)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token)) return false;
            return name.StartsWith(token, StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> TextureGuids(Material m)
        {
            var result = new HashSet<string>();
            if (m == null || m.shader == null) return result;

            int count = ShaderUtil.GetPropertyCount(m.shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string prop = ShaderUtil.GetPropertyName(m.shader, i);
                if (!m.HasProperty(prop)) continue;

                var tex = m.GetTexture(prop);
                if (tex == null) continue;

                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path)) continue; // 永続 ID を持たないものは証拠にしない

                // 同一ファイル内のサブアセットを区別するため GUID + localId を組で使う
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex, out string guid, out long localId))
                    result.Add($"{guid}:{localId}");
                else
                    result.Add(AssetDatabase.AssetPathToGUID(path));
            }
            return result;
        }

        private static string MainTexGuid(Material m)
        {
            if (m == null || !m.HasProperty("_MainTex")) return null;
            var tex = m.GetTexture("_MainTex");
            if (tex == null) return null;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex, out string guid, out long localId))
                return $"{guid}:{localId}";
            string path = AssetDatabase.GetAssetPath(tex);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
#endif
