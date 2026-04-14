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
            string buildPath = "Builds/DualCraft.app";

            // Force Mono scripting backend (IL2CPP may not be installed)
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            var scenes = new[]
            {
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/Battle.unity",
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
