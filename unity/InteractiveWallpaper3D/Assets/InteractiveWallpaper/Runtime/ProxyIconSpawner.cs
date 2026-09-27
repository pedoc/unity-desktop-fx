#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class ProxyIconSpawner : MonoBehaviour
    {
        private const float WorldHeight = 10f;
        private const int MaximumPreviewItems = 200;
        private static Font? s_labelFont;
        private static readonly Dictionary<int, Sprite> ShowcaseIcons = new Dictionary<int, Sprite>();
        private GameObject? _root;
        private Coroutine? _buildRoutine;

        private void Start()
        {
            var snapshot = InteractiveWallpaperBootstrap.Instance?.Snapshot;
            if (snapshot != null)
            {
                Rebuild(snapshot);
            }
        }

        public void Rebuild(DesktopSnapshot snapshot, Action? completed = null)
        {
            if (_buildRoutine != null)
            {
                StopCoroutine(_buildRoutine);
                _buildRoutine = null;
            }
            if (_root != null)
            {
                Destroy(_root);
                _root = null;
            }
            _buildRoutine = StartCoroutine(BuildSnapshot(snapshot, completed));
        }

        private IEnumerator BuildSnapshot(DesktopSnapshot snapshot, Action? completed)
        {
            if (snapshot.items == null || snapshot.virtualDesktop == null)
            {
                yield break;
            }

            _root = new GameObject("Desktop Proxy World");
            var worldWidth = (float)(WorldHeight * snapshot.virtualDesktop.width / Math.Max(1.0, snapshot.virtualDesktop.height));
            _root.AddComponent<ProxyIconWorld>().Initialize(worldWidth, WorldHeight);
            var count = Math.Min(snapshot.items.Length, MaximumPreviewItems);
            var desktopHeight = Math.Max(1, snapshot.virtualDesktop.height);
            var explorerIconPixels = snapshot.view?.iconSize > 0 ? snapshot.view.iconSize : 48;
            var cardHeight = Mathf.Clamp(
                (float)(WorldHeight * explorerIconPixels / desktopHeight),
                0.18f,
                InteractiveWallpaperBootstrap.ShowcaseMode ? 0.68f : 0.42f);
            var cardWidth = cardHeight;

            for (var index = 0; index < count; index++)
            {
                var item = snapshot.items[index];
                var point = DesktopCoordinateMath.PixelToWorld(
                    item.position.x,
                    item.position.y,
                    snapshot.virtualDesktop,
                    WorldHeight);

                var halfWidth = cardWidth * 0.5f;
                var halfHeight = cardHeight * 0.5f;
                var labelAllowance = cardHeight * 0.62f;
                var safeX = Mathf.Clamp(
                    (float)point.X,
                    -worldWidth * 0.5f + halfWidth,
                    worldWidth * 0.5f - halfWidth);
                var safeY = Mathf.Clamp(
                    (float)point.Y,
                    -WorldHeight * 0.5f + halfHeight + labelAllowance,
                    WorldHeight * 0.5f - halfHeight);

                var card = new GameObject($"Proxy {item.displayName}");
                card.transform.SetParent(_root.transform, false);
                card.transform.localPosition = new Vector3(safeX, safeY, 0f);

                var iconObject = new GameObject("Icon");
                iconObject.transform.SetParent(card.transform, false);
                var spriteRenderer = iconObject.AddComponent<SpriteRenderer>();
                spriteRenderer.sortingOrder = 10;

                var sprite = LoadBestIcon(item);
                if (sprite != null)
                {
                    spriteRenderer.sprite = sprite;
                    spriteRenderer.color = Color.white;
                    ScaleIconPreservingAspect(iconObject.transform, sprite, cardWidth, cardHeight);
                }
                else
                {
                    spriteRenderer.sprite = Sprite.Create(
                        Texture2D.whiteTexture,
                        new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                        new Vector2(0.5f, 0.5f),
                        1f);
                    spriteRenderer.color = ColorFromStableId(item.stableId);
                    ScaleIconPreservingAspect(iconObject.transform, spriteRenderer.sprite, cardWidth, cardHeight);
                }

                var collider = card.AddComponent<BoxCollider>();
                collider.size = new Vector3(cardWidth, cardHeight, 0.18f);
                collider.center = Vector3.zero;
                var body = card.AddComponent<Rigidbody>();
                body.mass = 0.15f;
                body.linearDamping = 0.7f;
                body.angularDamping = 0.7f;
                card.AddComponent<ProxyIconInteraction>().Initialize(
                    item.stableId,
                    item.displayName,
                    card.transform.localPosition,
                    spriteRenderer);

                CreateLabel(card.transform, item.displayName, cardHeight);

                if ((index + 1) % 16 == 0)
                {
                    yield return null;
                }
            }
            _buildRoutine = null;
            Debug.Log($"Desktop proxy icons ready: {count} items");
            completed?.Invoke();
        }

        private static void ScaleIconPreservingAspect(
            Transform iconTransform,
            Sprite sprite,
            float maximumWidth,
            float maximumHeight)
        {
            var size = sprite.bounds.size;
            var widthScale = size.x > 0.0001f ? maximumWidth / size.x : 1f;
            var heightScale = size.y > 0.0001f ? maximumHeight / size.y : 1f;
            var uniformScale = Mathf.Min(widthScale, heightScale);
            iconTransform.localScale = Vector3.one * uniformScale;
        }

        private static void CreateLabel(Transform parent, string displayName, float cardHeight)
        {
            s_labelFont ??= Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                48);
            var text = FormatLabel(displayName);
            var position = new Vector3(0f, -cardHeight * 0.66f, -0.04f);

            CreateTextMesh(parent, "Label Shadow", text, position + new Vector3(0.012f, -0.012f, 0.01f), Color.black, 19);
            CreateTextMesh(parent, "Label", text, position, Color.white, 20);
        }

        private static void CreateTextMesh(
            Transform parent,
            string name,
            string text,
            Vector3 localPosition,
            Color color,
            int sortingOrder)
        {
            var labelObject = new GameObject(name);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            var label = labelObject.AddComponent<TextMesh>();
            label.font = s_labelFont;
            label.text = text;
            label.anchor = TextAnchor.UpperCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = InteractiveWallpaperBootstrap.ShowcaseMode ? 0.026f : 0.018f;
            label.lineSpacing = 0.82f;
            label.color = color;
            label.richText = false;
            var renderer = labelObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = s_labelFont!.material;
            renderer.sortingOrder = sortingOrder;
        }

        private static string FormatLabel(string displayName)
        {
            var value = string.IsNullOrWhiteSpace(displayName) ? "未命名" : displayName.Trim();
            const int lineLength = 9;
            const int maximumLength = 18;
            if (value.Length > maximumLength)
            {
                value = value.Substring(0, maximumLength - 1) + "…";
            }
            if (value.Length <= lineLength)
            {
                return value;
            }
            return value.Substring(0, lineLength) + "\n" + value.Substring(lineLength);
        }

        private static Sprite? LoadBestIcon(DesktopItemSnapshot item)
        {
            if (InteractiveWallpaperBootstrap.ShowcaseMode && item.stableId.StartsWith("showcase:", StringComparison.Ordinal))
            {
                return CreateShowcaseIcon(item.stableId);
            }
            DesktopIconAsset? selected = null;
            foreach (var icon in item.icons ?? Array.Empty<DesktopIconAsset>())
            {
                if (string.IsNullOrWhiteSpace(icon.path) || !File.Exists(icon.path))
                {
                    continue;
                }
                if (selected == null || icon.size > selected.size)
                {
                    selected = icon;
                }
            }
            if (selected == null)
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(selected.path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = $"Desktop icon {item.displayName}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                if (!ImageConversion.LoadImage(texture, bytes, true))
                {
                    Destroy(texture);
                    return null;
                }
                return Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load icon for {item.displayName}: {exception.Message}");
                return null;
            }
        }

        private static Sprite CreateShowcaseIcon(string stableId)
        {
            var fields = stableId.Split(':');
            var index = fields.Length > 1 && int.TryParse(fields[1], out var parsed) ? parsed : 0;
            if (ShowcaseIcons.TryGetValue(index, out var existing))
            {
                return existing;
            }

            const int size = 64;
            var palette = new[]
            {
                new Color(0.12f, 0.7f, 0.88f), new Color(0.85f, 0.47f, 0.22f),
                new Color(0.55f, 0.41f, 0.93f), new Color(0.18f, 0.72f, 0.57f),
                new Color(0.18f, 0.56f, 0.95f), new Color(0.85f, 0.33f, 0.57f),
            };
            var tint = palette[index % palette.Length];
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = $"Showcase icon {index}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var px = (x - 31.5f) / 31.5f;
                    var py = (y - 31.5f) / 31.5f;
                    var q = new Vector2(Mathf.Abs(px) - 0.64f, Mathf.Abs(py) - 0.64f);
                    var edge = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude +
                        Mathf.Min(Mathf.Max(q.x, q.y), 0f) - 0.19f;
                    var alpha = Mathf.Clamp01(1f - edge * 50f);
                    var shade = 1.08f - (px + py) * 0.11f;
                    var color = tint * shade;
                    var symbol = (index % 4) switch
                    {
                        0 => Mathf.Abs(new Vector2(px, py).magnitude - 0.3f) < 0.065f,
                        1 => Mathf.Abs(px) + Mathf.Abs(py) < 0.35f,
                        2 => Mathf.Abs(py) < 0.075f && Mathf.Abs(px) < 0.34f ||
                            Mathf.Abs(px) < 0.075f && Mathf.Abs(py) < 0.34f,
                        _ => py > -0.2f && py < 0.24f && Mathf.Abs(px) < 0.3f ||
                            py > 0.16f && py < 0.35f && px > -0.3f && px < 0.08f,
                    };
                    if (symbol)
                    {
                        color = Color.Lerp(color, Color.white, 0.88f);
                    }
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            ShowcaseIcons[index] = sprite;
            return sprite;
        }

        private static Color ColorFromStableId(string stableId)
        {
            unchecked
            {
                var hash = 17;
                foreach (var character in stableId ?? string.Empty)
                {
                    hash = hash * 31 + character;
                }
                var hue = (hash & 0xFFFF) / 65535f;
                return Color.HSVToRGB(hue, 0.58f, 0.95f);
            }
        }
    }
}