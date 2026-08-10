#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// VRChat 同期パラメータのビットコスト計算を担当する静的クラス。
    /// NDMF の ParameterInfo を使って「ビルド後の実コスト」を集計し、
    /// 本ツールが追加するビット数も見積もる（いずれもキャッシュ付き）。
    /// </summary>
    internal static class MaterialSwitcherCostCalculator
    {
        // -2: 未計算、-1: 取得失敗（ParameterInfo の例外）、0+: 集計済み
        private static int    _cachedOtherBits  = -2;
        private static GameObject _costCacheAvatar;

        /// <summary>パラメータコストキャッシュを無効化する。</summary>
        public static void InvalidateCostCache()
        {
            _cachedOtherBits = -2;
        }

        /// <summary>
        /// 「本ツール以外」の同期パラメータビット数をキャッシュ付きで返す。
        /// avatarObject が変わった場合はキャッシュを自動的に破棄する。
        /// 戻り値が -1 の場合は集計失敗（Console を確認）。
        /// </summary>
        public static int GetOtherParameterBitsCached(GameObject avatarObject)
        {
            if (avatarObject != _costCacheAvatar)
            {
                _costCacheAvatar = avatarObject;
                _cachedOtherBits = -2;
            }
            if (_cachedOtherBits == -2)
                _cachedOtherBits = ComputeOtherParameterBits(avatarObject);
            return _cachedOtherBits;
        }

        /// <summary>
        /// NDMF の ParameterInfo を使ってアバターの実コスト（他ギミック・MA 生成を含む）を集計する。
        /// 本ツール自身のパラメータ（MatSwitch_*）は二重計上を防ぐため除外する。
        /// Bool=1bit、Int/Float=8bit、非同期=0bit として集計する。
        /// </summary>
        private static int ComputeOtherParameterBits(GameObject avatarObject)
        {
            if (avatarObject == null) return 0;
            try
            {
                int bits = 0;
                foreach (var p in nadena.dev.ndmf.ParameterInfo.ForUI.GetParametersForObject(avatarObject))
                {
                    // Expression（Animator 名前空間）の同期パラメータのみが 256bit 予算に計上される
                    if (p.Namespace != nadena.dev.ndmf.ParameterNamespace.Animator) continue;

                    // MatSwitch_* は本ツール分として EstimateSwitcherBits で別途見積るため除外
                    string nm = p.EffectiveName ?? p.OriginalName;
                    if (!string.IsNullOrEmpty(nm) && nm.StartsWith("MatSwitch_")) continue;

                    bits += p.BitUsage;
                }
                return bits;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VRC Material Switcher] パラメータコスト集計に失敗: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 本ツールが追加する同期パラメータビットを見積もる。
        /// 本ツールは MA Parameters で常に Int を明示宣言する（MaterialSwitcherSetup の
        /// SetupParameterDeclarations 参照）ため、バリエーション数によらず 1 グループ = 8bit。
        /// parameterSynced が false の場合は 0bit を返す。
        /// </summary>
        public static int EstimateSwitcherBits(List<MaterialGroup> enabledGroups, bool parameterSynced)
        {
            if (!parameterSynced) return 0;
            int bits = 0;
            foreach (var g in enabledGroups)
            {
                if (g.EnabledVariationCount < 2) continue;
                bits += 8; // ParameterSyncType.Int
            }
            return bits;
        }
    }
}
#endif
