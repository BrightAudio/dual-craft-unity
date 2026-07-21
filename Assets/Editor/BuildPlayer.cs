// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Build Script for macOS Standalone
// ═══════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DualCraft.Editor
{
    public static class BuildPlayer
    {
        [MenuItem("Dual Craft/Build macOS")]
        public static void BuildMacOS()
        {
            string buildPath = System.IO.Directory.Exists("/Volumes/Seagate")
                ? "/Volumes/Seagate/DualMonBuilds/Mac/Dual5Mon.app"
                : "Builds/Dual5Mon.app";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(buildPath));

            // Force Mono scripting backend (IL2CPP may not be installed)
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            var scenes = new[]
            {
                "Assets/Scenes/Battle.unity",
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/Collection.unity",
                "Assets/Scenes/DeckBuilder.unity",
            };

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = buildPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };

            Debug.Log("[Build] Starting macOS build...");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] SUCCESS — {summary.totalSize} bytes at {buildPath}");
            }
            else
            {
                Debug.LogError($"[Build] FAILED — {summary.totalErrors} errors");
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error)
                            Debug.LogError($"[Build] {msg.content}");
                    }
                }
            }
        }
    }
}
#endif
