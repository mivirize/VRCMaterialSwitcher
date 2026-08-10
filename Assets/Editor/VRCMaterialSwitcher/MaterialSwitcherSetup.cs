#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace VRCMaterialSwitcher
{
    /// <summary>
    /// Modular Avatarコンポーネントを使用して、マテリアル切替のExpression Menuをセットアップするクラス
    /// </summary>
    public static class MaterialSwitcherSetup
    {
        // 🌲 特殊文字（角括弧）によるパスパーサーのバグを防ぐため、オブジェクト名を英数字のみに変更
        private const string SETUP_ROOT_NAME = "VRCMaterialSwitcherRoot";
        private const string PARAMETER_PREFIX = "MatSwitch_";
        private const string GROUP_OBJECT_PREFIX = "Group_";

        /// <summary>
        /// MAコンポーネントを使用してマテリアル切替メニューをセットアップする（100%非破壊モード）
        /// </summary>
        /// <param name="avatarRoot">アバターのルートGameObject（VRCAvatarDescriptorを持つ）</param>
        /// <param name="config">設定データ</param>
        /// <returns>セットアップ結果</returns>
        public static SetupResult SetupWithModularAvatar(GameObject avatarRoot, SwitcherConfig config)
        {
            if (avatarRoot == null)
                return SetupResult.Failure("アバターが選択されていません。");

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return SetupResult.Failure("選択されたオブジェクトにVRCAvatarDescriptorがありません。");

            // メニューに実際に出るバリエーション（includeInMenu）が2つ以上あるグループのみ対象
            var enabledGroups = config.materialGroups.Where(g => g.enabled && g.EnabledVariationCount >= 2).ToList();
            if (enabledGroups.Count == 0)
                return SetupResult.Failure("有効なマテリアルグループがありません。\n各グループで2つ以上のバリエーションにチェックが入っているか確認してください。");

            // 事前検証: マテリアル未設定のバリエーションを検出
            foreach (var group in enabledGroups)
            {
                var missing = group.variations.Where(v => v.includeInMenu && v.material == null).ToList();
                if (missing.Count > 0)
                    return SetupResult.Failure(
                        $"グループ「{group.groupName}」にマテリアル未設定のバリエーションがあります: " +
                        string.Join(", ", missing.Select(v => v.displayName)));
            }

            // VRCAvatarDescriptorのExpressions設定チェックと自動アサイン
            if (descriptor.customExpressions == false)
            {
                Undo.RecordObject(descriptor, "Enable Custom Expressions");
                descriptor.customExpressions = true;
            }

            if (descriptor.expressionsMenu == null)
            {
                string dir = "Assets/VRCMaterialSwitcher_Generated";
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    AssetDatabase.CreateFolder("Assets", "VRCMaterialSwitcher_Generated");
                }
                string path = $"{dir}/{avatarRoot.name}_Menu.asset";
                VRCExpressionsMenu newMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                AssetDatabase.CreateAsset(newMenu, path);
                Undo.RecordObject(descriptor, "Assign Expressions Menu");
                descriptor.expressionsMenu = newMenu;
                Debug.Log($"[VRC Material Switcher] 新しい ExpressionsMenu アセットを作成しました: {path}");
            }

            if (descriptor.expressionParameters == null)
            {
                string dir = "Assets/VRCMaterialSwitcher_Generated";
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    AssetDatabase.CreateFolder("Assets", "VRCMaterialSwitcher_Generated");
                }
                string path = $"{dir}/{avatarRoot.name}_Params.asset";
                VRCExpressionParameters newParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                newParams.parameters = new VRCExpressionParameters.Parameter[0];
                AssetDatabase.CreateAsset(newParams, path);
                Undo.RecordObject(descriptor, "Assign Expression Parameters");
                descriptor.expressionParameters = newParams;
                Debug.Log($"[VRC Material Switcher] 新しい ExpressionParameters アセットを作成しました: {path}");
            }
            AssetDatabase.SaveAssets();

            // Undoグループ開始
            Undo.SetCurrentGroupName("VRC Material Switcher Setup");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                // ルートGameObjectを作成または取得
                var setupRoot = GetOrCreateSetupRoot(avatarRoot);
                var createdObjects = new List<GameObject> { setupRoot };

                // 再実行の一貫性: 前回の生成物を全て消してから作り直す。
                // （名前一致による部分削除だと、リネーム・無効化されたグループの残骸を取りこぼす）
                // ただし「Group_ で始まり、かつ MA Menu Item を持つ」＝本ツールの生成物だけを対象とし、
                // ユーザーが手で追加したオブジェクトは残す。
                for (int i = setupRoot.transform.childCount - 1; i >= 0; i--)
                {
                    var child = setupRoot.transform.GetChild(i);
                    if (IsGeneratedGroupObject(child))
                        Undo.DestroyObjectImmediate(child.gameObject);
                }

                // メインメニュー（SubMenu）のMA Menu Itemを作成
                var mainMenuItem = SetupMainMenu(setupRoot, config.menuName);

                // グループ間のパラメータ名・オブジェクト名の衝突を防ぐため、事前に一意名を割り当てる
                var paramNames = AssignUniqueParameterNames(enabledGroups);

                int totalMenuItems = 0;
                int totalParams = 0;

                foreach (var group in enabledGroups)
                {
                    var result = SetupGroupMenu(setupRoot, group, paramNames[group], config);
                    totalMenuItems += result.menuItems;
                    totalParams += result.parameters;
                    createdObjects.AddRange(result.objects);
                }

                // MAパラメータを明示宣言する（saved / synced / 既定値を確定的に適用するため。
                // 未宣言だと MA が Menu Item から自動生成し、既存の同名定義に上書きされうる）
                SetupParameterDeclarations(setupRoot, enabledGroups, paramNames, config);

                // 作成したすべてのオブジェクトおよびそのコンポーネントをダーティとしてマーク（保存対象にする）
                foreach (var obj in createdObjects)
                {
                    if (obj != null)
                    {
                        EditorUtility.SetDirty(obj);
                        foreach (var comp in obj.GetComponents<Component>())
                        {
                            if (comp != null)
                            {
                                EditorUtility.SetDirty(comp);
                            }
                        }
                    }
                }

                // シーンをダーティとしてマークして保存を促す（ビルド時の巻き戻し防止）
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);

                Undo.CollapseUndoOperations(undoGroup);
                EditorUtility.SetDirty(avatarRoot);

                return new SetupResult
                {
                    success = true,
                    message = $"セットアップ完了！\n{enabledGroups.Count}グループ、{totalMenuItems}メニュー項目、{totalParams}パラメータを追加しました。",
                    createdObjects = createdObjects,
                    menuItemCount = totalMenuItems,
                    parameterCount = totalParams
                };
            }
            catch (System.Exception e)
            {
                Undo.RevertAllInCurrentGroup();
                return SetupResult.Failure($"セットアップ中にエラーが発生しました: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// メインのSubMenuアイテムをセットアップ
        /// </summary>
        private static ModularAvatarMenuItem SetupMainMenu(GameObject setupRoot, string menuName)
        {
            var existingMenuItem = setupRoot.GetComponent<ModularAvatarMenuItem>();
            if (existingMenuItem != null)
            {
                existingMenuItem.Control.name = menuName;
                existingMenuItem.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
                existingMenuItem.MenuSource = SubmenuSource.Children;
                EditorUtility.SetDirty(existingMenuItem);
                return existingMenuItem;
            }

            var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(setupRoot);
            menuItem.Control = new VRCExpressionsMenu.Control
            {
                name = menuName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu
            };
            menuItem.MenuSource = SubmenuSource.Children;

            // 🌲 ビルド時の確実なマージを強制するために、MA Menu Installerをアタッチしておく
            var menuInstaller = setupRoot.GetComponent<ModularAvatarMenuInstaller>();
            if (menuInstaller == null)
            {
                Undo.AddComponent<ModularAvatarMenuInstaller>(setupRoot);
            }

            return menuItem;
        }

        /// <summary>
        /// グループごとに一意なパラメータ名を割り当てる。
        /// サニタイズ後の同名衝突（"Top-A" と "Top A" など）は連番サフィックスで回避する。
        /// </summary>
        private static Dictionary<MaterialGroup, string> AssignUniqueParameterNames(List<MaterialGroup> groups)
        {
            var result = new Dictionary<MaterialGroup, string>();
            var used = new HashSet<string>();

            foreach (var group in groups)
            {
                string baseName = PARAMETER_PREFIX + SanitizeParameterName(group.groupName);
                string name = baseName;
                int suffix = 2;
                while (!used.Add(name))
                {
                    name = $"{baseName}_{suffix}";
                    suffix++;
                }
                result[group] = name;
            }
            return result;
        }

        /// <summary>
        /// セットアップルートに MA Parameters を付与し、全グループのパラメータを
        /// Int / saved / synced / 既定値つきで明示宣言する。
        /// </summary>
        private static void SetupParameterDeclarations(
            GameObject setupRoot,
            List<MaterialGroup> groups,
            Dictionary<MaterialGroup, string> paramNames,
            SwitcherConfig config)
        {
            var maParams = setupRoot.GetComponent<ModularAvatarParameters>();
            if (maParams == null)
                maParams = Undo.AddComponent<ModularAvatarParameters>(setupRoot);
            else
                Undo.RecordObject(maParams, "Update Material Switcher Parameters");

            maParams.parameters = new List<ParameterConfig>();

            foreach (var group in groups)
            {
                maParams.parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = paramNames[group],
                    syncType = ParameterSyncType.Int,
                    saved = config.parameterSaved,
                    localOnly = !config.parameterSynced,
                    defaultValue = GetDefaultVariationIndex(group),
                    hasExplicitDefaultValue = true,
                });
            }

            EditorUtility.SetDirty(maParams);
        }

        /// <summary>
        /// メニューに含まれるバリエーションの中での既定値インデックスを返す。
        /// isDefault が除外されている場合は先頭の有効バリエーションを既定とする。
        /// </summary>
        private static int GetDefaultVariationIndex(MaterialGroup group)
        {
            var enabled = group.variations.Where(v => v.includeInMenu).ToList();
            int idx = enabled.FindIndex(v => v.isDefault);
            return idx >= 0 ? idx : 0;
        }

        /// <summary>
        /// 本ツールが生成したグループオブジェクトか判定する。
        /// 名前規約（Group_ 接頭辞）と MA Menu Item(SubMenu) の両方を満たすものだけを所有物とみなし、
        /// ユーザーがルート配下に手で追加したオブジェクトを再実行で消さないようにする。
        /// </summary>
        private static bool IsGeneratedGroupObject(Transform child)
        {
            if (!child.name.StartsWith(GROUP_OBJECT_PREFIX)) return false;
            var menuItem = child.GetComponent<ModularAvatarMenuItem>();
            return menuItem != null
                && menuItem.Control != null
                && menuItem.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
        }

        /// <summary>
        /// GameObject 名として安全な文字列に変換する（'/' は Transform パスを壊すため置換）。
        /// </summary>
        private static string SanitizeObjectName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            return name.Replace("/", "-").Replace("\\", "-");
        }

        /// <summary>
        /// 1つのマテリアルグループのメニューをセットアップ
        /// </summary>
        private static (int menuItems, int parameters, List<GameObject> objects) SetupGroupMenu(
            GameObject setupRoot, MaterialGroup group, string paramName, SwitcherConfig config)
        {
            var objects = new List<GameObject>();

            // '/' や '\' は Transform.Find のパスとして解釈され再実行・削除を壊すため除去する
            string groupObjName = GROUP_OBJECT_PREFIX + SanitizeObjectName(group.groupName);

            var groupObj = new GameObject(groupObjName);
            Undo.RegisterCreatedObjectUndo(groupObj, $"Create {group.groupName} group");
            groupObj.transform.SetParent(setupRoot.transform, false);
            objects.Add(groupObj);

            // グループ用SubMenu
            var groupMenuItem = Undo.AddComponent<ModularAvatarMenuItem>(groupObj);
            groupMenuItem.Control = new VRCExpressionsMenu.Control
            {
                name = group.groupName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu
            };
            groupMenuItem.MenuSource = SubmenuSource.Children;

            int menuItemCount = 0;
            int paramCount = 1;

            var enabledVariations = group.variations.Where(v => v.includeInMenu).ToList();
            int defaultIndex = GetDefaultVariationIndex(group);

            for (int i = 0; i < enabledVariations.Count; i++)
            {
                var variation = enabledVariations[i];
                bool isDefault = (i == defaultIndex);
                var varObj = CreateVariationMenuItem(groupObj, group, variation, paramName, i, isDefault, config);
                objects.Add(varObj);
                menuItemCount++;
            }

            return (menuItemCount, paramCount, objects);
        }

        /// <summary>
        /// 1つのバリエーション用のToggleメニューアイテム＋MaterialSetterを作成
        /// </summary>
        private static GameObject CreateVariationMenuItem(
            GameObject parentObj,
            MaterialGroup group,
            MaterialVariation variation,
            string paramName,
            int index,
            bool isDefault,
            SwitcherConfig config)
        {
            var varObj = new GameObject(SanitizeObjectName(variation.displayName));
            Undo.RegisterCreatedObjectUndo(varObj, $"Create {variation.displayName} toggle");
            varObj.transform.SetParent(parentObj.transform, false);

            var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(varObj);
            menuItem.Control = new VRCExpressionsMenu.Control
            {
                name = variation.displayName,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = paramName },
                value = index
            };
            menuItem.isSaved = config.parameterSaved;
            menuItem.isSynced = config.parameterSynced;
            menuItem.isDefault = isDefault;
            menuItem.automaticValue = false;

            var targets = group.GetEffectiveTargets();
            if (targets.Count > 0 && variation.material != null)
            {
                var materialSetter = Undo.AddComponent<ModularAvatarMaterialSetter>(varObj);

                // 全ターゲット（浴衣の上下など複数メッシュ）に同じマテリアルを適用する
                var switchObjects = new List<MaterialSwitchObject>();
                foreach (var t in targets)
                {
                    switchObjects.Add(new MaterialSwitchObject
                    {
                        Object = new AvatarObjectReference
                        {
                            referencePath = t.rendererPath
                        },
                        Material = variation.material,
                        MaterialIndex = t.materialSlotIndex
                    });
                }
                materialSetter.Objects = switchObjects;
            }

            return varObj;
        }

        /// <summary>
        /// セットアップ用のルートGameObjectを作成または取得
        /// </summary>
        private static GameObject GetOrCreateSetupRoot(GameObject avatarRoot)
        {
            var existing = avatarRoot.transform.Find(SETUP_ROOT_NAME);
            if (existing != null)
                return existing.gameObject;

            var root = new GameObject(SETUP_ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Material Switcher Root");
            root.transform.SetParent(avatarRoot.transform, false);

            return root;
        }

        /// <summary>
        /// 既存のセットアップを削除する
        /// </summary>
        public static int RemoveSetup(GameObject avatarRoot, string menuName = "衣装カラー")
        {
            if (avatarRoot == null) return 0;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                // 旧バージョン（直接書き込み方式）の残骸クリーンアップ。
                // 既定名は旧版のハードコード名 "衣装カラー"、現行設定のメニュー名も呼び出し側から渡せる。
                CleanUpAssetsDirect(descriptor, menuName);
            }

            var existing = avatarRoot.transform.Find(SETUP_ROOT_NAME);
            int childCount = 0;
            if (existing != null)
            {
                childCount = existing.childCount;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
            return childCount;
        }

        /// <summary>
        /// アセットから指定メニューと全パラメータを完全にクリーンアップする（直接書き込みの残骸用）
        /// </summary>
        private static void CleanUpAssetsDirect(VRCAvatarDescriptor descriptor, string menuName)
        {
            if (descriptor.expressionsMenu != null)
            {
                var menu = descriptor.expressionsMenu;
                Undo.RecordObject(menu, "Remove VRC Material Switcher Menu");
                var item = menu.controls.FirstOrDefault(c => c.name == menuName);
                if (item != null)
                {
                    menu.controls.Remove(item);
                    EditorUtility.SetDirty(menu);
                }
            }

            if (descriptor.expressionParameters != null)
            {
                var par = descriptor.expressionParameters;
                Undo.RecordObject(par, "Remove VRC Material Switcher Parameters");
                var list = par.parameters.ToList();
                int beforeCount = list.Count;
                list.RemoveAll(p => p.name.StartsWith(PARAMETER_PREFIX));
                int afterCount = list.Count;
                if (beforeCount != afterCount)
                {
                    par.parameters = list.ToArray();
                    EditorUtility.SetDirty(par);
                    Debug.Log($"[VRC Material Switcher] アセットから {beforeCount - afterCount} 個の残骸パラメータを消去しました。");
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 特定のグループのセットアップだけを削除する
        /// </summary>
        public static bool RemoveGroupSetup(GameObject avatarRoot, string groupName)
        {
            if (avatarRoot == null) return false;

            var setupRoot = avatarRoot.transform.Find(SETUP_ROOT_NAME);
            if (setupRoot == null) return false;

            string groupObjName = GROUP_OBJECT_PREFIX + SanitizeObjectName(groupName);
            var groupObj = setupRoot.Find(groupObjName);
            if (groupObj == null) return false;

            Undo.DestroyObjectImmediate(groupObj.gameObject);
            return true;
        }

        /// <summary>
        /// 現在のセットアップ情報を取得する
        /// </summary>
        public static List<(string groupName, int variationCount)> GetExistingSetup(GameObject avatarRoot)
        {
            var result = new List<(string, int)>();
            if (avatarRoot == null) return result;

            var setupRoot = avatarRoot.transform.Find(SETUP_ROOT_NAME);
            if (setupRoot == null) return result;

            for (int i = 0; i < setupRoot.childCount; i++)
            {
                var child = setupRoot.GetChild(i);
                string name = child.name;
                if (name.StartsWith(GROUP_OBJECT_PREFIX))
                {
                    string groupName = name.Substring(GROUP_OBJECT_PREFIX.Length);
                    int varCount = child.childCount;
                    result.Add((groupName, varCount));
                }
            }

            return result;
        }

        /// <summary>
        /// パラメータ名として安全な文字列に変換
        /// </summary>
        private static string SanitizeParameterName(string name)
        {
            var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
            if (string.IsNullOrEmpty(sanitized)) sanitized = "param";
            return sanitized;
        }
    }
}
#endif
