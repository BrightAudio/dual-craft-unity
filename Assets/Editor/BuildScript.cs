using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildScript
{
    private const string ExternalBuildRoot = "/Volumes/Seagate/DualMonBuilds";

    [InitializeOnLoadMethod]
    private static void ConfigureBatchImportWorkers()
    {
        if (System.Environment.GetEnvironmentVariable("DUALMON_SINGLE_PROCESS_IMPORT") == "1")
            EditorUserSettings.desiredImportWorkerCount = 0;
    }

    private static readonly string[] Scenes =
    {
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/DeckBuilder.unity",
        "Assets/Scenes/Battle.unity",
        "Assets/Scenes/Collection.unity",
        "Assets/Scenes/PackOpening.unity",
        "Assets/Scenes/Multiplayer.unity",
        "Assets/Scenes/Story.unity",
    };

    [MenuItem("Build/Build macOS")]
    public static void BuildMacOS()
    {
        // Force re-import of any externally added assets
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // Ensure all card art and UI PNGs are imported as Sprites
        ForceReimportSprites.Run();

        string outputPath = System.Environment.GetEnvironmentVariable("DUALMON_MAC_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = DefaultBuildPath("Mac/Dual5Mon.app", "Builds/Dual5Mon.app");

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputPath,
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

    [MenuItem("Build/Build Windows")]
    public static void BuildWindows()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ForceReimportSprites.Run();
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

        string outputPath = System.Environment.GetEnvironmentVariable("DUALMON_WINDOWS_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = DefaultBuildPath("Windows/Dual5Mon.exe", "Builds/Windows/Dual5Mon.exe");

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Windows build failed: {report.summary.totalErrors} errors");
            EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log($"Windows build succeeded: {report.summary.outputPath}");
        }
    }

    [MenuItem("Build/Build Linux Dedicated Server")]
    public static void BuildLinuxDedicatedServer()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
        const string serverScenePath = "Assets/Scenes/DedicatedServer.unity";
        EnsureEmptyServerScene(serverScenePath);

        string outputPath = System.Environment.GetEnvironmentVariable("DUALMON_SERVER_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = DefaultBuildPath("Server/DualMonServer.x86_64", "Builds/Server/DualMonServer.x86_64");

        var options = new BuildPlayerOptions
        {
            scenes = new[] { serverScenePath },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Linux server build failed: {report.summary.totalErrors} errors");
            EditorApplication.Exit(1);
        }

        Debug.Log($"Linux server build succeeded: {report.summary.outputPath}");
    }

    private static void EnsureEmptyServerScene(string scenePath)
    {
        if (System.IO.File.Exists(scenePath))
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
    }

    private static string DefaultBuildPath(string externalRelativePath, string localFallbackPath)
    {
        if (System.IO.Directory.Exists("/Volumes/Seagate"))
        {
            string path = System.IO.Path.Combine(ExternalBuildRoot, externalRelativePath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            return path;
        }

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localFallbackPath));
        return localFallbackPath;
    }
}
