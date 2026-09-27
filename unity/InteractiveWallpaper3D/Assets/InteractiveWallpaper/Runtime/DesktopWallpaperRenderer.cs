#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class DesktopWallpaperRenderer : MonoBehaviour
    {
        private const uint GetDesktopWallpaper = 0x0073;
        private SpriteRenderer? _renderer;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void Start()
        {
            try
            {
                Texture2D texture;
                if (InteractiveWallpaperBootstrap.ShowcaseMode)
                {
                    texture = CreateShowcaseBackdrop();
                }
                else
                {
                    var wallpaperPath = GetWallpaperPath();
                    if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
                    {
                        Debug.LogWarning($"Desktop wallpaper image was not found: {wallpaperPath}");
                        return;
                    }

                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                    {
                        name = "Windows desktop wallpaper",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(wallpaperPath), true))
                    {
                        Destroy(texture);
                        Debug.LogWarning($"Unable to decode desktop wallpaper: {wallpaperPath}");
                        return;
                    }
                    Debug.Log($"Desktop wallpaper loaded: {wallpaperPath}");
                }

                var background = new GameObject(InteractiveWallpaperBootstrap.ShowcaseMode
                    ? "Synthetic Showcase Backdrop"
                    : "Windows Desktop Wallpaper");
                DontDestroyOnLoad(background);
                background.transform.position = new Vector3(0f, 0f, 4f);
                _renderer = background.AddComponent<SpriteRenderer>();
                _renderer.sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                _renderer.sortingOrder = -1000;
                ScaleToCamera();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load desktop wallpaper: {exception.Message}");
            }
        }

        private static Texture2D CreateShowcaseBackdrop()
        {
            const int width = 1024;
            const int height = 576;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Procedural FX showcase backdrop",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                var v = y / (float)(height - 1);
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1);
                    var baseColor = Color.Lerp(new Color(0.025f, 0.055f, 0.1f), new Color(0.075f, 0.15f, 0.22f), v);
                    var leftGlow = Mathf.Exp(-((u - 0.19f) * (u - 0.19f) * 11f + (v - 0.3f) * (v - 0.3f) * 9f));
                    var rightGlow = Mathf.Exp(-((u - 0.82f) * (u - 0.82f) * 14f + (v - 0.62f) * (v - 0.62f) * 11f));
                    var fineGrid = (Mathf.Abs(Mathf.Repeat(u * 20f, 1f) - 0.5f) > 0.49f ||
                        Mathf.Abs(Mathf.Repeat(v * 12f, 1f) - 0.5f) > 0.49f) ? 0.018f : 0f;
                    var color = baseColor + new Color(0.018f, 0.12f, 0.15f) * leftGlow +
                        new Color(0.1f, 0.065f, 0.025f) * rightGlow + Color.white * fineGrid;
                    pixels[y * width + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void Update()
        {
            if (_renderer != null &&
                (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight))
            {
                ScaleToCamera();
            }
        }

        private void ScaleToCamera()
        {
            var camera = Camera.main;
            var sprite = _renderer?.sprite;
            if (camera == null || sprite == null || !camera.orthographic)
            {
                return;
            }

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            var worldHeight = camera.orthographicSize * 2f;
            var worldWidth = worldHeight * Math.Max(0.1f, camera.aspect);
            var spriteSize = sprite.bounds.size;
            var scale = Mathf.Max(
                worldWidth / Mathf.Max(0.001f, spriteSize.x),
                worldHeight / Mathf.Max(0.001f, spriteSize.y));
            _renderer!.transform.position = new Vector3(
                camera.transform.position.x,
                camera.transform.position.y,
                4f);
            _renderer.transform.rotation = Quaternion.identity;
            _renderer.transform.localScale = Vector3.one * scale;
        }

        private static string GetWallpaperPath()
        {
            var buffer = new StringBuilder(32768);
            return SystemParametersInfoW(
                GetDesktopWallpaper,
                (uint)buffer.Capacity,
                buffer,
                0)
                ? buffer.ToString()
                : string.Empty;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfoW(
            uint action,
            uint parameter,
            StringBuilder value,
            uint update);
    }
}