#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    public partial class MaterialSwitcherWindow
    {
        // ========================================
        // レンダラーマッピング
        // ========================================

        private void DrawRendererSection()
        {
            if (config.materialGroups.Count == 0 || avatarObject == null) return;

            EditorGUILayout.Space(4);
            foldoutRenderers = EditorGUILayout.Foldout(foldoutRenderers,
                "▼ レンダラーマッピング", true, EditorStyles.foldoutHeader);
            if (!foldoutRenderers) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                EditorGUILayout.LabelField(
                    "各グループが適用されるレンダラーとスロットを指定", EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("自動マッピング"))
                {
                    int mapped = MaterialVariationDetector.AutoMapRenderers(
                        avatarObject, config.materialGroups);
                    if (mapped > 0) configDirty = true; // 自動マッピングで変更があれば保存
                    ShowMessage(
                        mapped > 0
                            ? $"レンダラーを自動マッピングしました（{mapped}グループ成功）。"
                            : "自動マッピングできるレンダラーが見つかりませんでした。\n" +
                              "アバターのメッシュに対象マテリアルが適用されているか確認してください。",
                        mapped > 0 ? MessageType.Info : MessageType.Warning);
                }

                EditorGUILayout.HelpBox(
                    "1グループが複数メッシュに適用される場合（浴衣の上下など）は、\n" +
                    "「＋ 対象レンダラー追加」で適用先を複数登録できます。",
                    MessageType.None);

                EditorGUILayout.Space(4);

                foreach (var group in config.materialGroups)
                {
                    if (!group.enabled) continue;
                    DrawGroupRenderTargets(group);
                }
            }
        }

        /// <summary>
        /// 1グループのレンダラーターゲット（複数可）を表示・編集する。
        /// Fix #6: 移行ロジックは LoadConfig 内で済んでいるため、ここでは行わない。
        /// Fix #6: back-sync はレンダラー変更・追加・削除時のみ行い、毎フレーム実行しない。
        /// Fix #10: 割り当てられたレンダラーがアバター配下でない場合は拒否する。
        /// </summary>
        private void DrawGroupRenderTargets(MaterialGroup group)
        {
            if (group.renderTargets == null)
                group.renderTargets = new System.Collections.Generic.List<MaterialRenderTarget>();

            // Fix #6: 旧形式移行は LoadConfig で済んでいる。ここでは行わない。

            using (new EditorGUILayout.VerticalScope(groupBoxStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(group.groupName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    string status = group.renderTargets.Count == 0
                        ? "未設定"
                        : $"{group.renderTargets.Count}メッシュ";
                    EditorGUILayout.LabelField(status, EditorStyles.miniLabel, GUILayout.Width(80));
                }

                if (group.renderTargets.Count == 0)
                {
                    EditorGUILayout.LabelField("(適用先レンダラーが未設定)", EditorStyles.miniLabel);
                }

                int removeIndex = -1;
                for (int ti = 0; ti < group.renderTargets.Count; ti++)
                {
                    var target = group.renderTargets[ti];

                    // パスから Renderer を解決
                    Renderer currentRenderer = null;
                    if (!string.IsNullOrEmpty(target.rendererPath))
                    {
                        Transform t = avatarObject.transform.Find(target.rendererPath);
                        if (t != null) currentRenderer = t.GetComponent<Renderer>();
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var renderer = EditorGUILayout.ObjectField(
                            currentRenderer, typeof(Renderer), true) as Renderer;

                        if (EditorGUI.EndChangeCheck() && renderer != null)
                        {
                            // Fix #10: アバター配下でないレンダラーは拒否する
                            if (avatarObject == null ||
                                !renderer.transform.IsChildOf(avatarObject.transform))
                            {
                                ShowMessage(
                                    "アバター配下でないレンダラーは設定できません。\n" +
                                    "アバターの子オブジェクトのレンダラーのみ指定できます。",
                                    MessageType.Warning);
                            }
                            else
                            {
                                target.rendererPath = MaterialVariationDetector.GetRelativePath(
                                    avatarObject.transform, renderer.transform);
                                target.materialSlotIndex = 0;
                                // Fix #6: 変更時のみ back-sync する
                                SyncLegacyRenderTargetFields(group);
                            }
                        }

                        // マテリアルスロット選択
                        if (currentRenderer != null)
                        {
                            var mats = currentRenderer.sharedMaterials;
                            string[] slotNames = new string[mats.Length];
                            for (int s = 0; s < mats.Length; s++)
                            {
                                slotNames[s] = $"[{s}] {(mats[s] != null ? mats[s].name : "null")}";
                            }
                            int slot = Mathf.Clamp(
                                target.materialSlotIndex, 0, Mathf.Max(0, mats.Length - 1));
                            target.materialSlotIndex = EditorGUILayout.Popup(
                                slot, slotNames, GUILayout.Width(150));
                        }
                        else
                        {
                            EditorGUILayout.LabelField(
                                $"Slot [{target.materialSlotIndex}]", GUILayout.Width(150));
                        }

                        if (GUILayout.Button("－", GUILayout.Width(24), GUILayout.Height(18)))
                        {
                            removeIndex = ti;
                        }
                    }
                }

                if (removeIndex >= 0)
                {
                    group.renderTargets.RemoveAt(removeIndex);
                    // Fix #6: 削除時のみ back-sync する
                    SyncLegacyRenderTargetFields(group);
                    GUI.changed = true;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("＋ 対象レンダラー追加", GUILayout.Width(170)))
                    {
                        group.renderTargets.Add(new MaterialRenderTarget("", 0));
                        configDirty = true;
                        // 空エントリ追加時は back-sync 不要（rendererPath が空のため）
                    }
                }
            }
        }

        /// <summary>
        /// Fix #6: renderTargets の先頭エントリを後方互換フィールド（rendererPath / materialSlotIndex）
        /// に同期する。レンダラーの変更・削除が発生したときのみ呼び出す（毎フレームは実行しない）。
        /// </summary>
        private static void SyncLegacyRenderTargetFields(MaterialGroup group)
        {
            if (group.renderTargets.Count > 0)
            {
                group.rendererPath        = group.renderTargets[0].rendererPath;
                group.materialSlotIndex   = group.renderTargets[0].materialSlotIndex;
            }
            else
            {
                group.rendererPath = "";
            }
        }
    }
}
#endif
