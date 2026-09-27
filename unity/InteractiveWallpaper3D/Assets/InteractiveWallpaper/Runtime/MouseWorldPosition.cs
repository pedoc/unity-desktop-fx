#nullable enable

using System.Runtime.InteropServices;
using UnityEngine;

namespace InteractiveWallpaper
{
    public static class MouseWorldPosition
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out NativePoint point);

        public static bool TryGetWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            var camera = Camera.main;
            if (camera == null || !GetCursorPos(out var cursor))
            {
                return false;
            }

            // Windows primary-display coordinates use (0, 0) at the top-left.
            // Unity screen coordinates use (0, 0) at the bottom-left.
            if (cursor.X < 0 || cursor.Y < 0 || cursor.X >= Screen.width || cursor.Y >= Screen.height)
            {
                return false;
            }

            var screenPosition = new Vector3(
                cursor.X,
                Screen.height - 1 - cursor.Y,
                0f);
            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out var distance))
            {
                return false;
            }

            var point = ray.GetPoint(distance);
            if (!IsInsideWorld(point))
            {
                return false;
            }
            point.z = 0f;
            worldPosition = point;
            return true;
        }

        public static Vector3 GetRandomWorldPosition()
        {
            var world = ProxyIconWorld.Instance;
            var width = world != null ? world.WorldWidth : 16f;
            var height = world != null ? world.WorldHeight : 9f;
            return new Vector3(
                Random.Range(-width * 0.45f, width * 0.45f),
                Random.Range(-height * 0.45f, height * 0.45f),
                0f);
        }

        private static bool IsInsideWorld(Vector3 point)
        {
            var world = ProxyIconWorld.Instance;
            if (world == null)
            {
                return true;
            }
            return point.x >= -world.WorldWidth * 0.5f &&
                point.x <= world.WorldWidth * 0.5f &&
                point.y >= -world.WorldHeight * 0.5f &&
                point.y <= world.WorldHeight * 0.5f;
        }
    }
}