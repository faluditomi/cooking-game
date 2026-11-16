using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// One-click player builds for the jam, into Builds/ (git-ignored). The player settings that
    /// matter are applied before each build, so they can't drift: Windows runs full screen, and
    /// WebGL uses gzip with a decompression fallback, which works on itch.io without server setup.
    /// </summary>
    public static class CookingBuild
    {
        private const string Scene = "Assets/Cooking/Prototype/CookingPrototype.unity";
        public const string WindowsPath = "Builds/Windows/EmphisStall.exe";
        public const string WebGLPath = "Builds/WebGL";

        [MenuItem("Custom Tools/Cooking Prototype/Build Windows")]
        public static void BuildWindows()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            Build(BuildTarget.StandaloneWindows64, WindowsPath);
        }

        [MenuItem("Custom Tools/Cooking Prototype/Build WebGL")]
        public static void BuildWebGL()
        {
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("[Cooking] WebGL Build Support isn't installed. Add it to this Unity version in Unity Hub (Installs → ⚙ → Add modules).");
                return;
            }

            // itch.io doesn't send Content-Encoding headers, so let the loader decompress itself.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            Build(BuildTarget.WebGL, WebGLPath);
        }

        private static void Build(BuildTarget target, string path)
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            });

            BuildSummary summary = report.summary;
            string message = "[Cooking] " + target + " build " + summary.result + " in " + summary.totalTime.TotalSeconds.ToString("0") + " s: "
                             + path + " (" + (summary.totalSize / 1048576f).ToString("0.0") + " MB, " + summary.totalErrors + " errors).";

            if(summary.result == BuildResult.Succeeded) Debug.Log(message);
            else Debug.LogError(message);
        }
    }
}
