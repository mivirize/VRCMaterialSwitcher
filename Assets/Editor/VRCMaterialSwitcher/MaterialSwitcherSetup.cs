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

            var enabledGroups = config.materialGroups.Where(g => g.enabled && g.VariationCount >= 2).ToList();
            if (enabledGroups.Count == 0)
                return SetupResult.Failure("有効なマテリアルグループがありません。");

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

                // メインメニュー（SubMenu）のMA Menu Itemを作成
                var mainMenuItem = SetupMainMenu(setupRoot, config.menuName);

                int totalMenuItems = 0;
                int totalParams = 0;

                foreach (var group in enabledGroups)
                {
                    var result = SetupGroupMenu(setupRoot, group, config);
                    totalMenuItems += result.menuItems;
                    totalParams += result.parameters;
                    createdObjects.AddRange(result.objects);
                }

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
        /// 1つのマテリアルグループのメニューをセットアップ
        /// </summary>
        private static (int menuItems, int parameters, List<GameObject> objects) SetupGroupMenu(
            GameObject setupRoot, MaterialGroup group, SwitcherConfig config)
        {
            var objects = new List<GameObject>();
            string paramName = PARAMETER_PREFIX + SanitizeParameterName(group.groupName);

            // 🌲 パスバグを防ぐため、角括弧を使わない命名規則に修正
            string groupObjName = $"Group_{group.groupName}";

            var existingGroupObj = setupRoot.transform.Find(groupObjName);
            if (existingGroupObj != null)
            {
                Undo.DestroyObjectImmediate(existingGroupObj.gameObject);
            }

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
            for (int i = 0; i < enabledVariations.Count; i++)
            {
                var variation = enabledVariations[i];
                var varObj = CreateVariationMenuItem(groupObj, group, variation, paramName, i, config);
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
            SwitcherConfig config)
        {
            var varObj = new GameObject(variation.displayName);
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
            menuItem.isDefault = variation.isDefault;
            menuItem.automaticValue = false;
            menuItem.MenuSource = SubmenuSource.Children;

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
        public static int RemoveSetup(GameObject avatarRoot)
        {
            if (avatarRoot == null) return 0;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                CleanUpAssetsDirect(descriptor, "衣装カラー");
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

            string groupObjName = $"Group_{groupName}";
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
                if (name.StartsWith("Group_"))
                {
                    string groupName = name.Substring(6);
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
