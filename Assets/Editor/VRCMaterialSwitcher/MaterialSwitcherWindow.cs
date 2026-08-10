#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// VRC Material Switcher メイン EditorWindow。
    /// マテリアルカラーバリエーションの検出・Expression Menu セットアップを行う GUI。
    /// このファイルはフィールド定義・ライフサイクル・スキャン・アクション系のメソッドを担当する。
    /// 各セクションの描画は MaterialSwitcherWindow.Groups / Renderers / Setup の partial class に分割。
    /// </summary>
    public partial class MaterialSwitcherWindow : EditorWindow
    {
        // ---- 状態 ----
        private GameObject avatarObject;
        private VRCAvatarDescriptor avatarDescriptor;
        private SwitcherConfig config = new SwitcherConfig();
        private DefaultAsset scanFolder;
        private Vector2 scrollPosition;
        private string lastMessage = "";
        private MessageType lastMessageType = MessageType.None;

        // ---- 保存ディバウンス（Fix #3）----
        private bool   configDirty  = false;
        private double lastSaveTime = 0;

        // ---- グループ化できなかったマテリアル（Fix #7）----
        private List<string> ungroupedMaterials = new List<string>();

        // ---- セクション折りたたみ ----
        private bool foldoutAvatar      = true;
        private bool foldoutScan        = true;
        private bool foldoutGroups      = true;
        private bool foldoutMenuSettings = true;
        private bool foldoutRenderers   = true;
        private bool foldoutExisting    = true;

        // ---- 削除リクエスト（次の OnGUI 冒頭でまとめて処理し、反復中の変更によるレイアウト破壊を防ぐ）----
        private MaterialGroup    pendingRemoveGroup;
        private MaterialGroup    pendingVarGroup;
        private MaterialVariation pendingRemoveVariation;

        // ---- スタイル ----
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle boxStyle;
        private GUIStyle groupBoxStyle;
        private bool     stylesInitialized;

        // ---- 解像度ダウン UI の状態（DrawSetupSection / ApplyReduce で共有）----
        private int  reduceTargetSize = 1024;
        private bool reduceUseCrunch  = false;

        // VRChat の Uncompressed Size 上限（PC 約 500MB）
        private const long UncompressedSizeLimit = 500L * 1024 * 1024;

        // ========================================
        // ライフサイクル
        // ========================================

        private void OnEnable()
        {
            LoadConfig();
        }

        private void OnDisable()
        {
            // Fix #3: ウィンドウが閉じる前に必ずフラッシュする
            configDirty = false;
            SaveConfig();
        }

        /// <summary>Fix #3: フォーカスを失ったときに即フラッシュする。</summary>
        private void OnLostFocus()
        {
            if (configDirty)
            {
                configDirty  = false;
                lastSaveTime = EditorApplication.timeSinceStartup;
                SaveConfig();
            }
        }

        /// <summary>
        /// Fix #3: ディバウンスされた保存。変更後 0.7 秒が経過したら実際にディスクへ書き込む。
        /// EditorWindow.OnInspectorUpdate は約 10fps で呼ばれる。
        /// </summary>
        private void OnInspectorUpdate()
        {
            if (configDirty &&
                EditorApplication.timeSinceStartup - lastSaveTime > 0.7)
            {
                lastSaveTime = EditorApplication.timeSinceStartup;
                configDirty  = false;
                SaveConfig();
            }
        }

        // ========================================
        // 設定 I/O（ConfigIO への薄いラッパー）
        // ========================================

        /// <summary>設定をディスクに保存する。</summary>
        public void SaveConfig()
        {
            MaterialSwitcherConfigIO.SaveConfig(config);
        }

        /// <summary>
        /// ディスクから設定をロードし、scanFolder を復元する。
        /// Fix #2: GUID 復元・旧形式移行は ConfigIO 内で完結する。
        /// </summary>
        public void LoadConfig()
        {
            scanFolder = MaterialSwitcherConfigIO.LoadConfig(config);
        }

        // ========================================
        // ウィンドウ表示
        // ========================================

        [MenuItem("Tools/VRC Material Switcher")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaterialSwitcherWindow>("VRC Material Switcher");
            window.minSize = new Vector2(420, 500);
        }

        // ========================================
        // スタイル初期化
        // ========================================

        private void InitStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin   = new RectOffset(4, 4, 8, 4)
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin   = new RectOffset(4, 4, 4, 2)
            };

            boxStyle = new GUIStyle("helpbox")
            {
                padding = new RectOffset(8, 8, 8, 8),
                margin  = new RectOffset(4, 4, 4, 4)
            };

            groupBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 6, 6),
                margin  = new RectOffset(4, 4, 2, 2)
            };

            stylesInitialized = true;
        }

        // ========================================
        // メイン描画
        // ========================================

        private void OnGUI()
        {
            InitStyles();

            // 反復中のコレクション変更を避けるため、削除はフレーム冒頭でまとめて処理する
            ProcessPendingRemovals();

            EditorGUI.BeginChangeCheck();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawHeader();

            // メッセージ表示
            if (!string.IsNullOrEmpty(lastMessage))
            {
                EditorGUILayout.HelpBox(lastMessage, lastMessageType);
                if (GUILayout.Button("✕ メッセージを閉じる", GUILayout.Width(140)))
                    lastMessage = "";
                EditorGUILayout.Space(4);
            }

            DrawAvatarSection();
            DrawScanSection();
            DrawMaterialGroupsSection();    // Groups partial
            DrawMenuSettingsSection();
            DrawRendererSection();           // Renderers partial
            DrawSetupSection();             // Setup partial
            DrawExistingSetupSection();     // Setup partial

            EditorGUILayout.EndScrollView();

            // Fix #3: どの GUI 要素でも変更があれば dirty フラグを立てる（即時保存しない）
            if (EditorGUI.EndChangeCheck())
            {
                configDirty  = true;
                lastSaveTime = EditorApplication.timeSinceStartup; // ディバウンス起点をリセット
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

            EditorGUILayout.LabelField(
                "衣装のカラーバリエーションを Expression Menu から簡単切替",
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
            foldoutAvatar = EditorGUILayout.Foldout(
                foldoutAvatar, "▼ アバター設定", true, EditorStyles.foldoutHeader);
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
                            ShowMessage(
                                "選択されたオブジェクトに VRCAvatarDescriptor がありません。",
                                MessageType.Warning);
                            avatarObject = null;
                        }
                        else
                        {
                            // Fix #5: アバターが変わったらサイズキャッシュを無効化する
                            // （EnsureSizeCache 内でも自動判定されるが、ここで明示的に実施）
                            MaterialSwitcherTextureAnalyzer.InvalidateSizeCache();
                        }
                    }
                    else
                    {
                        avatarDescriptor = null;
                    }
                }

                if (avatarDescriptor != null)
                {
                    EditorGUILayout.HelpBox(
                        "✓ VRCAvatarDescriptor が見つかりました", MessageType.Info);
                }

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
            foldoutScan = EditorGUILayout.Foldout(
                foldoutScan, "▼ 衣装フォルダスキャン", true, EditorStyles.foldoutHeader);
            if (!foldoutScan) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                EditorGUILayout.LabelField(
                    "カラーバリエーションを含む衣装フォルダを選択してください",
                    EditorStyles.wordWrappedMiniLabel);

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
                            ungroupedMaterials?.Clear();
                            configDirty = true;
                            ShowMessage("マテリアルグループをクリアしました。", MessageType.Info);
                        }
                    }
                }
            }
        }

        // ========================================
        // メニュー設定
        // ========================================

        private void DrawMenuSettingsSection()
        {
            if (config.materialGroups.Count == 0) return;

            EditorGUILayout.Space(4);
            foldoutMenuSettings = EditorGUILayout.Foldout(
                foldoutMenuSettings, "▼ メニュー設定", true, EditorStyles.foldoutHeader);
            if (!foldoutMenuSettings) return;

            using (new EditorGUILayout.VerticalScope(boxStyle))
            {
                config.menuName = EditorGUILayout.TextField("メニュー名", config.menuName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    config.parameterSaved = EditorGUILayout.ToggleLeft(
                        "Saved（再ログイン後も保持）", config.parameterSaved, GUILayout.Width(200));
                    config.parameterSynced = EditorGUILayout.ToggleLeft(
                        "Synced（他の人にも同期）", config.parameterSynced, GUILayout.Width(200));
                }

                EditorGUILayout.HelpBox(
                    "Modular Avatar を使用した非破壊セットアップです。\n" +
                    "ビルド時にのみ適用され、元のアセットは変更されません。",
                    MessageType.Info);
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
                    avatarObject      = desc.gameObject;
                    avatarDescriptor  = desc;
                    MaterialSwitcherTextureAnalyzer.InvalidateSizeCache(); // Fix #5
                    ShowMessage($"アバター「{avatarObject.name}」を選択しました。", MessageType.Info);
                    return;
                }
            }

            // シーン上の全アバターを検索
            var avatars = FindObjectsByType<VRCAvatarDescriptor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (avatars.Length == 0)
            {
                ShowMessage(
                    "シーン上に VRCAvatarDescriptor が見つかりません。\nアバターをシーンに配置してください。",
                    MessageType.Warning);
                return;
            }

            if (avatars.Length == 1)
            {
                avatarObject     = avatars[0].gameObject;
                avatarDescriptor = avatars[0];
                MaterialSwitcherTextureAnalyzer.InvalidateSizeCache(); // Fix #5
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
                    avatarObject     = av.gameObject;
                    avatarDescriptor = av;
                    MaterialSwitcherTextureAnalyzer.InvalidateSizeCache(); // Fix #5
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

            // Fix #5: スキャン後にサイズキャッシュを無効化する（グループが変わった）
            InvalidateCaches();

            // Fix #7: グループ化できなかったマテリアルを保持して UI に表示する
            ungroupedMaterials = new List<string>(MaterialVariationDetector.LastUngroupedMaterials);

            // スキャン結果をディスクに保存する（ボタンクリックは EndChangeCheck で捕捉されないため）
            configDirty = true;

            if (config.materialGroups.Count > 0)
            {
                int mapped = 0;
                if (avatarObject != null)
                    mapped = MaterialVariationDetector.AutoMapRenderers(avatarObject, config.materialGroups);

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
                    $"{config.materialGroups.Sum(g => g.VariationCount)}バリエーションを検出しました。" +
                    mapMsg,
                    MessageType.Info);
            }
            else
            {
                ShowMessage(
                    "カラーバリエーションが検出されませんでした。\n" +
                    "フォルダ内に suffix 型（shirt_black.mat）や prefix 型（Black_Tex.mat）の" +
                    "マテリアルがあるか確認してください。",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// 空のマテリアルグループを手動で新規作成する。
        /// グループ名・バリエーション名・マテリアルは後から UI で編集できる。
        /// </summary>
        private void AddNewGroup()
        {
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
                groupName  = name,
                enabled    = true,
                foldout    = true,
                variations = new List<MaterialVariation>
                {
                    // セットアップには 2 バリエーション以上が必要なので初期値を 2 つ用意
                    new MaterialVariation("default", null, true),
                    new MaterialVariation("variant", null, false),
                }
            };

            config.materialGroups.Add(group);
            configDirty = true; // ボタンクリックは EndChangeCheck で捕捉されないため
            ShowMessage(
                $"グループ「{name}」を作成しました。\n" +
                "グループ名・バリエーションのマテリアル・レンダラーを設定してください。",
                MessageType.Info);
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
                    string mat = v.material != null ? v.material.name : "(null)";
                    preview += $"    {(v.isDefault ? "●" : "○")} {v.displayName}: {mat}" +
                               $"{(v.isDefault ? " [DEFAULT]" : "")}\n";
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
                // Fix #5: セットアップ実行後にサイズキャッシュを無効化する
                InvalidateCaches();
                ShowMessage(result.message, MessageType.Info);

                // Hierarchy 上でセットアップルートを展開・選択
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
            lastMessage     = message;
            lastMessageType = type;
        }

        /// <summary>
        /// パラメータコストキャッシュとサイズキャッシュを両方無効化する。
        /// スキャン・セットアップ実行・テクスチャ縮小の後に呼ぶ。
        /// </summary>
        private void InvalidateCaches()
        {
            MaterialSwitcherCostCalculator.InvalidateCostCache();
            MaterialSwitcherTextureAnalyzer.InvalidateSizeCache();
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
                (reduceUseCrunch ? "（Crunch 圧縮も有効化）" : "") + "。\n\n" +
                "・テクスチャのインポート設定を書き換えます（プロジェクト全体に影響）\n" +
                "・元に戻すには手動で解像度を上げ直してください\n\n続行しますか？",
                "実行", "キャンセル"))
                return;

            int n = MaterialSwitcherTextureAnalyzer.ReduceTextures(
                textures, reduceTargetSize, reduceUseCrunch);
            InvalidateCaches();
            ShowMessage(
                $"{n}枚のテクスチャを変更しました" +
                $"（最大{reduceTargetSize} / Crunch:{(reduceUseCrunch ? "有効" : "無効")}）。\n" +
                "容量表示を更新しました。まだ超過する場合はさらに下げる／" +
                "不要な衣装を外す／バリエーションを減らしてください。",
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
