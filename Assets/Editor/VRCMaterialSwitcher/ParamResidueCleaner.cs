#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace VRCMaterialSwitcher
{
    [InitializeOnLoad]
    public static class ParamResidueCleaner
    {
        static ParamResidueCleaner()
        {
            // エディタ起動完了後にクリーンアップを実行
            EditorApplication.delayCall += AutoCleanupOnLoad;
        }

        private static void AutoCleanupOnLoad()
        {
            if (SessionState.GetBool("SwitcherCleanupDone", false)) return;
            SessionState.SetBool("SwitcherCleanupDone", true);

            Debug.Log("[Cleaner] エディタ起動時の残骸パラメータ自動クリーンアップを開始...");
            CleanupResidues(true); // 自動実行時はサイレント（ダイアログなし）
        }

        [MenuItem("Tools/VRC Material Switcher/Force Cleanup Parameter Residues")]
        public static void CleanupResiduesMenu()
        {
            CleanupResidues(false); // 手動実行時はダイアログあり
        }

        public static void CleanupResidues(bool silent)
        {
            var avatars = UnityEngine.Object.FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (avatars.Length == 0)
            {
                if (!silent) Debug.LogError("[Cleaner] シーン上にアバター（VRCAvatarDescriptor）が見つかりません。");
                return;
            }

            int totalCleaned = 0;

            foreach (var avatar in avatars)
            {
                if (avatar.expressionParameters != null)
                {
                    var par = avatar.expressionParameters;
                    Undo.RecordObject(par, "Force Remove Switcher Residue Parameters");
                    
                    var list = par.parameters.ToList();
                    int before = list.Count;
                    
                    // MatSwitch_ プレフィックスのパラメータを全削除
                    list.RemoveAll(p => p.name.StartsWith("MatSwitch_", StringComparison.OrdinalIgnoreCase));
                    
                    int after = list.Count;
                    if (before != after)
                    {
                        par.parameters = list.ToArray();
                        EditorUtility.SetDirty(par);
                        totalCleaned += (before - after);
                        Debug.Log($"[Cleaner] アバター '{avatar.gameObject.name}' のパラメータアセット '{par.name}' から {before - after} 個の残骸パラメータを消去しました。");
                    }
                }

                // メニュー側の残骸も削除
                if (avatar.expressionsMenu != null)
                {
                    var menu = avatar.expressionsMenu;
                    Undo.RecordObject(menu, "Force Remove Switcher Menu Item");
                    
                    var item = menu.controls.FirstOrDefault(c => c.name == "衣装カラー");
                    if (item != null)
                    {
                        menu.controls.Remove(item);
                        EditorUtility.SetDirty(menu);
                        Debug.Log($"[Cleaner] アバター '{avatar.gameObject.name}' のメニューから「衣装カラー」を削除しました。");
                    }
                }

                // Animator Controller 内の残骸レイヤーも削除
                CleanUpAnimatorController(avatar);
            }

            if (totalCleaned > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[Cleaner] クリーンアップ完了！合計 {totalCleaned} 個の残骸パラメータを自動消去しました。");
                
                if (!silent)
                {
                    EditorUtility.DisplayDialog("クリーンアップ完了", $"アセットから合計 {totalCleaned} 個の残骸パラメータを削除しました。\n\nこれでパラメータ過多エラーが解消されます。", "OK");
                }
            }
            else
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("クリーンアップ", "削除すべき残骸パラメータは見つかりませんでした。", "OK");
                }
            }
        }

        /// <summary>
        /// アバターのFXコントローラーからマテリアル切り替えの残骸レイヤーを完全に削除する
        /// </summary>
        private static void CleanUpAnimatorController(VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers == null) return;

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && layer.animatorController != null)
                {
                    var controller = layer.animatorController as UnityEditor.Animations.AnimatorController;
                    if (controller != null)
                    {
                        Undo.RecordObject(controller, "Remove Material Switcher Animator Layers");
                        
                        bool changed = false;
                        for (int i = controller.layers.Length - 1; i >= 0; i--)
                        {
                            string layerName = controller.layers[i].name;
                            if (layerName.StartsWith("MatSwitch_", StringComparison.OrdinalIgnoreCase))
                            {
                                controller.RemoveLayer(i);
                                changed = true;
                                Debug.Log($"[Cleaner] FXコントローラーから残骸レイヤー '{layerName}' を削除しました。");
                            }
                        }

                        // ついでにパラメータリストからも消去
                        for (int i = controller.parameters.Length - 1; i >= 0; i--)
                        {
                            string paramName = controller.parameters[i].name;
                            if (paramName.StartsWith("MatSwitch_", StringComparison.OrdinalIgnoreCase))
                            {
                                controller.RemoveParameter(i);
                                changed = true;
                                Debug.Log($"[Cleaner] FXコントローラーから残骸パラメータ '{paramName}' を削除しました。");
                            }
                        }

                        if (changed)
                        {
                            EditorUtility.SetDirty(controller);
                        }
                    }
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
