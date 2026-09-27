#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;

namespace InteractiveWallpaper.Editor
{
    public static class RuntimeBuild
    {
        [MenuItem("交互式壁纸/Build Windows Runtime")]
        public static void BuildWindowsRuntime()
        {
            var output = Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_UNITY_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "Builds",
                    "Windows",
                    "InteractiveWallpaper.exe"));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            PlayerSettings.productName = "交互式壁纸";
            PlayerSettings.companyName = "InteractiveWallpaper";
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Interactive Wallpaper build failed: {report.summary.result}, " +
                    $"errors={report.summary.totalErrors}");
            }

            var nativeLibrary = FindNativeLibrary();
            var deployedLibrary = Path.Combine(
                Path.GetDirectoryName(output)!,
                "InteractiveWallpaper.Native.dll");
            File.Copy(nativeLibrary, deployedLibrary, true);

            var backupDirectory = Path.Combine(
                Path.GetDirectoryName(output)!,
                Path.GetFileNameWithoutExtension(output) + "_BackUpThisFolder_ButDontShipItWithYourGame");
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, true);
            }

            Debug.Log($"Built Interactive Wallpaper: {output}");
            Debug.Log("Runtime idle cat threshold: 60s by default; set INTERACTIVE_WALLPAPER_DEBUG_BUILD=1 before launch for 10s testing.");
            Debug.Log($"Deployed native desktop bridge: {deployedLibrary}");
        }

        private static string FindNativeLibrary()
        {
            var configured = Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_NATIVE_DLL");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }

            var repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var candidates = new[]
            {
                Path.Combine(repositoryRoot, "out", "build", "bin", "Release", "InteractiveWallpaper.Native.dll"),
                Path.Combine(repositoryRoot, "out", "build", "bin", "Debug", "InteractiveWallpaper.Native.dll"),
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new BuildFailedException(
                "InteractiveWallpaper.Native.dll was not found. Build the native CMake target first " +
                "or set INTERACTIVE_WALLPAPER_NATIVE_DLL.");
        }
    }
}



