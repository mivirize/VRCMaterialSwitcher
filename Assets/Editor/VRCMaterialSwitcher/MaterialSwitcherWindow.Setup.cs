#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace VRCMaterialSwitcher
{
    public partial class MaterialSwitcherWindow
    {
        // ========================================
        // セットアップ実行セクション
        // ========================================

        private void DrawSetupSection()
        {
            var enabledGroups = config.materialGroups
                .Where(g => g.enabled && g.EnabledVariationCount >= 2).ToList();
            if (enabledGroups.Count == 0) return;

            EditorGUILayout.Space(8);
            DrawHorizontalLine();
            EditorGUILayout.Space(4);

            // セットアップ情報サマリー
            int totalVariations = enabledGroups.Sum(g => g.EnabledVariationCount);
            EditorGUILayout.LabelField(
                $"セットアップ: {enabledGroups.Count}グループ / {totalVariations}バリエーション",
                subHeaderStyle);

            // ---- パラメータコスト計算（VRChat 同期パラメータ上限 = 合計 256bit）----
            int maxBits    = VRCExpressionParameters.MAX_PARAMETER_COST;
            int otherBits  = MaterialSwitcherCostCalculator.GetOtherParameterBitsCached(avatarObject);
            bool costOk    = otherBits >= 0;
            int toolBits   = MaterialSwitcherCostCalculator.EstimateSwitcherBits(
                                 enabledGroups, config.parameterSynced);
            int totalBits  = (costOk ? otherBits : 0) + toolBits;
            int remaining  = maxBits - totalBits;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("パラメータコスト（VRChat 同期上限）", EditorStyles.boldLabel);
                    if (GUILayout.Button("↻ 再計算", GUILayout.Width(70)))
                        InvalidateCaches();
                }

                if (avatarObject == null)
                    EditorGUILayout.LabelField(
                        "アバター未設定のため集計できません。", EditorStyles.miniLabel);
                else if (!costOk)
                    EditorGUILayout.LabelField(
                        "既存パラメータの集計に失敗しました（Console を確認）。", EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(
                        $"アバター既存（他ギミック・MA 生成を含む）: {otherBits} bit",
                        EditorStyles.miniLabel);

                EditorGUILayout.LabelField(
                    config.parameterSynced
                        ? $"本ツール（{enabledGroups.Count}グループ）: {toolBits} bit"
                        : "本ツール: 0 bit（Synced オフ）",
                    EditorStyles.miniLabel);

                // 使用率バー
                Rect barRect = EditorGUILayout.GetControlRect(false, 18);
                float frac = maxBits > 0 ? Mathf.Clamp01(totalBits / (float)maxBits) : 0f;
                EditorGUI.ProgressBar(barRect, frac, $"{totalBits} / {maxBits} bit");

                if (costOk && config.parameterSynced)
                {
                    if (remaining >= 0)
                        EditorGUILayout.LabelField(
                            $"残り: {remaining} bit → あと約 {remaining / 8} グループ追加可能" +
                            $"（3択以上=8bit / 2択なら最大 {remaining} 個）",
                            EditorStyles.miniLabel);
                    else
                        EditorGUILayout.LabelField(
                            $"超過: {-remaining} bit オーバー → " +
                            $"3択以上グループを約 {(-remaining + 7) / 8} 個減らす／Synced オフで解消",
                            EditorStyles.miniLabel);
                }
                else if (!config.parameterSynced)
                    EditorGUILayout.LabelField(
                        "Synced オフのため同期コスト 0bit（他者に色は同期されません）",
                        EditorStyles.miniLabel);

                EditorGUILayout.LabelField(
                    "※他ギミックを編集した場合は「↻ 再計算」で更新してください",
                    EditorStyles.miniLabel);
            }

            // 上限超過・逼迫の警告
            if (costOk && config.parameterSynced && totalBits > maxBits)
            {
                EditorGUILayout.HelpBox(
                    $"同期パラメータが上限を {totalBits - maxBits} bit 超過しています" +
                    $"（合計 {totalBits} / {maxBits} bit）。\n" +
                    "このままではアップロードできません（VRCExpressionParameters has too many parameters）。\n" +
                    "・不要なグループを無効化する（マテリアルグループ欄のチェックを外す）\n" +
                    "・メニュー設定の「Synced」をオフにする（コスト 0bit）",
                    MessageType.Error);
            }
            else if (costOk && config.parameterSynced && totalBits > maxBits * 0.9f)
            {
                EditorGUILayout.HelpBox(
                    $"上限が近づいています（残り {remaining} bit）。追加は慎重に。",
                    MessageType.Warning);
            }

            // ---- 容量（Uncompressed Size）----
            MaterialSwitcherTextureAnalyzer.EnsureSizeCache(avatarObject, enabledGroups);
            const float MB = 1024f * 1024f;
            float swMB    = MaterialSwitcherTextureAnalyzer.CachedSwitcherTexBytes / MB;
            float avMB    = MaterialSwitcherTextureAnalyzer.CachedAvatarTexBytes   / MB;
            float texMB   = MaterialSwitcherTextureAnalyzer.CachedTotalTexBytes    / MB;
            float meshMB  = MaterialSwitcherTextureAnalyzer.CachedMeshBytes        / MB;
            float grandMB = texMB + meshMB;
            float limMB   = UncompressedSizeLimit / MB;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "容量（Uncompressed Size 目安 / PC 上限 約 500MB）", EditorStyles.boldLabel);

                EditorGUILayout.LabelField($"　切替同梱テクスチャ: {swMB:F0} MB", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"　アバター本体・装着物テクスチャ: {avMB:F0} MB", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"　メッシュ: {meshMB:F0} MB", EditorStyles.miniLabel);

                Rect sizeBar = EditorGUILayout.GetControlRect(false, 18);
                float sfrac = limMB > 0 ? Mathf.Clamp01(grandMB / limMB) : 0f;
                EditorGUI.ProgressBar(sizeBar, sfrac, $"合計概算 {grandMB:F0} / {limMB:F0} MB");

                EditorGUILayout.LabelField(
                    "※アニメ/シェーダ/VRCFury 等は未計上。実値は SDK Build 画面の Uncompressed Size で確認",
                    EditorStyles.miniLabel);

                if (grandMB > limMB)
                    EditorGUILayout.HelpBox(
                        $"合計概算が上限（{limMB:F0}MB）を超えています（{grandMB:F0}MB）。\n" +
                        "・使わない衣装/小物をアバターから外す（非表示でも同梱されます）\n" +
                        "・下記でテクスチャ解像度を下げる／バリエーションを減らす",
                        MessageType.Error);

                EditorGUILayout.Space(2);

                // 解像度ダウン
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("最大解像度:", GUILayout.Width(70));
                    if (GUILayout.Toggle(reduceTargetSize == 2048, "2048",
                            EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                        reduceTargetSize = 2048;
                    if (GUILayout.Toggle(reduceTargetSize == 1024, "1024",
                            EditorStyles.miniButtonMid, GUILayout.Width(50)))
                        reduceTargetSize = 1024;
                    if (GUILayout.Toggle(reduceTargetSize == 512, "512",
                            EditorStyles.miniButtonMid, GUILayout.Width(50)))
                        reduceTargetSize = 512;
                    if (GUILayout.Toggle(reduceTargetSize == 256, "256",
                            EditorStyles.miniButtonRight, GUILayout.Width(50)))
                        reduceTargetSize = 256;
                    reduceUseCrunch = EditorGUILayout.ToggleLeft(
                        "Crunch 圧縮", reduceUseCrunch, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"↓ 切替色のみ {reduceTargetSize} 以下に"))
                        ApplyReduce(
                            MaterialSwitcherTextureAnalyzer.CollectSwitcherTextures(enabledGroups),
                            "切替色のバリエーション");

                    if (GUILayout.Button($"↓ アバター全体を {reduceTargetSize} 以下に"))
                    {
                        var all = MaterialSwitcherTextureAnalyzer.CollectAvatarTextures(avatarObject);
                        all.UnionWith(
                            MaterialSwitcherTextureAnalyzer.CollectSwitcherTextures(enabledGroups));
                        ApplyReduce(all, "アバター全体（本体・髪・顔・全衣装を含む）");
                    }
                }
            }

            // 未マッピング警告
            var unmapped = enabledGroups.Where(g => !g.HasRenderTarget).ToList();
            if (unmapped.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{unmapped.Count}グループのレンダラーが未設定です。\n" +
                    "「自動マッピング」を実行するか、手動で設定してください。\n" +
                    "未設定のグループはメニューのみ作成されます（マテリアル切替なし）。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                // プレビューボタン
                if (GUILayout.Button("👁 プレビュー", GUILayout.Height(32)))
                {
                    PreviewSetup(enabledGroups);
                }

                // セットアップ実行ボタン
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
                if (GUILayout.Button("✓ セットアップ実行", GUILayout.Height(32)))
                {
                    ExecuteSetup();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        // ========================================
        // 既存セットアップ管理
        // ========================================

        private void DrawExistingSetupSection()
        {
            if (avatarObject == null) return;

            var existingSetups = MaterialSwitcherSetup.GetExistingSetup(avatarObject);
            if (existingSetups.Count == 0) return;

            EditorGUILayout.Space(8);
            DrawHorizontalLine();
            EditorGUILayout.Space(4);

            foldoutExisting = EditorGUILayout.Foldout(foldoutExisting,
                $"▼ 既存セットアップ管理 ({existingSetups.Count}件)", true, EditorStyles.foldoutHeader);
            if (!foldoutExisting) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                foreach (var (groupName, varCount) in existingSetups)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"✓ {groupName} ({varCount}色)", GUILayout.Width(200));

                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("削除", GUILayout.Width(60)))
                        {
                            if (EditorUtility.DisplayDialog("確認",
                                $"「{groupName}」のセットアップを削除しますか？", "削除", "キャンセル"))
                            {
                                MaterialSwitcherSetup.RemoveGroupSetup(avatarObject, groupName);
                                ShowMessage($"「{groupName}」を削除しました。", MessageType.Info);
                            }
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }

                EditorGUILayout.Space(4);

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("全セットアップを削除"))
                {
                    if (EditorUtility.DisplayDialog("確認",
                        "VRC Material Switcher の全セットアップを削除しますか？\nこの操作は Undo で元に戻せます。",
                        "全削除", "キャンセル"))
                    {
                        // Fix #9: config.menuName を渡す
                        int count = MaterialSwitcherSetup.RemoveSetup(avatarObject, config.menuName);
                        ShowMessage($"{count}件のセットアップを削除しました。", MessageType.Info);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
        }
    }
}
#endif
