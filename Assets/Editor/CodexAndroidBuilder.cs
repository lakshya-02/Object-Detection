using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class CodexAndroidBuilder
{
    private const string DefaultBuildPath = "Builds/ObjectDetection-Quest.apk";

    [MenuItem("Build/Codex/Build Quest APK")]
    public static void BuildQuestApkFromMenu()
    {
        BuildQuestApk();
    }

    public static void BuildQuestApk()
    {
        string buildPath = Environment.GetEnvironmentVariable("CODEX_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(buildPath))
        {
            buildPath = DefaultBuildPath;
        }

        buildPath = Path.GetFullPath(buildPath);
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath) ?? Directory.GetCurrentDirectory());

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No enabled scenes are configured in Editor Build Settings.");
        }

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Android build failed with result " + summary.result + " and " + summary.totalErrors + " errors.");
        }

        UnityEngine.Debug.Log("Android APK built: " + buildPath + " (" + summary.totalSize + " bytes)");
    }
}
