#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace InteractiveWallpaper
{
    [DefaultExecutionOrder(-10000)]
    public sealed class InteractiveWallpaperBootstrap : MonoBehaviour
    {
        private const float WindowAttachmentRetrySeconds = 1f;
        private const string SingleInstanceMutexName = "Local\\InteractiveWallpaper.SingleInstance";
        private const string DesktopSnapshotCacheName = "desktop-snapshot.json";

        private float _nextWindowAttachmentAttempt;
        private float _startupStartedAt;
        private IntPtr _singleInstanceHandle;
        private bool _ownsSingleInstanceMutex;
        private bool _quitShortcutDown;
        private bool _gravityShortcutDown;
        private bool _kickShortcutDown;
        private bool _resetShortcutDown;
        private bool _sweepShortcutDown;
        private bool _shockwaveShortcutDown;
        private bool _sneezeShortcutDown;
        private bool _overlayShortcutDown;
        private bool _portalShortcutDown;
        private bool _robotShortcutDown;
        private bool _weatherShortcutDown;

        public static bool ShowcaseMode => string.Equals(
            Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_SHOWCASE"),
            "1",
            StringComparison.OrdinalIgnoreCase);
        public static string ShowcaseEffect => Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_SHOWCASE_EFFECT") ?? string.Empty;
        public static bool HumanCharacterEnabled =>
            (ShowcaseMode && string.Equals(ShowcaseEffect, "human", StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_ENABLE_HUMAN"), "1", StringComparison.OrdinalIgnoreCase);
        public static InteractiveWallpaperBootstrap? Instance { get; private set; }
        public DesktopSnapshot? Snapshot { get; private set; }
        public string Status { get; private set; } = "正在启动交互式壁纸…";
        public bool NativeBridgeReady { get; private set; }
        public bool DesktopWindowAttached { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateBootstrap()
        {
            if (Instance != null)
            {
                return;
            }
            var host = new GameObject("Interactive Wallpaper Runtime");
            DontDestroyOnLoad(host);
            host.AddComponent<InteractiveWallpaperBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _startupStartedAt = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            if (!ShowcaseMode)
            {
                _singleInstanceHandle = WindowsSingleInstance.Create(SingleInstanceMutexName, out _ownsSingleInstanceMutex);
                if (!_ownsSingleInstanceMutex)
                {
                    WindowsDialog.ShowAlreadyRunning();
                    Application.Quit(0);
                    return;
                }
            }

            EnsurePreviewCamera();
            gameObject.AddComponent<RuntimeStatusOverlay>();
            gameObject.AddComponent<DesktopWallpaperRenderer>();
            if (HumanCharacterEnabled && VideoCharacterPerformance.IsVideoAvailable)
            {
                // Start the character poster/video in parallel with Shell icon extraction.
                gameObject.AddComponent<VideoCharacterPerformance>();
            }
            if (!ShowcaseMode || string.Equals(ShowcaseEffect, "cat", StringComparison.OrdinalIgnoreCase))
            {
                gameObject.AddComponent<IdleCatDirector>();
            }
            StartCoroutine(ShowcaseMode ? InitializeShowcase() : InitializeRuntime());
        }

        private IEnumerator InitializeShowcase()
        {
            // A real Unity Player scene with synthetic icons: never read or attach to the user's desktop.
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return null;
            Snapshot = CreateShowcaseSnapshot(Math.Max(1, Screen.width), Math.Max(1, Screen.height));
            NativeBridgeReady = true;
            Status = "Unity 特效演示场景（合成图标，不访问真实桌面）";
            AddRuntimeComponents();
            RuntimeStatusOverlay.Instance?.ToggleVisibility();
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_SHOWCASE_CAPTURE_DIR")))
            {
                gameObject.AddComponent<ShowcaseCaptureDirector>();
            }
        }

        private static DesktopSnapshot CreateShowcaseSnapshot(int width, int height)
        {
            var samples = new (string name, float x, float y)[]
            {
                ("Scenes", .09f, .14f), ("Materials", .21f, .14f),
                ("Portal", .09f, .31f), ("Robot", .21f, .31f),
                ("Wind", .09f, .48f), ("Cat", .21f, .48f),
                ("Notes", .09f, .65f), ("FX", .21f, .65f),
                ("Textures", .72f, .16f), ("Particles", .84f, .16f),
                ("Samples", .72f, .35f), ("Archive", .84f, .35f),
                ("Models", .72f, .54f), ("Audio", .84f, .54f),
            };
            var snapshot = new DesktopSnapshot
            {
                schemaVersion = 1,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                virtualDesktop = new VirtualDesktopBounds { x = 0, y = 0, width = width, height = height },
                view = new DesktopViewSnapshot { iconSize = 64, mode = 1 },
                items = new DesktopItemSnapshot[samples.Length],
                itemCount = samples.Length,
            };
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                snapshot.items[index] = new DesktopItemSnapshot
                {
                    stableId = $"showcase:{index}:{sample.name}",
                    displayName = sample.name,
                    typeName = "Effect sample",
                    position = new DesktopPoint
                    {
                        x = Mathf.RoundToInt(width * sample.x),
                        y = Mathf.RoundToInt(height * sample.y),
                    },
                };
            }
            return snapshot;
        }

        private IEnumerator InitializeRuntime()
        {
            // Render the wallpaper and status overlay immediately, then enter the desktop layer
            // before the more expensive Shell icon extraction completes.
            yield return null;
            Status = "正在进入桌面层…";
            TryAttachDesktopWindow();
            Status = DesktopWindowAttached
                ? "已进入桌面层，正在读取图标和布局…"
                : "正在读取图标和布局；桌面窗口稍后重试…";

            var iconDirectory = Path.Combine(Application.persistentDataPath, "desktop-icons");
            Directory.CreateDirectory(iconDirectory);
            var snapshotCachePath = Path.Combine(Application.persistentDataPath, DesktopSnapshotCacheName);
            var snapshotTask = Task.Run(() =>
                NativeDesktopBridge.CaptureDesktopSnapshotJson(iconDirectory, true, 48));

            var useFreshSnapshot = !TryLoadCachedSnapshot(snapshotCachePath, out var cachedSnapshot);
            if (!useFreshSnapshot)
            {
                try
                {
                    Snapshot = cachedSnapshot;
                    ValidateSnapshot(Snapshot);
                    NativeBridgeReady = true;
                    Debug.Log($"Cached desktop snapshot: {Snapshot!.virtualDesktop.width}x{Snapshot.virtualDesktop.height}; items={Snapshot.itemCount}");
                    AddRuntimeComponents();
                    Status = $"已加载缓存的 {Snapshot.itemCount} 个桌面项目；正在后台刷新…";
                    StartCoroutine(RefreshSnapshotInBackground(snapshotTask, snapshotCachePath));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Cached desktop snapshot is invalid; waiting for fresh snapshot: {exception.Message}");
                    useFreshSnapshot = true;
                }
            }
            if (useFreshSnapshot)
            {
                yield return WaitForFreshSnapshot(snapshotTask, snapshotCachePath);
            }

            Status = DesktopWindowAttached
                ? $"已加载 {Snapshot!.itemCount} 个桌面项目；程序正在桌面层运行"
                : $"已加载 {Snapshot!.itemCount} 个桌面项目；正在等待桌面窗口";
            Debug.Log($"Interactive Wallpaper ready: {Status}; elapsed={Time.realtimeSinceStartup - _startupStartedAt:F2}s");
        }

        private void Update()
        {
            var quitShortcutDown = WindowsKeyboard.IsQuitShortcutDown();
            if (quitShortcutDown && !_quitShortcutDown)
            {
                Application.Quit(0);
                return;
            }
            _quitShortcutDown = quitShortcutDown;
            HandleDesktopEffectShortcuts();

            if (ShowcaseMode || DesktopWindowAttached || Time.unscaledTime < _nextWindowAttachmentAttempt)
            {
                return;
            }

            _nextWindowAttachmentAttempt = Time.unscaledTime + WindowAttachmentRetrySeconds;
            TryAttachDesktopWindow();
        }

        private void HandleDesktopEffectShortcuts()
        {
            var gravityDown = WindowsKeyboard.IsShortcutDown(0x47);
            if (gravityDown && !_gravityShortcutDown)
            {
                ProxyIconWorld.Instance?.DropAll();
            }
            _gravityShortcutDown = gravityDown;

            var kickDown = WindowsKeyboard.IsShortcutDown(0x4B);
            if (kickDown && !_kickShortcutDown)
            {
                ProxyIconWorld.Instance?.KickSelected();
            }
            _kickShortcutDown = kickDown;

            var resetDown = WindowsKeyboard.IsShortcutDown(0x52);
            if (resetDown && !_resetShortcutDown)
            {
                ProxyIconWorld.Instance?.ResetAll();
            }
            _resetShortcutDown = resetDown;

            var sweepDown = WindowsKeyboard.IsShortcutDown(0x57);
            if (sweepDown && !_sweepShortcutDown)
            {
                ProxyIconWorld.Instance?.SweepAll();
            }
            _sweepShortcutDown = sweepDown;

            var shockwaveDown = WindowsKeyboard.IsShortcutDown(0x42);
            if (shockwaveDown && !_shockwaveShortcutDown)
            {
                ProxyIconWorld.Instance?.Shockwave();
            }
            _shockwaveShortcutDown = shockwaveDown;
            var sneezeDown = WindowsKeyboard.IsShortcutDown(0x53);
            if (sneezeDown && !_sneezeShortcutDown)
            {
                VideoCharacterPerformance.Instance?.Replay();
            }
            _sneezeShortcutDown = sneezeDown;
            var overlayDown = WindowsKeyboard.IsShortcutDown(0x48);
            if (overlayDown && !_overlayShortcutDown)
            {
                RuntimeStatusOverlay.Instance?.ToggleVisibility();
            }
            _overlayShortcutDown = overlayDown;

            var portalDown = WindowsKeyboard.IsShortcutDown(0x50);
            if (portalDown && !_portalShortcutDown)
            {
                ProxyIconWorld.Instance?.StartPortalEffect();
            }
            _portalShortcutDown = portalDown;

            var robotDown = WindowsKeyboard.IsShortcutDown(0x4D);
            if (robotDown && !_robotShortcutDown)
            {
                ProxyIconWorld.Instance?.StartRobotEffect();
            }
            _robotShortcutDown = robotDown;

            var weatherDown = WindowsKeyboard.IsShortcutDown(0x54);
            if (weatherDown && !_weatherShortcutDown)
            {
                ProxyIconWorld.Instance?.StartWeatherEffect();
            }
            _weatherShortcutDown = weatherDown;
        }
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            WindowsSingleInstance.Close(_singleInstanceHandle, _ownsSingleInstanceMutex);
            _singleInstanceHandle = IntPtr.Zero;
            _ownsSingleInstanceMutex = false;
        }

        private IEnumerator WaitForFreshSnapshot(Task<string> snapshotTask, string cachePath)
        {
            while (!snapshotTask.IsCompleted)
            {
                yield return null;
            }

            if (snapshotTask.IsFaulted)
            {
                Fatal(
                    $"交互式壁纸启动失败：{snapshotTask.Exception?.GetBaseException().Message}",
                    3);
                yield break;
            }

            try
            {
                Snapshot = JsonUtility.FromJson<DesktopSnapshot>(snapshotTask.Result);
                ValidateSnapshot(Snapshot);
                NativeBridgeReady = true;
                File.WriteAllText(cachePath, snapshotTask.Result);
                Debug.Log($"Primary desktop snapshot: {Snapshot!.virtualDesktop.width}x{Snapshot.virtualDesktop.height}; iconSize={Snapshot.view.iconSize}; items={Snapshot.itemCount}");
                Status = $"已读取 {Snapshot.itemCount} 个主屏桌面项目，正在进入桌面层…";
                AddRuntimeComponents();
            }
            catch (Exception exception)
            {
                Fatal($"交互式壁纸启动失败：{exception.Message}", 3);
            }
        }

        private IEnumerator RefreshSnapshotInBackground(Task<string> snapshotTask, string cachePath)
        {
            while (!snapshotTask.IsCompleted)
            {
                yield return null;
            }
            if (snapshotTask.IsFaulted)
            {
                Debug.LogWarning($"Background desktop snapshot refresh failed: {snapshotTask.Exception?.GetBaseException().Message}");
                yield break;
            }

            try
            {
                var fresh = JsonUtility.FromJson<DesktopSnapshot>(snapshotTask.Result);
                ValidateSnapshot(fresh);
                File.WriteAllText(cachePath, snapshotTask.Result);
                var oldFingerprint = DesktopSnapshotFingerprint.Compute(Snapshot!);
                var newFingerprint = DesktopSnapshotFingerprint.Compute(fresh);
                if (!string.Equals(oldFingerprint, newFingerprint, StringComparison.Ordinal))
                {
                    Debug.Log("Fresh desktop snapshot differs from cache; synchronizing proxy icons.");
                    ApplyDesktopSnapshot(fresh, () => Debug.Log($"Desktop proxy icons synchronized: {fresh.itemCount} items."));
                }
                else
                {
                    Debug.Log("Fresh desktop snapshot matches cache; keeping existing proxy icons.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to apply background desktop snapshot: {exception.Message}");
            }
        }

        private void AddRuntimeComponents()
        {
            if (GetComponent<ProxyIconSpawner>() == null)
            {
                gameObject.AddComponent<ProxyIconSpawner>();
            }
            if (!ShowcaseMode && GetComponent<DesktopSnapshotSynchronizer>() == null)
            {
                gameObject.AddComponent<DesktopSnapshotSynchronizer>();
            }
            if (HumanCharacterEnabled && VideoCharacterPerformance.Instance == null && GetComponent<ProceduralCharacterController>() == null)
            {
                gameObject.AddComponent<ProceduralCharacterController>();
            }
        }

        private static bool TryLoadCachedSnapshot(string path, out DesktopSnapshot? snapshot)
        {
            snapshot = null;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }
                snapshot = JsonUtility.FromJson<DesktopSnapshot>(File.ReadAllText(path));
                return snapshot?.items != null && snapshot.virtualDesktop != null;
            }
            catch
            {
                snapshot = null;
                return false;
            }
        }

        public void ApplyDesktopSnapshot(DesktopSnapshot snapshot, Action? completed = null)
        {
            ValidateSnapshot(snapshot);
            Snapshot = snapshot;
            var spawner = GetComponent<ProxyIconSpawner>();
            if (spawner == null)
            {
                completed?.Invoke();
                return;
            }
            spawner.Rebuild(snapshot, () =>
            {
                Status = $"桌面已同步：{snapshot.itemCount} 个主屏项目";
                completed?.Invoke();
            });
        }
        public bool RequestOpenDesktopItem(string stableId, string displayName)
        {
            if (ShowcaseMode)
            {
                Status = "演示图标不对应真实文件";
                return false;
            }
            try
            {
                if (NativeDesktopBridge.OpenDesktopItem(stableId, out var error))
                {
                    Status = $"已打开 {displayName}";
                    return true;
                }
                Status = $"无法打开 {displayName}：{error}";
                return false;
            }
            catch (Exception exception)
            {
                Status = $"无法打开 {displayName}：{exception.Message}";
                return false;
            }
        }

        private void TryAttachDesktopWindow()
        {
            try
            {
                if (NativeDesktopBridge.TryAttachCurrentProcessWindow(out var attachment, out var error))
                {
                    DesktopWindowAttached = true;
                    Status = $"已进入桌面层：{attachment.Width}x{attachment.Height}";
                    Debug.Log($"Desktop window attached: {attachment.Width}x{attachment.Height}; elapsed={Time.realtimeSinceStartup - _startupStartedAt:F2}s");
                    return;
                }
                Status = string.IsNullOrWhiteSpace(error)
                    ? "正在等待 Unity 桌面窗口…"
                    : $"尚未进入桌面层：{error}";
            }
            catch (Exception exception)
            {
                Status = $"桌面附着不可用：{exception.Message}";
            }
        }

        private static void ValidateSnapshot(DesktopSnapshot? snapshot)
        {
            if (snapshot?.virtualDesktop == null || snapshot.items == null)
            {
                throw new InvalidDataException("桌面快照不完整。");
            }
            if (snapshot.itemCount != snapshot.items.Length)
            {
                throw new InvalidDataException("桌面快照项目数量不一致。");
            }
        }

        private static void EnsurePreviewCamera()
        {
            if (Camera.main != null)
            {
                return;
            }
            var cameraObject = new GameObject("Interactive Wallpaper Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.075f, 1f);
        }

        private void Fatal(string message, int exitCode)
        {
            Status = message;
            Debug.LogError(message);
            EnsurePreviewCamera();
            if (GetComponent<RuntimeStatusOverlay>() == null)
            {
                gameObject.AddComponent<RuntimeStatusOverlay>();
            }
            if (!Application.isEditor)
            {
                WindowsDialog.ShowError(message);
                Application.Quit(exitCode);
            }
        }

        private static class WindowsKeyboard
        {
            private const int VirtualKeyControl = 0x11;
            private const int VirtualKeyAlt = 0x12;
            private const int VirtualKeyQ = 0x51;

            [DllImport("user32.dll")]
            private static extern short GetAsyncKeyState(int virtualKey);

            public static bool IsQuitShortcutDown()
            {
                return IsShortcutDown(VirtualKeyQ);
            }

            public static bool IsShortcutDown(int virtualKey)
            {
                return IsDown(VirtualKeyControl) &&
                    IsDown(VirtualKeyAlt) &&
                    IsDown(virtualKey);
            }

            private static bool IsDown(int virtualKey)
            {
                return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
            }
        }
        private static class WindowsSingleInstance
        {
            private const int AlreadyExists = 183;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateMutexW(
                IntPtr attributes,
                bool initialOwner,
                string name);

            [DllImport("kernel32.dll")]
            private static extern bool ReleaseMutex(IntPtr mutex);

            [DllImport("kernel32.dll")]
            private static extern bool CloseHandle(IntPtr handle);

            public static IntPtr Create(string name, out bool ownsMutex)
            {
                var handle = CreateMutexW(IntPtr.Zero, true, name);
                if (handle == IntPtr.Zero)
                {
                    ownsMutex = true;
                    return IntPtr.Zero;
                }
                ownsMutex = Marshal.GetLastWin32Error() != AlreadyExists;
                return handle;
            }

            public static void Close(IntPtr handle, bool ownsMutex)
            {
                if (handle == IntPtr.Zero)
                {
                    return;
                }
                if (ownsMutex)
                {
                    ReleaseMutex(handle);
                }
                CloseHandle(handle);
            }
        }
        private static class WindowsDialog
        {
            private const uint InformationIcon = 0x00000040;

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int MessageBoxW(
                IntPtr window,
                string text,
                string caption,
                uint type);

            public static void ShowAlreadyRunning()
            {
                if (Application.platform != RuntimePlatform.WindowsPlayer)
                {
                    return;
                }
                MessageBoxW(
                    IntPtr.Zero,
                    "交互式壁纸已经在桌面层运行。\n\n按 Win+D 可以立即查看桌面效果。",
                    "交互式壁纸",
                    InformationIcon);
            }

            public static void ShowError(string message)
            {
                if (Application.platform != RuntimePlatform.WindowsPlayer)
                {
                    return;
                }
                MessageBoxW(
                    IntPtr.Zero,
                    message,
                    "交互式壁纸启动失败",
                    0x00000010);
            }
        }
    }
}
