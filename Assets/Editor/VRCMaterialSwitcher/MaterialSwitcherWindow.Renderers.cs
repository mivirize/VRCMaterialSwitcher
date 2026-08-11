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
                    lastMappingReport = MaterialSwitcherRendererMatcher.MapAll(
                        avatarObject, config.materialGroups);
                    expandedNoCandidateGroups.Clear();

                    if (lastMappingReport.AdoptedGroupCount > 0)
                        configDirty = true;

                    int total = config.materialGroups.Count;
                    int adopted = lastMappingReport.AdoptedGroupCount;
                    int needsConf = lastMappingReport.NeedsConfirmationCount;
                    int noCandidate = lastMappingReport.NoCandidateCount;

                    if (adopted == total)
                    {
                        ShowMessage(
                            $"適用先を決定しました（{adopted}/{total}グループ）。",
                            MessageType.Info);
                    }
                    else
                    {
                        ShowMessage(
                            $"適用先を決定しました（{adopted}/{total}グループ）。\n" +
                            $"要確認: {needsConf}グループ / 候補なし: {noCandidate}グループ\n" +
                            "詳細は各グループの下に表示されています。",
                            MessageType.Warning);
                    }

                    // レポートの有無で以降の制御数が変わるため、現フレームの描画を打ち切って
                    // Layout / Repaint を作り直す（IMGUI のレイアウト不整合を防ぐ）
                    GUIUtility.ExitGUI();
                }

                // レポートサマリーブロック
                if (lastMappingReport != null)
                    DrawMappingReportSummary(lastMappingReport);

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

        private void DrawMappingReportSummary(MappingReport report)
        {
            int exactCount    = report.groups.Count(g => g.outcome == MappingOutcome.ExactMatch);
            int inferredCount = report.groups.Count(g => g.outcome == MappingOutcome.Inferred);
            int needsConf     = report.NeedsConfirmationCount;
            int noCandidate   = report.NoCandidateCount;

            string summary =
                $"アバター内: レンダラー {report.rendererCount}個 / マテリアルスロット {report.slotCount}個\n" +
                $"確定 {exactCount} / 推定 {inferredCount} / 要確認 {needsConf} / 候補なし {noCandidate}";
            EditorGUILayout.HelpBox(summary, MessageType.Info);

            foreach (var w in report.warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);
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
                group.renderTargets = new List<MaterialRenderTarget>();

            // マッピングレポートから該当グループ結果を検索（参照等価）
            GroupMappingResult mappingResult = null;
            if (lastMappingReport != null)
                mappingResult = lastMappingReport.groups.FirstOrDefault(r => r.group == group);

            using (new EditorGUILayout.VerticalScope(groupBoxStyle))
            {
                // ヘッダー行（グループ名 + ステータスチップ + メッシュ数）
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(group.groupName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (mappingResult != null)
                    {
                        string chip;
                        switch (mappingResult.outcome)
                        {
                            case MappingOutcome.ExactMatch:        chip = "確定";    break;
                            case MappingOutcome.Inferred:          chip = "推定";    break;
                            case MappingOutcome.NeedsConfirmation: chip = "要確認";  break;
                            default:                               chip = "候補なし"; break;
                        }
                        EditorGUILayout.LabelField(chip, EditorStyles.miniLabel, GUILayout.Width(60));
                    }

                    string status = group.renderTargets.Count == 0
                        ? "未設定"
                        : $"{group.renderTargets.Count}メッシュ";
                    EditorGUILayout.LabelField(status, EditorStyles.miniLabel, GUILayout.Width(80));
                }

                // 診断メッセージ
                if (mappingResult != null && !string.IsNullOrEmpty(mappingResult.diagnosis))
                {
                    var severity = (mappingResult.outcome == MappingOutcome.ExactMatch ||
                                    mappingResult.outcome == MappingOutcome.Inferred)
                        ? MessageType.Info : MessageType.Warning;
                    EditorGUILayout.HelpBox(mappingResult.diagnosis, severity);

                    if (mappingResult.keptPrevious)
                    {
                        EditorGUILayout.HelpBox(
                            "※ 今回は決定できなかったため、以前の設定を残しています。",
                            MessageType.None);
                    }
                }

                if (group.renderTargets.Count == 0 && mappingResult == null)
                    EditorGUILayout.LabelField("(適用先レンダラーが未設定)", EditorStyles.miniLabel);

                // ターゲット一覧（削除はインデックス記録→ループ後に実行）
                int removeIndex = -1;
                for (int ti = 0; ti < group.renderTargets.Count; ti++)
                {
                    var target = group.renderTargets[ti];

                    // パスから Renderer を解決
                    Renderer currentRenderer = null;
                    if (target.IsAvatarRoot)
                    {
                        currentRenderer = avatarObject.GetComponent<Renderer>();
                    }
                    else if (!string.IsNullOrEmpty(target.rendererPath))
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
                                // ルート自身は空パスになるため専用の番兵値を入れる
                                // （空パスは「未設定」を意味し、適用先として捨てられてしまう）
                                target.rendererPath = renderer.transform == avatarObject.transform
                                    ? MaterialRenderTarget.AvatarRootPath
                                    : MaterialVariationDetector.GetRelativePath(
                                        avatarObject.transform, renderer.transform);
                                target.materialSlotIndex = 0;
                                SyncLegacyRenderTargetFields(group);
                            }
                        }

                        // マテリアルスロット選択
                        if (currentRenderer != null)
                        {
                            var mats = currentRenderer.sharedMaterials;
                            string[] slotNames = new string[mats.Length];
                            for (int s = 0; s < mats.Length; s++)
                                slotNames[s] = $"[{s}] {(mats[s] != null ? mats[s].name : "null")}";
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
                            removeIndex = ti;
                    }
                }

                if (removeIndex >= 0)
                {
                    group.renderTargets.RemoveAt(removeIndex);
                    SyncLegacyRenderTargetFields(group);
                    GUI.changed = true;
                }

                // 候補ピッカー（要確認かつ候補あり）
                if (mappingResult != null &&
                    mappingResult.outcome == MappingOutcome.NeedsConfirmation &&
                    mappingResult.candidates.Count > 0)
                {
                    DrawCandidatePicker(group, mappingResult);
                }

                // 全スロットフォールバック: 候補なしに加え、要確認でも「候補以外から選びたい」
                // ケースがあるため常に出す
                if (mappingResult != null &&
                    (mappingResult.outcome == MappingOutcome.NoCandidate ||
                     mappingResult.outcome == MappingOutcome.NeedsConfirmation))
                {
                    DrawAllSlotsFallback(group, mappingResult);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("＋ 対象レンダラー追加", GUILayout.Width(170)))
                    {
                        group.renderTargets.Add(new MaterialRenderTarget("", 0));
                        configDirty = true;
                    }
                }
            }
        }

        /// <summary>
        /// NeedsConfirmation グループ向け候補ピッカー。
        /// ボタンクリックは foreach 外でまとめて適用し、反復中のコレクション変更を防ぐ。
        /// </summary>
        private void DrawCandidatePicker(MaterialGroup group, GroupMappingResult result)
        {
            EditorGUILayout.LabelField("適用先の候補:", EditorStyles.boldLabel);

            const int maxCandidates = 8;
            var displayCandidates = result.candidates.Take(maxCandidates).ToList();

            MatchCandidate chosenCandidate = null;

            foreach (var candidate in displayCandidates)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(candidate.slot.Label, GUILayout.ExpandWidth(true));
                        EditorGUILayout.LabelField(
                            $"確度{candidate.score}", EditorStyles.miniLabel, GUILayout.Width(60));
                    }
                    EditorGUILayout.LabelField(candidate.ReasonText, EditorStyles.miniLabel);

                    if (GUILayout.Button("この適用先を使う", GUILayout.Width(130)))
                        chosenCandidate = candidate;
                }
            }

            if (result.candidates.Count > maxCandidates)
            {
                EditorGUILayout.LabelField(
                    $"（他 {result.candidates.Count - maxCandidates} 件は省略）",
                    EditorStyles.miniLabel);
            }

            // ループ完了後に適用（レイアウトミスマッチ防止）
            if (chosenCandidate != null)
            {
                group.renderTargets = new List<MaterialRenderTarget>
                {
                    new MaterialRenderTarget(chosenCandidate.slot.path, chosenCandidate.slot.slot)
                };
                SyncLegacyRenderTargetFields(group);
                configDirty = true;

                result.outcome   = MappingOutcome.Inferred;
                result.diagnosis = "手動で選択した適用先を使用します。";
                result.candidates.Clear();

                // 候補一覧が消えて制御数が変わるため、現フレームを打ち切る
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// 候補が無い / 候補以外から選びたいグループ向けの全スロット一覧フォールバック。
        /// 展開状態は expandedNoCandidateGroups で管理。上限 60 行。
        /// </summary>
        private void DrawAllSlotsFallback(MaterialGroup group, GroupMappingResult result)
        {
            bool expanded = expandedNoCandidateGroups.Contains(group);
            string btnLabel = expanded ? "▲ 全スロット一覧を閉じる" : "アバターの全スロットから選ぶ";

            // キャプチャしてすべての描画後に適用（Layout / Repaint / MouseUp で制御数を一致させる）
            bool toggleRequested = GUILayout.Button(btnLabel);

            if (expanded)
            {
                const int maxSlots = 60;
                var slots = MaterialSwitcherRendererMatcher.EnumerateSlots(avatarObject).ToList();

                EditorGUILayout.LabelField($"全スロット（{slots.Count}件）:", EditorStyles.boldLabel);

                var displaySlots = slots.Take(maxSlots).ToList();
                SlotRef chosenSlot = null;

                foreach (var slot in displaySlots)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            slot.Label, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("この適用先を使う", GUILayout.Width(130)))
                            chosenSlot = slot;
                    }
                }

                if (slots.Count > maxSlots)
                {
                    EditorGUILayout.LabelField(
                        $"（他 {slots.Count - maxSlots} 件は省略）", EditorStyles.miniLabel);
                }

                // ループ完了後に適用
                if (chosenSlot != null)
                {
                    group.renderTargets = new List<MaterialRenderTarget>
                    {
                        new MaterialRenderTarget(chosenSlot.path, chosenSlot.slot)
                    };
                    SyncLegacyRenderTargetFields(group);
                    configDirty = true;
                    expandedNoCandidateGroups.Remove(group);

                    result.outcome   = MappingOutcome.Inferred;
                    result.diagnosis = "手動で選択した適用先を使用します。";
                    result.candidates.Clear();

                    GUIUtility.ExitGUI();
                }
            }

            // 展開トグルは描画完了後に適用し、現フレームを打ち切る
            if (toggleRequested)
            {
                if (expanded)
                    expandedNoCandidateGroups.Remove(group);
                else
                    expandedNoCandidateGroups.Add(group);
                GUIUtility.ExitGUI();
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
                group.rendererPath      = group.renderTargets[0].rendererPath;
                group.materialSlotIndex = group.renderTargets[0].materialSlotIndex;
            }
            else
            {
                group.rendererPath = "";
            }
        }
    }
}
#endif
