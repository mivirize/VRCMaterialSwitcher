#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// VRC Material Switcher メインEditorWindow
    /// マテリアルカラーバリエーションの検出・Expression Menuセットアップを行うGUI
    /// </summary>
    public class MaterialSwitcherWindow : EditorWindow
    {
        // ---- 状態 ----
        private GameObject avatarObject;
        private VRCAvatarDescriptor avatarDescriptor;
        private SwitcherConfig config = new SwitcherConfig();
        private DefaultAsset scanFolder;
        private Vector2 scrollPosition;
        private string lastMessage = "";
        private MessageType lastMessageType = MessageType.None;

        // ---- セクション折りたたみ ----
        private bool foldoutAvatar = true;
        private bool foldoutScan = true;
        private bool foldoutGroups = true;
        private bool foldoutMenuSettings = true;
        private bool foldoutRenderers = true;
        private bool foldoutExisting = true;

        // ---- 削除リクエスト（次のOnGUI冒頭でまとめて処理し、反復中の変更によるレイアウト破壊を防ぐ） ----
        private MaterialGroup pendingRemoveGroup;
        private MaterialGroup pendingVarGroup;
        private MaterialVariation pendingRemoveVariation;

        // ---- スタイル ----
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle boxStyle;
        private GUIStyle groupBoxStyle;
        private bool stylesInitialized;
        private void OnEnable()
        {
            LoadConfig();
        }

        private void OnDisable()
        {
            SaveConfig();
        }

        private string GetConfigPath()
        {
            return "ProjectSettings/VRCMaterialSwitcherSettings.json";
        }

        public void SaveConfig()
        {
            try
            {
                string json = JsonUtility.ToJson(config, true);
                System.IO.File.WriteAllText(GetConfigPath(), json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VRC Material Switcher] 設定の保存に失敗しました: {e.Message}");
            }
        }

        public void LoadConfig()
        {
            try
            {
                string path = GetConfigPath();
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    JsonUtility.FromJsonOverwrite(json, config);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VRC Material Switcher] 設定のロードに失敗しました: {e.Message}");
            }
        }

        [MenuItem("Tools/VRC Material Switcher")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaterialSwitcherWindow>("VRC Material Switcher");
            window.minSize = new Vector2(420, 500);
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(4, 4, 8, 4)
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(4, 4, 4, 2)
            };

            boxStyle = new GUIStyle("helpbox")
            {
                padding = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(4, 4, 4, 4)
            };

            groupBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(4, 4, 2, 2)
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // 反復中のコレクション変更を避けるため、削除はフレーム冒頭でまとめて処理する
            ProcessPendingRemovals();

            EditorGUI.BeginChangeCheck();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // ヘッダー
            DrawHeader();

            // メッセージ表示
            if (!string.IsNullOrEmpty(lastMessage))
            {
                EditorGUILayout.HelpBox(lastMessage, lastMessageType);
                if (GUILayout.Button("✕ メッセージを閉じる", GUILayout.Width(140)))
                {
                    lastMessage = "";
                }
                EditorGUILayout.Space(4);
            }

            // アバター設定セクション
            DrawAvatarSection();

            // スキャンセクション
            DrawScanSection();

            // マテリアルグループセクション
            DrawMaterialGroupsSection();

            // メニュー設定セクション
            DrawMenuSettingsSection();

            // レンダラーマッピングセクション
            DrawRendererSection();

            // セットアップ実行
            DrawSetupSection();

            // 既存セットアップ管理
            DrawExistingSetupSection();

            EditorGUILayout.EndScrollView();

            // どのGUI要素でも変更があれば即座にディスクへ保存する
            if (EditorGUI.EndChangeCheck())
            {
                SaveConfig();
            }
        }

        /// <summary>
        /// 削除リクエストをフレーム冒頭でまとめて処理する。
        /// OnGUIの反復中にコレクションを変更するとレイアウトが破壊されるため遅延実行する。
        /// </summary>
        private void ProcessPendingRemovals()
        {
            if (pendingRemoveGroup != null)
            {
                string removedName = pendingRemoveGroup.groupName;
                config.materialGroups.Remove(pendingRemoveGroup);
                pendingRemoveGroup = null;
                ShowMessage($"グループ「{removedName}」を削除しました。", MessageType.Info);
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
            }
        }

        // ========================================
        // ヘッダー
        // ========================================
        private void DrawHeader()
        {
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("🎨 VRC Material Switcher", headerStyle);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.LabelField("衣装のカラーバリエーションをExpression Menuから簡単切替",
                EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(4);
            DrawHorizontalLine();
            EditorGUILayout.Space(4);
        }

        // ========================================
        // アバター設定
        // ========================================
        private void DrawAvatarSection()
        {
            foldoutAvatar = EditorGUILayout.Foldout(foldoutAvatar, "▼ アバター設定", true, EditorStyles.foldoutHeader);
            if (!foldoutAvatar) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                EditorGUI.BeginChangeCheck();
                avatarObject = (GameObject)EditorGUILayout.ObjectField(
                    "アバター", avatarObject, typeof(GameObject), true);

                if (EditorGUI.EndChangeCheck())
                {
                    if (avatarObject != null)
                    {
                        avatarDescriptor = avatarObject.GetComponent<VRCAvatarDescriptor>();
                        if (avatarDescriptor == null)
                        {
                            ShowMessage("選択されたオブジェクトにVRCAvatarDescriptorがありません。", MessageType.Warning);
                            avatarObject = null;
                        }
                    }
                    else
                    {
                        avatarDescriptor = null;
                    }
                }

                if (avatarDescriptor != null)
                {
                    EditorGUILayout.HelpBox("✓ VRCAvatarDescriptorが見つかりました", MessageType.Info);
                }

                // シーン上のアバター自動検出ボタン
                if (GUILayout.Button("シーンからアバターを自動検出"))
                {
                    AutoDetectAvatar();
                }
            }
        }

        // ========================================
        // スキャンセクション
        // ========================================
        private void DrawScanSection()
        {
            EditorGUILayout.Space(4);
            foldoutScan = EditorGUILayout.Foldout(foldoutScan, "▼ 衣装フォルダスキャン", true, EditorStyles.foldoutHeader);
            if (!foldoutScan) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                EditorGUILayout.LabelField("カラーバリエーションを含む衣装フォルダを選択してください", EditorStyles.wordWrappedMiniLabel);

                scanFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    "衣装フォルダ", scanFolder, typeof(DefaultAsset), false);

                if (scanFolder != null)
                {
                    string folderPath = AssetDatabase.GetAssetPath(scanFolder);
                    config.scanFolderPath = folderPath;
                    EditorGUILayout.LabelField("パス: " + folderPath, EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = scanFolder != null;
                    if (GUILayout.Button("🔍 スキャン実行", GUILayout.Height(28)))
                    {
                        RunScan();
                    }
                    GUI.enabled = true;

                    if (config.materialGroups.Count > 0)
                    {
                        if (GUILayout.Button("クリア", GUILayout.Width(60), GUILayout.Height(28)))
                        {
                            config.materialGroups.Clear();
                            ShowMessage("マテリアルグループをクリアしました。", MessageType.Info);
                        }
                    }
                }
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
                        }
                        if (GUILayout.Button("全て無効", GUILayout.Width(72), GUILayout.Height(24)))
                        {
                            foreach (var g in config.materialGroups) g.enabled = false;
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
                    return;
                }

                EditorGUILayout.Space(4);

                // 反復中はコレクションを変更しない（削除はpendingに積んで冒頭で処理）
                foreach (var group in config.materialGroups)
                {
                    DrawMaterialGroupItem(group);
                }
            }
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
                            SaveConfig();
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
                            SaveConfig();
                            Repaint();
                        }

                        // デフォルト設定ボタン
                        GUI.enabled = !variation.isDefault;
                        if (GUILayout.Button("★", GUILayout.Width(24), GUILayout.Height(18)))
                        {
                            foreach (var v in group.variations) v.isDefault = false;
                            variation.isDefault = true;
                            SaveConfig();
                            Repaint();
                        }
                        GUI.enabled = true;

                        // バリエーション削除ボタン（pendingに積んで冒頭で処理）
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
                }

                EditorGUI.indentLevel--;
            }
        }

        // ========================================
        // メニュー設定
        // ========================================
        private void DrawMenuSettingsSection()
        {
            if (config.materialGroups.Count == 0) return;

            EditorGUILayout.Space(4);
            foldoutMenuSettings = EditorGUILayout.Foldout(foldoutMenuSettings,
                "▼ メニュー設定", true, EditorStyles.foldoutHeader);
            if (!foldoutMenuSettings) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                config.menuName = EditorGUILayout.TextField("メニュー名", config.menuName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    config.parameterSaved = EditorGUILayout.ToggleLeft("Saved（再ログイン後も保持）",
                        config.parameterSaved, GUILayout.Width(200));
                    config.parameterSynced = EditorGUILayout.ToggleLeft("Synced（他の人にも同期）",
                        config.parameterSynced, GUILayout.Width(200));
                }

                EditorGUILayout.HelpBox(
                    "Modular Avatarを使用した非破壊セットアップです。\n" +
                    "ビルド時にのみ適用され、元のアセットは変更されません。",
                    MessageType.Info);
            }
        }

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
                EditorGUILayout.LabelField("各グループが適用されるレンダラーとスロットを指定", EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("自動マッピング"))
                {
                    int mapped = MaterialVariationDetector.AutoMapRenderers(avatarObject, config.materialGroups);
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
        /// 1グループのレンダラーターゲット（複数可）を表示・編集する
        /// </summary>
        private void DrawGroupRenderTargets(MaterialGroup group)
        {
            if (group.renderTargets == null)
                group.renderTargets = new System.Collections.Generic.List<MaterialRenderTarget>();

            // 旧形式（単一rendererPath）からの移行
            if (group.renderTargets.Count == 0 && !string.IsNullOrEmpty(group.rendererPath))
            {
                group.renderTargets.Add(new MaterialRenderTarget(group.rendererPath, group.materialSlotIndex));
            }

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

                    // パスからRendererを解決
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
                            target.rendererPath = MaterialVariationDetector.GetRelativePath(
                                avatarObject.transform, renderer.transform);
                            target.materialSlotIndex = 0;
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
                            int slot = Mathf.Clamp(target.materialSlotIndex, 0, Mathf.Max(0, mats.Length - 1));
                            target.materialSlotIndex = EditorGUILayout.Popup(
                                slot, slotNames, GUILayout.Width(150));
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"Slot [{target.materialSlotIndex}]", GUILayout.Width(150));
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
                    GUI.changed = true;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("＋ 対象レンダラー追加", GUILayout.Width(170)))
                    {
                        group.renderTargets.Add(new MaterialRenderTarget("", 0));
                    }
                }

                // 後方互換: 先頭ターゲットを主フィールドへ同期
                if (group.renderTargets.Count > 0)
                {
                    group.rendererPath = group.renderTargets[0].rendererPath;
                    group.materialSlotIndex = group.renderTargets[0].materialSlotIndex;
                }
                else
                {
                    group.rendererPath = "";
                }
            }
        }

        // ========================================
        // セットアップ実行
        // ========================================
        private void DrawSetupSection()
        {
            var enabledGroups = config.materialGroups.Where(g => g.enabled && g.VariationCount >= 2).ToList();
            if (enabledGroups.Count == 0) return;

            EditorGUILayout.Space(8);
            DrawHorizontalLine();
            EditorGUILayout.Space(4);

            // セットアップ情報サマリー
            int totalVariations = enabledGroups.Sum(g => g.EnabledVariationCount);
            EditorGUILayout.LabelField(
                $"セットアップ: {enabledGroups.Count}グループ / {totalVariations}バリエーション",
                subHeaderStyle);

            // ---- パラメータコスト計算（VRChat同期パラメータ上限 = 合計256bit）----
            //   NDMFのParameterInfoで「他ギミック・MA生成を含むビルド後の実コスト」を集計し、本ツール分を加算する。
            int maxBits = VRCExpressionParameters.MAX_PARAMETER_COST;
            int otherBits = GetOtherParameterBitsCached();
            bool costOk = otherBits >= 0;
            int toolBits = EstimateSwitcherBits(enabledGroups);
            int totalBits = (costOk ? otherBits : 0) + toolBits;
            int remainingBits = maxBits - totalBits;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("パラメータコスト（VRChat同期上限）", EditorStyles.boldLabel);
                    if (GUILayout.Button("↻ 再計算", GUILayout.Width(70)))
                        InvalidateCostCache();
                }

                if (avatarObject == null)
                    EditorGUILayout.LabelField("アバター未設定のため集計できません。", EditorStyles.miniLabel);
                else if (!costOk)
                    EditorGUILayout.LabelField("既存パラメータの集計に失敗しました（Consoleを確認）。", EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(
                        $"アバター既存（他ギミック・MA生成を含む）: {otherBits} bit", EditorStyles.miniLabel);

                EditorGUILayout.LabelField(
                    config.parameterSynced
                        ? $"本ツール（{enabledGroups.Count}グループ）: {toolBits} bit"
                        : "本ツール: 0 bit（Syncedオフ）",
                    EditorStyles.miniLabel);

                // 使用率バー
                Rect barRect = EditorGUILayout.GetControlRect(false, 18);
                float frac = maxBits > 0 ? Mathf.Clamp01(totalBits / (float)maxBits) : 0f;
                EditorGUI.ProgressBar(barRect, frac, $"{totalBits} / {maxBits} bit");

                if (costOk && config.parameterSynced)
                {
                    if (remainingBits >= 0)
                        EditorGUILayout.LabelField(
                            $"残り: {remainingBits} bit → あと約 {remainingBits / 8} グループ追加可能（3択以上=8bit / 2択なら最大 {remainingBits} 個）",
                            EditorStyles.miniLabel);
                    else
                        EditorGUILayout.LabelField(
                            $"超過: {-remainingBits} bit オーバー → 3択以上グループを約 {(-remainingBits + 7) / 8} 個減らす／Syncedオフで解消",
                            EditorStyles.miniLabel);
                }
                else if (!config.parameterSynced)
                    EditorGUILayout.LabelField("Syncedオフのため同期コスト0bit（他者に色は同期されません）", EditorStyles.miniLabel);

                EditorGUILayout.LabelField(
                    "※他ギミックを編集した場合は「↻ 再計算」で更新してください",
                    EditorStyles.miniLabel);
            }

            // 上限超過・逼迫の警告
            if (costOk && config.parameterSynced && totalBits > maxBits)
            {
                EditorGUILayout.HelpBox(
                    $"同期パラメータが上限を {totalBits - maxBits} bit 超過しています（合計 {totalBits} / {maxBits} bit）。\n" +
                    "このままではアップロードできません（VRCExpressionParameters has too many parameters）。\n" +
                    "・不要なグループを無効化する（マテリアルグループ欄のチェックを外す）\n" +
                    "・メニュー設定の「Synced」をオフにする（コスト0bit）",
                    MessageType.Error);
            }
            else if (costOk && config.parameterSynced && totalBits > maxBits * 0.9f)
            {
                EditorGUILayout.HelpBox(
                    $"上限が近づいています（残り {remainingBits} bit）。追加は慎重に。",
                    MessageType.Warning);
            }

            // ---- 容量（Uncompressed Size）----
            EnsureSizeCache(enabledGroups);
            const float MB = 1024f * 1024f;
            float swMB = cachedSwitcherTexBytes / MB;
            float avMB = cachedAvatarTexBytes / MB;
            float texMB = cachedTotalTexBytes / MB;     // 切替∪アバター（重複排除）
            float meshMB = cachedMeshBytes / MB;
            float grandMB = texMB + meshMB;             // テクスチャ＋メッシュの概算合計
            float limMB = UncompressedSizeLimit / MB;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("容量（Uncompressed Size 目安 / PC上限 約500MB）", EditorStyles.boldLabel);

                EditorGUILayout.LabelField($"　切替同梱テクスチャ: {swMB:F0} MB", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"　アバター本体・装着物テクスチャ: {avMB:F0} MB", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"　メッシュ: {meshMB:F0} MB", EditorStyles.miniLabel);

                Rect sizeBar = EditorGUILayout.GetControlRect(false, 18);
                float sfrac = limMB > 0 ? Mathf.Clamp01(grandMB / limMB) : 0f;
                EditorGUI.ProgressBar(sizeBar, sfrac, $"合計概算 {grandMB:F0} / {limMB:F0} MB");

                EditorGUILayout.LabelField(
                    "※アニメ/シェーダ/VRCFury等は未計上。実値はSDK Build画面のUncompressed Sizeで確認",
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
                    if (GUILayout.Toggle(reduceTargetSize == 2048, "2048", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                        reduceTargetSize = 2048;
                    if (GUILayout.Toggle(reduceTargetSize == 1024, "1024", EditorStyles.miniButtonMid, GUILayout.Width(50)))
                        reduceTargetSize = 1024;
                    if (GUILayout.Toggle(reduceTargetSize == 512, "512", EditorStyles.miniButtonMid, GUILayout.Width(50)))
                        reduceTargetSize = 512;
                    if (GUILayout.Toggle(reduceTargetSize == 256, "256", EditorStyles.miniButtonRight, GUILayout.Width(50)))
                        reduceTargetSize = 256;
                    reduceUseCrunch = EditorGUILayout.ToggleLeft("Crunch圧縮", reduceUseCrunch, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"↓ 切替色のみ {reduceTargetSize} 以下に"))
                        ApplyReduce(CollectSwitcherTextures(enabledGroups), "切替色のバリエーション");

                    if (GUILayout.Button($"↓ アバター全体を {reduceTargetSize} 以下に"))
                    {
                        var all = CollectAvatarTextures();
                        all.UnionWith(CollectSwitcherTextures(enabledGroups));
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
                        "VRC Material Switcherの全セットアップを削除しますか？\nこの操作はUndoで元に戻せます。",
                        "全削除", "キャンセル"))
                    {
                        int count = MaterialSwitcherSetup.RemoveSetup(avatarObject);
                        ShowMessage($"{count}件のセットアップを削除しました。", MessageType.Info);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
        }

        // ========================================
        // アクション
        // ========================================

        private void AutoDetectAvatar()
        {
            // まず選択中のオブジェクトを確認
            if (Selection.activeGameObject != null)
            {
                var desc = Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>();
                if (desc == null)
                    desc = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();

                if (desc != null)
                {
                    avatarObject = desc.gameObject;
                    avatarDescriptor = desc;
                    ShowMessage($"アバター「{avatarObject.name}」を選択しました。", MessageType.Info);
                    return;
                }
            }

            // シーン上の全アバターを検索
            var avatars = FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (avatars.Length == 0)
            {
                ShowMessage("シーン上にVRCAvatarDescriptorが見つかりません。\nアバターをシーンに配置してください。", MessageType.Warning);
                return;
            }

            if (avatars.Length == 1)
            {
                avatarObject = avatars[0].gameObject;
                avatarDescriptor = avatars[0];
                ShowMessage($"アバター「{avatarObject.name}」を自動検出しました。", MessageType.Info);
                return;
            }

            // 複数ある場合はメニュー表示
            var menu = new GenericMenu();
            foreach (var avatar in avatars)
            {
                var av = avatar; // クロージャ用
                menu.AddItem(new GUIContent(av.gameObject.name), false, () =>
                {
                    avatarObject = av.gameObject;
                    avatarDescriptor = av;
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void RunScan()
        {
            if (scanFolder == null) return;

            string folderPath = AssetDatabase.GetAssetPath(scanFolder);
            config.materialGroups = MaterialVariationDetector.DetectVariations(folderPath);

            if (config.materialGroups.Count > 0)
            {
                // アバターが設定済みなら自動マッピングも実行
                int mapped = 0;
                if (avatarObject != null)
                {
                    mapped = MaterialVariationDetector.AutoMapRenderers(avatarObject, config.materialGroups);
                }

                string mapMsg;
                if (avatarObject == null)
                {
                    mapMsg = "\nアバターを設定すると自動マッピングが実行されます。";
                }
                else if (mapped >= config.materialGroups.Count)
                {
                    mapMsg = $"\nレンダラー自動マッピング: 全{config.materialGroups.Count}グループ成功。";
                }
                else
                {
                    mapMsg = $"\nレンダラー自動マッピング: {mapped}/{config.materialGroups.Count}グループ成功。" +
                             "\n未マッピングのグループは「レンダラーマッピング」で手動設定してください。";
                }

                ShowMessage(
                    $"✓ {config.materialGroups.Count}グループ、" +
                    $"{config.materialGroups.Sum(g => g.VariationCount)}バリエーションを検出しました。" + mapMsg,
                    MessageType.Info);
            }
            else
            {
                ShowMessage(
                    "カラーバリエーションが検出されませんでした。\n" +
                    "フォルダ内にsuffix型（shirt_black.mat）やprefix型（Black_Tex.mat）のマテリアルがあるか確認してください。",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// 空のマテリアルグループを手動で新規作成する。
        /// グループ名・バリエーション名・マテリアルは後からUIで編集できる。
        /// </summary>
        private void AddNewGroup()
        {
            // 既存と重複しないユニークなグループ名を生成
            string baseName = "新規グループ";
            string name = baseName;
            int suffix = 1;
            while (config.materialGroups.Any(g => g.groupName == name))
            {
                suffix++;
                name = $"{baseName}{suffix}";
            }

            var group = new MaterialGroup
            {
                groupName = name,
                enabled = true,
                foldout = true,
                variations = new List<MaterialVariation>
                {
                    // セットアップには2バリエーション以上が必要なので初期値を2つ用意
                    new MaterialVariation("default", null, true),
                    new MaterialVariation("variant", null, false),
                }
            };

            config.materialGroups.Add(group);
            ShowMessage(
                $"グループ「{name}」を作成しました。\n" +
                "グループ名・バリエーションのマテリアル・レンダラーを設定してください。",
                MessageType.Info);
            GUI.changed = true;
        }

        private void PreviewSetup(List<MaterialGroup> groups)
        {
            string preview = "=== セットアッププレビュー ===\n\n";
            preview += $"メニュー名: {config.menuName}\n";
            preview += $"Saved: {config.parameterSaved} / Synced: {config.parameterSynced}\n\n";

            foreach (var group in groups)
            {
                preview += $"[{group.groupName}] ({group.EnabledVariationCount}バリエーション)\n";
                var targets = group.GetEffectiveTargets();
                if (targets.Count == 0)
                {
                    preview += "  適用先: (未設定)\n";
                }
                else
                {
                    preview += $"  適用先: {targets.Count}メッシュ\n";
                    foreach (var t in targets)
                    {
                        string rp = string.IsNullOrEmpty(t.rendererPath) ? "(未設定)" : t.rendererPath;
                        preview += $"    - {rp} [slot {t.materialSlotIndex}]\n";
                    }
                }

                foreach (var v in group.variations)
                {
                    if (!v.includeInMenu) continue;
                    string def = v.isDefault ? " [DEFAULT]" : "";
                    string mat = v.material != null ? v.material.name : "(null)";
                    preview += $"    {(v.isDefault ? "●" : "○")} {v.displayName}: {mat}{def}\n";
                }
                preview += "\n";
            }

            EditorUtility.DisplayDialog("セットアッププレビュー", preview, "OK");
        }

        private void ExecuteSetup()
        {
            if (avatarObject == null)
            {
                ShowMessage("アバターが選択されていません。", MessageType.Error);
                return;
            }

            var result = MaterialSwitcherSetup.SetupWithModularAvatar(avatarObject, config);

            if (result.success)
            {
                ShowMessage(result.message, MessageType.Info);
                // Hierarchy上でセットアップルートを展開・選択
                var setupRoot = avatarObject.transform.Find("VRCMaterialSwitcherRoot");
                if (setupRoot != null)
                {
                    Selection.activeGameObject = setupRoot.gameObject;
                    EditorGUIUtility.PingObject(setupRoot.gameObject);
                }
            }
            else
            {
                ShowMessage(result.message, MessageType.Error);
            }
        }

        // ========================================
        // ユーティリティ
        // ========================================

        private void ShowMessage(string message, MessageType type)
        {
            lastMessage = message;
            lastMessageType = type;
        }

        // ---- コスト計算のキャッシュ（毎フレーム全走査は重いため） ----
        private int cachedOtherBits = -2;         // -2:未計算, -1:取得失敗（パラメータbit）
        private long cachedSwitcherTexBytes = -1;  // 本ツール同梱テクスチャ容量
        private long cachedAvatarTexBytes = -1;    // アバター描画テクスチャ（本体+装着物）
        private long cachedTotalTexBytes = -1;     // テクスチャ union（切替∪アバター）
        private long cachedMeshBytes = -1;         // メッシュ容量
        private GameObject costCacheAvatar;

        // VRChatのUncompressed Size上限（PC約500MB）
        private const long UncompressedSizeLimit = 500L * 1024 * 1024;

        private void InvalidateCostCache()
        {
            cachedOtherBits = -2;
            cachedSwitcherTexBytes = -1;
            cachedAvatarTexBytes = -1;
            cachedTotalTexBytes = -1;
            cachedMeshBytes = -1;
        }

        /// <summary>
        /// アバターに最終的に生成される「本ツール以外」の同期パラメータビットを集計する（キャッシュ付き）。
        /// NDMFのParameterInfoを使うため、Modular Avatarがビルド時に生成する分（他の衣装ギミック等）も
        /// 含めた実コストを取得できる（アセット定義分のみのCalcTotalCostでは大幅に過小になる）。
        /// </summary>
        private int GetOtherParameterBitsCached()
        {
            if (avatarObject != costCacheAvatar)
            {
                costCacheAvatar = avatarObject;
                cachedOtherBits = -2;
            }
            if (cachedOtherBits == -2)
                cachedOtherBits = ComputeOtherParameterBits();
            return cachedOtherBits;
        }

        private int ComputeOtherParameterBits()
        {
            if (avatarObject == null) return 0;
            try
            {
                int bits = 0;
                foreach (var p in nadena.dev.ndmf.ParameterInfo.ForUI.GetParametersForObject(avatarObject))
                {
                    // Expression（Animator名前空間）の同期パラメータのみが256bit予算に計上される
                    if (p.Namespace != nadena.dev.ndmf.ParameterNamespace.Animator) continue;

                    // 本ツール自身のパラメータ（MatSwitch_*）は別途見積るため除外（二重計上防止）
                    string nm = p.EffectiveName ?? p.OriginalName;
                    if (!string.IsNullOrEmpty(nm) && nm.StartsWith("MatSwitch_")) continue;

                    bits += p.BitUsage; // Bool=1, Int/Float=8, 非同期=0
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
        /// 本ツールが追加する同期パラメータビットを見積る。
        /// Modular Avatarは値が0/1のみ（2択）ならBool=1bit、3択以上はInt=8bitで生成する。
        /// </summary>
        private int EstimateSwitcherBits(List<MaterialGroup> enabledGroups)
        {
            if (!config.parameterSynced) return 0;
            int bits = 0;
            foreach (var g in enabledGroups)
            {
                int m = g.EnabledVariationCount;
                if (m <= 0) continue;
                bits += (m <= 2) ? 1 : 8;
            }
            return bits;
        }

        // ================= 容量（Uncompressed Size）関連 =================

        // 解像度ダウンUIの状態
        private int reduceTargetSize = 1024;
        private bool reduceUseCrunch = false;

        /// <summary>マテリアルが参照する全テクスチャを集める。</summary>
        private static void CollectTextures(Material m, HashSet<Texture> set)
        {
            if (m == null || m.shader == null) return;
            int count = ShaderUtil.GetPropertyCount(m.shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string prop = ShaderUtil.GetPropertyName(m.shader, i);
                var tex = m.GetTexture(prop);
                if (tex != null) set.Add(tex);
            }
        }

        /// <summary>有効グループのバリエーションが同梱する全テクスチャを集める。</summary>
        private HashSet<Texture> CollectSwitcherTextures(List<MaterialGroup> enabledGroups)
        {
            var set = new HashSet<Texture>();
            foreach (var g in enabledGroups)
                foreach (var v in g.variations)
                    if (v.includeInMenu && v.material != null)
                        CollectTextures(v.material, set);
            return set;
        }

        private static System.Reflection.MethodInfo _texSizeMethod;
        private static bool _texSizeMethodResolved;

        /// <summary>テクスチャのGPUメモリサイズ（VRChatのUncompressed Size計算に一致）をバイトで返す。</summary>
        private static long TextureStorageBytes(Texture t)
        {
            if (t == null) return 0;
            try
            {
                if (!_texSizeMethodResolved)
                {
                    _texSizeMethodResolved = true;
                    var util = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TextureUtil");
                    _texSizeMethod = util?.GetMethod("GetStorageMemorySizeLong",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                }
                if (_texSizeMethod != null)
                {
                    var r = _texSizeMethod.Invoke(null, new object[] { t });
                    if (r is long l) return l;
                }
            }
            catch { }
            return UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
        }

        private static long SumTextureBytes(HashSet<Texture> texs)
        {
            long total = 0;
            foreach (var t in texs) total += TextureStorageBytes(t);
            return total;
        }

        /// <summary>アバターの全レンダラーが参照するテクスチャを集める（本体・装着物すべて）。</summary>
        private HashSet<Texture> CollectAvatarTextures()
        {
            var set = new HashSet<Texture>();
            if (avatarObject == null) return set;
            foreach (var r in avatarObject.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    CollectTextures(m, set);
            return set;
        }

        /// <summary>容量キャッシュを必要に応じて再計算する（テクスチャ＋メッシュ）。</summary>
        private void EnsureSizeCache(List<MaterialGroup> enabledGroups)
        {
            if (cachedTotalTexBytes >= 0) return;

            var switcherTex = CollectSwitcherTextures(enabledGroups);
            cachedSwitcherTexBytes = SumTextureBytes(switcherTex);

            var avatarTex = CollectAvatarTextures();
            cachedAvatarTexBytes = SumTextureBytes(avatarTex);

            var union = new HashSet<Texture>(switcherTex);
            union.UnionWith(avatarTex);
            cachedTotalTexBytes = SumTextureBytes(union);

            // メッシュ容量
            long meshBytes = 0;
            if (avatarObject != null)
            {
                var meshes = new HashSet<Mesh>();
                foreach (var r in avatarObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) meshes.Add(smr.sharedMesh);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
                }
                foreach (var m in meshes) meshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);
            }
            cachedMeshBytes = meshBytes;
        }

        /// <summary>
        /// 指定テクスチャ集合の最大解像度を下げる（＋任意でCrunch圧縮）。
        /// import設定を書き換えて再インポートする。戻り値は変更したテクスチャ数。
        /// </summary>
        private int ReduceTextures(IEnumerable<Texture> textures, int maxSize, bool useCrunch)
        {
            var list = new HashSet<Texture>(textures).ToList();
            int changed = 0;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    EditorUtility.DisplayProgressBar("テクスチャ解像度を調整中",
                        $"{i + 1}/{list.Count}: {t.name}", (float)(i + 1) / Mathf.Max(1, list.Count));

                    string path = AssetDatabase.GetAssetPath(t);
                    if (string.IsNullOrEmpty(path)) continue;
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
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

        /// <summary>確認ダイアログ付きでテクスチャ縮小を実行し、容量表示を更新する。</summary>
        private void ApplyReduce(HashSet<Texture> textures, string scopeLabel)
        {
            if (textures == null || textures.Count == 0)
            {
                ShowMessage("対象テクスチャがありません。", MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("テクスチャ解像度の変更",
                $"対象: {scopeLabel}（{textures.Count}枚）\n" +
                $"最大解像度を {reduceTargetSize} 以下に変更します" +
                (reduceUseCrunch ? "（Crunch圧縮も有効化）" : "") + "。\n\n" +
                "・テクスチャのインポート設定を書き換えます（プロジェクト全体に影響）\n" +
                "・元に戻すには手動で解像度を上げ直してください\n\n続行しますか？",
                "実行", "キャンセル"))
                return;

            int n = ReduceTextures(textures, reduceTargetSize, reduceUseCrunch);
            InvalidateCostCache();
            ShowMessage(
                $"{n}枚のテクスチャを変更しました（最大{reduceTargetSize} / Crunch:{(reduceUseCrunch ? "有効" : "無効")}）。\n" +
                "容量表示を更新しました。まだ超過する場合はさらに下げる／不要な衣装を外す／バリエーションを減らしてください。",
                MessageType.Info);
        }

        private static void DrawHorizontalLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
    }
}
#endif
