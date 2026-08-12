#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// 旧バージョン（直接書き込み方式）が Expression Parameters / FX レイヤーに残した
    /// "MatSwitch_" 残骸を掃除するユーティリティ。
    ///
    /// 安全設計:
    ///  - 自動実行はしない（手動メニューからのみ）。
    ///  - 削除対象を先に列挙し、確認ダイアログで承認された場合のみ削除する。
    ///  - 対象はシーン内で選択中のアバター1体のみ（未選択時は中断）。
    /// </summary>
    public static class ParamResidueCleaner
    {
        private const string PARAMETER_PREFIX = "MatSwitch_";

        [MenuItem("MIVI/VRC Material Switcher/残骸パラメータのクリーンアップ (選択アバター)", false, 41)]
        public static void CleanupResiduesMenu()
        {
            var avatar = FindTargetAvatar();
            if (avatar == null)
            {
                EditorUtility.DisplayDialog("クリーンアップ",
                    "対象アバターが特定できません。\nHierarchy でアバター（VRCAvatarDescriptor 付き）を選択してから実行してください。",
                    "OK");
                return;
            }

            var plan = BuildCleanupPlan(avatar);
            if (plan.Count == 0)
            {
                EditorUtility.DisplayDialog("クリーンアップ",
                    $"アバター '{avatar.gameObject.name}' に削除すべき残骸（{PARAMETER_PREFIX}*）は見つかりませんでした。",
                    "OK");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"アバター '{avatar.gameObject.name}' から以下の {plan.Count} 件を削除します:");
            sb.AppendLine();
            foreach (var item in plan.Take(20))
                sb.AppendLine("  - " + item);
            if (plan.Count > 20)
                sb.AppendLine($"  ... 他 {plan.Count - 20} 件");
            sb.AppendLine();
            sb.AppendLine("他のツールが同じ接頭辞を使っている場合、その資産も対象になります。よく確認してください。");

            if (!EditorUtility.DisplayDialog("残骸クリーンアップの確認", sb.ToString(), "削除する", "キャンセル"))
                return;

            int cleaned = ExecuteCleanup(avatar);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("クリーンアップ完了",
                $"{cleaned} 件の残骸を削除しました。", "OK");
        }

        /// <summary>選択中の VRCAvatarDescriptor を探す（選択オブジェクトの親方向も辿る）。</summary>
        private static VRCAvatarDescriptor FindTargetAvatar()
        {
            var selected = Selection.activeGameObject;
            if (selected != null)
            {
                var descriptor = selected.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor != null) return descriptor;
            }

            // 選択がない場合、シーンにアバターが1体だけならそれを対象にする
            var avatars = UnityEngine.Object.FindObjectsByType<VRCAvatarDescriptor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            return avatars.Length == 1 ? avatars[0] : null;
        }

        /// <summary>削除対象を実行せずに列挙する。</summary>
        private static List<string> BuildCleanupPlan(VRCAvatarDescriptor avatar)
        {
            var plan = new List<string>();

            if (avatar.expressionParameters != null)
            {
                foreach (var p in avatar.expressionParameters.parameters)
                {
                    if (p != null && p.name.StartsWith(PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase))
                        plan.Add($"Expression Parameter: {p.name}");
                }
            }

            var controller = GetFxController(avatar);
            if (controller != null)
            {
                foreach (var layer in controller.layers)
                {
                    if (layer.name.StartsWith(PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase))
                        plan.Add($"FX レイヤー: {layer.name}");
                }
                foreach (var p in controller.parameters)
                {
                    if (p.name.StartsWith(PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase))
                        plan.Add($"FX パラメータ: {p.name}");
                }
            }

            return plan;
        }

        /// <summary>承認済みの削除を実行する。戻り値は削除件数。</summary>
        private static int ExecuteCleanup(VRCAvatarDescriptor avatar)
        {
            int cleaned = 0;

            if (avatar.expressionParameters != null)
            {
                var par = avatar.expressionParameters;
                Undo.RecordObject(par, "Remove Material Switcher Residue Parameters");

                var list = par.parameters.ToList();
                int before = list.Count;
                list.RemoveAll(p => p.name.StartsWith(PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase));
                if (list.Count != before)
                {
                    par.parameters = list.ToArray();
                    EditorUtility.SetDirty(par);
                    cleaned += before - list.Count;
                }
            }

            var controller = GetFxController(avatar);
            if (controller != null)
            {
                Undo.RecordObject(controller, "Remove Material Switcher Residue Layers");

                bool changed = false;
                for (int i = controller.layers.Length - 1; i >= 0; i--)
                {
                    if (controller.layers[i].name.StartsWith(PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        controller.RemoveLayer(i);
                        changed = true;
                        cleaned++;
                    }
                }
                for (int i = controller.parameters.Length - 1; i >= 0; i--)
                {
                    if (controller.parameters[i].name.StartsWith(PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        controller.RemoveParameter(i);
                        changed = true;
                        cleaned++;
                    }
                }
                if (changed)
                    EditorUtility.SetDirty(controller);
            }

            return cleaned;
        }

        private static UnityEditor.Animations.AnimatorController GetFxController(VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers == null) return null;
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && layer.animatorController != null)
                    return layer.animatorController as UnityEditor.Animations.AnimatorController;
            }
            return null;
        }
    }
}
#endif
