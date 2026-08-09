#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// マテリアルバリエーションの1つ（例: shirt_black.mat）
    /// </summary>
    [Serializable]
    public class MaterialVariation
    {
        /// <summary>表示名（例: "black", "deepsea"）</summary>
        public string displayName;

        /// <summary>対応するマテリアルアセット</summary>
        public Material material;

        /// <summary>デフォルトバリエーションかどうか</summary>
        public bool isDefault;

        /// <summary>メニューに含めるかどうか</summary>
        public bool includeInMenu = true;

        public MaterialVariation() { }

        public MaterialVariation(string displayName, Material material, bool isDefault = false)
        {
            this.displayName = displayName;
            this.material = material;
            this.isDefault = isDefault;
            this.includeInMenu = true;
        }
    }

    /// <summary>
    /// マテリアル切替の適用先（1つのレンダラー＋スロット）。
    /// 浴衣の上下（Yukata A/B）のように、同じグループが複数メッシュに適用される場合に複数保持する。
    /// </summary>
    [Serializable]
    public class MaterialRenderTarget
    {
        /// <summary>対象レンダラーのパス（アバタールートからの相対パス）</summary>
        public string rendererPath;

        /// <summary>レンダラー上のマテリアルスロットインデックス</summary>
        public int materialSlotIndex;

        public MaterialRenderTarget() { }

        public MaterialRenderTarget(string rendererPath, int materialSlotIndex)
        {
            this.rendererPath = rendererPath;
            this.materialSlotIndex = materialSlotIndex;
        }
    }

    /// <summary>
    /// マテリアルグループ（例: "shirt" → shirt.mat, shirt_black.mat, shirt_deepsea.mat）
    /// </summary>
    [Serializable]
    public class MaterialGroup
    {
        /// <summary>グループ名（例: "shirt"）</summary>
        public string groupName;

        /// <summary>このグループのバリエーション一覧</summary>
        public List<MaterialVariation> variations = new List<MaterialVariation>();

        /// <summary>メニューに追加するかどうか</summary>
        public bool enabled = true;

        /// <summary>UI上の折りたたみ状態</summary>
        [NonSerialized]
        public bool foldout = true;

        /// <summary>
        /// 対象レンダラーのパス（アバタールートからの相対パス）。
        /// 後方互換用の主ターゲット。実際の適用先は renderTargets を参照（GetEffectiveTargets）。
        /// </summary>
        public string rendererPath;

        /// <summary>
        /// レンダラー上のマテリアルスロットインデックス（後方互換用の主ターゲット）
        /// </summary>
        public int materialSlotIndex;

        /// <summary>
        /// マテリアル切替の適用先一覧（複数メッシュ対応：浴衣の上下など）。
        /// 空の場合は rendererPath / materialSlotIndex を単一ターゲットとして使用する。
        /// </summary>
        public List<MaterialRenderTarget> renderTargets = new List<MaterialRenderTarget>();

        /// <summary>バリエーション数</summary>
        public int VariationCount => variations.Count;

        /// <summary>メニューに含めるバリエーション数</summary>
        public int EnabledVariationCount
        {
            get
            {
                int count = 0;
                foreach (var v in variations)
                    if (v.includeInMenu) count++;
                return count;
            }
        }

        /// <summary>デフォルトバリエーションを取得</summary>
        public MaterialVariation GetDefault()
        {
            foreach (var v in variations)
                if (v.isDefault) return v;
            return variations.Count > 0 ? variations[0] : null;
        }

        /// <summary>
        /// 実際に適用すべきレンダラーターゲット一覧を返す。
        /// renderTargets が設定されていればそれを、無ければ単一の rendererPath を返す（後方互換）。
        /// </summary>
        public List<MaterialRenderTarget> GetEffectiveTargets()
        {
            var result = new List<MaterialRenderTarget>();

            if (renderTargets != null && renderTargets.Count > 0)
            {
                foreach (var t in renderTargets)
                    if (t != null && !string.IsNullOrEmpty(t.rendererPath))
                        result.Add(t);
                return result;
            }

            if (!string.IsNullOrEmpty(rendererPath))
                result.Add(new MaterialRenderTarget(rendererPath, materialSlotIndex));

            return result;
        }

        /// <summary>マッピング済み（適用先が1つ以上ある）か</summary>
        public bool HasRenderTarget => GetEffectiveTargets().Count > 0;
    }

    /// <summary>
    /// ツール全体の設定データ
    /// </summary>
    [Serializable]
    public class SwitcherConfig
    {
        /// <summary>Expression Menuに表示するサブメニュー名</summary>
        public string menuName = "衣装カラー";

        /// <summary>パラメータを保存するか（saved）</summary>
        public bool parameterSaved = true;

        /// <summary>パラメータを同期するか（synced）</summary>
        public bool parameterSynced = true;

        /// <summary>セットアップモード</summary>
        public SetupMode setupMode = SetupMode.ModularAvatar;

        /// <summary>検出済みマテリアルグループ</summary>
        public List<MaterialGroup> materialGroups = new List<MaterialGroup>();

        /// <summary>スキャン対象フォルダパス</summary>
        public string scanFolderPath = "";
    }

    /// <summary>
    /// セットアップモード
    /// </summary>
    public enum SetupMode
    {
        /// <summary>Modular Avatarコンポーネントを使用（推奨）</summary>
        ModularAvatar,

        /// <summary>VRC SDK直接操作（MAなし環境用）</summary>
        DirectSDK
    }

    /// <summary>
    /// セットアップ結果の情報
    /// </summary>
    public class SetupResult
    {
        public bool success;
        public string message;
        public List<GameObject> createdObjects = new List<GameObject>();
        public int menuItemCount;
        public int parameterCount;

        public static SetupResult Success(string message, int menuItems = 0, int parameters = 0)
        {
            return new SetupResult
            {
                success = true,
                message = message,
                menuItemCount = menuItems,
                parameterCount = parameters
            };
        }

        public static SetupResult Failure(string message)
        {
            return new SetupResult
            {
                success = false,
                message = message
            };
        }
    }
}
#endif
