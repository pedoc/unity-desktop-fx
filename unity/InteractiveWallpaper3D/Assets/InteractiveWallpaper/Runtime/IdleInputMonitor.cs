#nullable enable

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class IdleInputMonitor : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        private float _previousIdleSeconds;

        public float IdleSeconds { get; private set; }
        public event Action? InputDetected;

        private void Update()
        {
            var current = ReadIdleSeconds();
            if (current + 0.05f < _previousIdleSeconds)
            {
                InputDetected?.Invoke();
            }
            _previousIdleSeconds = current;
            IdleSeconds = current;
        }

        private static float ReadIdleSeconds()
        {
            var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
            {
                return 0f;
            }

            var elapsedMilliseconds = unchecked(GetTickCount() - info.dwTime);
            return elapsedMilliseconds / 1000f;
        }
    }
}
