// Import TMP Essential Resources and set up default font
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

namespace DualCraft.Editor
{
    public static class TMPSetup
    {
        [MenuItem("Dual Craft/Import TMP Essentials")]
        public static void ImportTMPEssentials()
        {
            // Find TMP Essential Resources package
            string packagePath = FindTMPPackage();
            if (string.IsNullOrEmpty(packagePath))
            {
                Debug.LogError("[TMPSetup] Could not find TMP Essential Resources.unitypackage");
                return;
            }

            Debug.Log($"[TMPSetup] Importing TMP Essential Resources from: {packagePath}");
            AssetDatabase.ImportPackage(packagePath, false); // false = no dialog
            AssetDatabase.Refresh();

            // After import, set default font in TMP Settings
            EditorApplication.delayCall += SetDefaultFont;
        }

        static string FindTMPPackage()
        {
            // Search in Library/PackageCache
            string cacheDir = Path.Combine(Application.dataPath, "..", "Library", "PackageCache");
            if (Directory.Exists(cacheDir))
            {
                var files = Directory.GetFiles(cacheDir, "TMP Essential Resources.unitypackage", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }

            // Search in built-in packages
            string builtIn = Path.Combine(EditorApplication.applicationContentsPath,
                "Resources/PackageManager/BuiltInPackages/com.unity.ugui/Package Resources");
            string builtInPath = Path.Combine(builtIn, "TMP Essential Resources.unitypackage");
            if (File.Exists(builtInPath)) return builtInPath;

            return null;
        }

        static void SetDefaultFont()
        {
            // Find LiberationSans SDF that was just imported
            var guids = AssetDatabase.FindAssets("LiberationSans SDF t:TMP_FontAsset");
            if (guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets("LiberationSans SDF");
            }

            if (guids.Length == 0)
            {
                Debug.LogWarning("[TMPSetup] LiberationSans SDF font not found after import. Searching for any TMP font...");
                guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            }

            if (guids.Length > 0)
            {
                string fontPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                Debug.Log($"[TMPSetup] Found TMP font at: {fontPath}");

                // Find TMP Settings
                var settingsGuids = AssetDatabase.FindAssets("TMP Settings t:TMP_Settings");
                if (settingsGuids.Length == 0)
                    settingsGuids = AssetDatabase.FindAssets("TMP Settings");

                if (settingsGuids.Length > 0)
                {
                    string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuids[0]);
                    Debug.Log($"[TMPSetup] Found TMP Settings at: {settingsPath}");

                    var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(settingsPath);
                    if (settings != null)
                    {
                        var fontAsset = AssetDatabase.LoadAssetAtPath<Object>(fontPath);
                        var field = settings.GetType().GetField("m_defaultFontAsset",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(settings, fontAsset);
                            EditorUtility.SetDirty(settings);
                            AssetDatabase.SaveAssets();
                            Debug.Log("[TMPSetup] Default font asset set successfully!");
                        }
                        else
                        {
                            Debug.LogWarning("[TMPSetup] Could not find m_defaultFontAsset field, trying serialized property...");
                            var so = new SerializedObject(settings);
                            var prop = so.FindProperty("m_defaultFontAsset");
                            if (prop != null)
                            {
                                prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(fontPath);
                                so.ApplyModifiedProperties();
                                Debug.Log("[TMPSetup] Default font asset set via SerializedObject!");
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[TMPSetup] TMP Settings asset not found");
                }
            }
            else
            {
                Debug.LogError("[TMPSetup] No TMP font assets found after import!");
            }

            Debug.Log("[TMPSetup] === TMP SETUP COMPLETE ===");
        }
    }
}
#endif
