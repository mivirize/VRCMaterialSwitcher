#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRCMaterialSwitcher
{
    public partial class MaterialSwitcherWindow
    {
        // ========================================
        // 削除リクエスト処理
        // ========================================

        /// <summary>
        /// 削除リクエストをフレーム冒頭でまとめて処理する。
        /// OnGUI の反復中にコレクションを変更するとレイアウトが破壊されるため遅延実行する。
        /// Fix #4: 削除後に configDirty を設定する。
        /// </summary>
        private void ProcessPendingRemovals()
        {
            if (pendingRemoveGroup != null)
            {
                string removedName = pendingRemoveGroup.groupName;
                config.materialGroups.Remove(pendingRemoveGroup);
                pendingRemoveGroup = null;
                ShowMessage($"グループ「{removedName}」を削除しました。", MessageType.Info);
                configDirty = true; // Fix #4
            }

            if (pendingVarGroup != null && pendingRemoveVariation != null)
            {
                bool wasDefault = pendingRemoveVariation.isDefault;
                pendingVarGroup.variations.Remove(pendingRemoveVariation);

                // デフォルトが削除された場合は先頭を新しいデフォルトに昇格させる
                if (wasDefault && pendingVarGroup.variations.Count > 0
                    && !pendingVarGroup.variations.Any(v => v.isDefault))
                {
                    pendingVarGroup.variations[0].isDefault = true;
                }

                pendingVarGroup = null;
                pendingRemoveVariation = null;
                configDirty = true; // Fix #4
            }
        }

        // ========================================
        // マテリアルグループ表示
        // ========================================

        private void DrawMaterialGroupsSection()
        {
            EditorGUILayout.Space(4);
            foldoutGroups = EditorGUILayout.Foldout(foldoutGroups,
                $"▼ マテリアルグループ ({config.materialGroups.Count}件)", true, EditorStyles.foldoutHeader);
            if (!foldoutGroups) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                // ヘッダー操作行（新規作成 ＋ 一括操作）
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                    if (GUILayout.Button("＋ 新規グループ作成", GUILayout.Height(24)))
                    {
                        AddNewGroup();
                    }
                    GUI.backgroundColor = Color.white;

                    if (config.materialGroups.Count > 0)
                    {
                        if (GUILayout.Button("全て有効", GUILayout.Width(72), GUILayout.Height(24)))
                        {
                            foreach (var g in config.materialGroups) g.enabled = true;
                            configDirty = true;
                        }
                        if (GUILayout.Button("全て無効", GUILayout.Width(72), GUILayout.Height(24)))
                        {
                            foreach (var g in config.materialGroups) g.enabled = false;
                            configDirty = true;
                        }
                    }
                }

                // グループが空のときは案内を表示
                if (config.materialGroups.Count == 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "マテリアルグループがありません。\n" +
                        "・「衣装フォルダスキャン」で自動検出する\n" +
                        "・「＋ 新規グループ作成」で手動で追加する\n" +
                        "のいずれかでグループを用意してください。",
                        MessageType.Info);
                    DrawUngroupedMaterialsHint();
                    return;
                }

                EditorGUILayout.Space(4);

                // 反復中はコレクションを変更しない（削除は pending に積んで冒頭で処理）
                foreach (var group in config.materialGroups)
                {
                    DrawMaterialGroupItem(group);
                }

                // Fix #7: グループ化できなかったマテリアルのヒント表示
                DrawUngroupedMaterialsHint();
            }
        }

        /// <summary>Fix #7: 直近スキャンでグループ化できなかったマテリアル名を HelpBox で案内する。</summary>
        private void DrawUngroupedMaterialsHint()
        {
            if (ungroupedMaterials == null || ungroupedMaterials.Count == 0) return;

            const int maxDisplay = 10;
            var display = ungroupedMaterials.Take(maxDisplay).ToList();
            string names = string.Join(", ", display);
            if (ungroupedMaterials.Count > maxDisplay)
                names += $" 他{ungroupedMaterials.Count - maxDisplay}件";

            EditorGUILayout.HelpBox(
                $"グループ化できなかったマテリアル（{ungroupedMaterials.Count}件）: {names}\n" +
                "「＋ 新規グループ作成」から手動でグループ化できます",
                MessageType.Info);
        }

        private void DrawMaterialGroupItem(MaterialGroup group)
        {
            using (new EditorGUILayout.VerticalScope(groupBoxStyle))
            {
                // ヘッダー行
                using (new EditorGUILayout.HorizontalScope())
                {
                    group.enabled = EditorGUILayout.Toggle(group.enabled, GUILayout.Width(16));

                    group.foldout = EditorGUILayout.Foldout(group.foldout,
                        $"{group.groupName} ({group.EnabledVariationCount}/{group.VariationCount}バリエーション)",
                        true);

                    // 削除ボタン（実際の削除は次フレーム冒頭で安全に処理）
                    GUI.backgroundColor = new Color(0.95f, 0.55f, 0.55f);
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        pendingRemoveGroup = group;
                        Repaint();
                    }
                    GUI.backgroundColor = Color.white;
                }

                if (!group.foldout) return;

                // Fix #8: シェーダー混在などの警告を表示する
                if (!string.IsNullOrEmpty(group.warning))
                    EditorGUILayout.HelpBox(group.warning, MessageType.Warning);

                EditorGUI.indentLevel++;

                // グループ名編集（手動作成・リネーム対応）
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("グループ名", GUILayout.Width(90));
                    group.groupName = EditorGUILayout.TextField(group.groupName);
                }

                EditorGUILayout.Space(2);

                // バリエーション一覧
                foreach (var variation in group.variations)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        bool newInclude = EditorGUILayout.Toggle(variation.includeInMenu, GUILayout.Width(16));
                        if (EditorGUI.EndChangeCheck())
                        {
                            variation.includeInMenu = newInclude;
                            configDirty = true; // Fix #3: replace SaveConfig()
                            Repaint();
                        }

                        // デフォルトマーカー
                        EditorGUILayout.LabelField(variation.isDefault ? "● " : "○ ", GUILayout.Width(20));

                        // 表示名編集
                        variation.displayName = EditorGUILayout.TextField(
                            variation.displayName, GUILayout.Width(100));

                        // マテリアル参照
                        EditorGUI.BeginChangeCheck();
                        var newMat = (Material)EditorGUILayout.ObjectField(
                            variation.material, typeof(Material), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            variation.material = newMat;
                            configDirty = true; // Fix #3: replace SaveConfig()
                            Repaint();
                        }

                        // デフォルト設定ボタン
                        GUI.enabled = !variation.isDefault;
                        if (GUILayout.Button("★", GUILayout.Width(24), GUILayout.Height(18)))
                        {
                            foreach (var v in group.variations) v.isDefault = false;
                            variation.isDefault = true;
                            configDirty = true; // Fix #3: replace SaveConfig()
                            Repaint();
                        }
                        GUI.enabled = true;

                        // バリエーション削除ボタン（pending に積んで冒頭で処理）
                        if (GUILayout.Button("－", GUILayout.Width(24), GUILayout.Height(18)))
                        {
                            pendingVarGroup = group;
                            pendingRemoveVariation = variation;
                            Repaint();
                        }
                    }
                }

                // バリエーション追加ボタン
                if (GUILayout.Button("＋ バリエーション追加", GUILayout.Width(160)))
                {
                    // 最初のバリエーションなら自動的にデフォルト扱い
                    bool isFirst = group.variations.Count == 0;
                    group.variations.Add(new MaterialVariation("new", null, isFirst));
                    configDirty = true;
                }

                EditorGUI.indentLevel--;
            }
        }
    }
}
#endif
