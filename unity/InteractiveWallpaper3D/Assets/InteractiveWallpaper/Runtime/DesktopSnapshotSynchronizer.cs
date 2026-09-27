#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class DesktopSnapshotSynchronizer : MonoBehaviour
    {
        private enum SyncPhase
        {
            Idle,
            Polling,
            Reloading,
        }

        private const float PollIntervalSeconds = 2.5f;
        private InteractiveWallpaperBootstrap? _bootstrap;
        private Task<string>? _snapshotTask;
        private SyncPhase _phase;
        private float _nextPollTime;
        private string _fingerprint = string.Empty;
        private string _iconDirectory = string.Empty;

        private void Start()
        {
            _bootstrap = InteractiveWallpaperBootstrap.Instance;
            if (_bootstrap?.Snapshot == null)
            {
                enabled = false;
                return;
            }
            _fingerprint = DesktopSnapshotFingerprint.Compute(_bootstrap.Snapshot);
            _iconDirectory = Path.Combine(Application.persistentDataPath, "desktop-icons");
            _nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }

        private void Update()
        {
            if (_bootstrap == null)
            {
                return;
            }

            if (_snapshotTask == null)
            {
                if (Time.unscaledTime >= _nextPollTime)
                {
                    BeginSnapshot(SyncPhase.Polling, false);
                }
                return;
            }

            if (!_snapshotTask.IsCompleted)
            {
                return;
            }

            var completedTask = _snapshotTask;
            _snapshotTask = null;
            if (completedTask.IsFaulted)
            {
                Debug.LogWarning($"Desktop synchronization failed: {completedTask.Exception?.GetBaseException().Message}");
                ScheduleNextPoll();
                return;
            }

            try
            {
                var snapshot = JsonUtility.FromJson<DesktopSnapshot>(completedTask.Result);
                if (snapshot == null || snapshot.items == null || snapshot.virtualDesktop == null)
                {
                    throw new InvalidDataException("Desktop synchronization snapshot is incomplete.");
                }

                var fingerprint = DesktopSnapshotFingerprint.Compute(snapshot);
                if (_phase == SyncPhase.Polling && fingerprint != _fingerprint)
                {
                    Debug.Log("Desktop change detected; refreshing proxy icons.");
                    BeginSnapshot(SyncPhase.Reloading, true);
                    return;
                }

                if (_phase == SyncPhase.Reloading)
                {
                    _fingerprint = fingerprint;
                    _bootstrap.ApplyDesktopSnapshot(snapshot, () =>
                        Debug.Log($"Desktop proxy icons synchronized: {snapshot.itemCount} items."));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to apply desktop synchronization snapshot: {exception.Message}");
            }
            ScheduleNextPoll();
        }

        private void BeginSnapshot(SyncPhase phase, bool includeIcons)
        {
            _phase = phase;
            _snapshotTask = includeIcons
                ? Task.Run(() => NativeDesktopBridge.CaptureDesktopSnapshotJson(_iconDirectory, true, 48))
                : Task.Run(() => NativeDesktopBridge.CaptureDesktopSnapshotJson(_iconDirectory, true));
        }

        private void ScheduleNextPoll()
        {
            _phase = SyncPhase.Idle;
            _nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }
    }
}