#nullable enable

using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace InteractiveWallpaper
{
    /// <summary>Captures real Player frames over a synthetic, privacy-safe desktop fixture.</summary>
    public sealed class ShowcaseCaptureDirector : MonoBehaviour
    {
        private IEnumerator Start()
        {
            var directory = Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_SHOWCASE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory))
            {
                yield break;
            }
            Directory.CreateDirectory(directory);

            var bootstrap = InteractiveWallpaperBootstrap.Instance;
            var deadline = Time.realtimeSinceStartup + 30f;
            while (ProxyIconWorld.Instance == null || bootstrap?.Snapshot == null ||
                   ProxyIconWorld.Instance.Icons.Count < bootstrap.Snapshot.itemCount)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogError("Showcase capture timed out waiting for synthetic icons.");
                    Application.Quit(1);
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.45f);

            var effect = InteractiveWallpaperBootstrap.ShowcaseEffect.Trim().ToLowerInvariant();
            var world = ProxyIconWorld.Instance!;
            switch (effect)
            {
                case "portal":
                    world.StartPortalEffect();
                    yield return new WaitForSecondsRealtime(1.55f);
                    break;
                case "robot":
                    world.StartRobotEffect();
                    yield return new WaitForSecondsRealtime(2.52f);
                    break;
                case "wind":
                    world.StartWeatherEffect();
                    yield return new WaitForSecondsRealtime(1.95f);
                    break;
                case "cat":
                    IdleCatDirector.Instance?.StartShowcaseTheft();
                    yield return new WaitForSecondsRealtime(7.0f);
                    break;
                case "human":
                    deadline = Time.realtimeSinceStartup + 18f;
                    while (VideoCharacterPerformance.Instance == null ||
                           !VideoCharacterPerformance.Instance.ActionStatus.Contains("准备打喷嚏"))
                    {
                        if (Time.realtimeSinceStartup >= deadline)
                        {
                            Debug.LogError("Showcase human video did not enter the sneeze clip.");
                            Application.Quit(2);
                            yield break;
                        }
                        yield return null;
                    }
                    yield return new WaitForSecondsRealtime(2.35f);
                    break;
                default:
                    effect = "overview";
                    break;
            }

            var file = Path.Combine(directory, effect + ".png");
            ScreenCapture.CaptureScreenshot(file);
            deadline = Time.realtimeSinceStartup + 8f;
            while ((!File.Exists(file) || new FileInfo(file).Length < 1024) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (!File.Exists(file) || new FileInfo(file).Length < 1024)
            {
                Debug.LogError("Showcase screenshot was not written: " + file);
                Application.Quit(3);
                yield break;
            }
            Debug.Log("Showcase screenshot captured: " + file);
            yield return new WaitForSecondsRealtime(0.25f);
            Application.Quit(0);
        }
    }
}
