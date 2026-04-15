using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    [MenuItem("Build/Build macOS")]
    public static void BuildMacOS()
    {
        var scenes = new[]
        {
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/DeckBuilder.unity",
            "Assets/Scenes/Battle.unity",
            "Assets/Scenes/Collection.unity",
            "Assets/Scenes/PackOpening.unity",
            "Assets/Scenes/Multiplayer.unity",
        };

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/DualCraft.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Build failed: {report.summary.totalErrors} errors");
            EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log($"Build succeeded: {report.summary.outputPath}");
        }
    }
}
