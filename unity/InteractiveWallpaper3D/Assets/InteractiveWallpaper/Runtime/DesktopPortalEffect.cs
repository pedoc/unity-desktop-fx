#nullable enable

using System.Collections;
using UnityEngine;

namespace InteractiveWallpaper
{
    /// <summary>Short, opt-in portal gag. It only animates a Unity proxy; Shell icons are untouched.</summary>
    public sealed class DesktopPortalEffect : MonoBehaviour
    {
        private sealed class PortalVisual
        {
            public Transform Root = null!;
            public Transform OuterRing = null!;
            public Transform InnerRing = null!;
            public SpriteRenderer Aura = null!;
            public SpriteRenderer Disk = null!;
            public SpriteRenderer Outer = null!;
            public SpriteRenderer Inner = null!;
            public Vector3 Position;
            public Color Tint;
        }

        private const float OpenSeconds = 0.42f;
        private const float EnterSeconds = 0.7f;
        private const float EmergeSeconds = 0.38f;
        private const float ReturnSeconds = 1.15f;
        private static Sprite? s_ringSprite;
        private static Sprite? s_diskSprite;

        private ProxyIconInteraction? _icon;
        private PortalVisual? _entrance;
        private PortalVisual? _exit;
        private Vector3 _iconStart;
        private Quaternion _iconRotation;
        private bool _holding;

        public bool Begin(ProxyIconInteraction icon, Vector3 entrance, float worldWidth, float worldHeight)
        {
            _icon = icon;
            if (!_icon.BeginPortalHold())
            {
                return false;
            }

            _holding = true;
            _iconStart = icon.WorldPosition;
            _iconRotation = icon.transform.rotation;
            entrance.z = _iconStart.z;
            var exit = ChooseExitPosition(entrance, worldWidth, worldHeight);
            _entrance = CreatePortal("Entrance Portal", entrance, new Color(0.21f, 0.92f, 1f, 1f));
            _exit = CreatePortal("Exit Portal", exit, new Color(0.82f, 0.36f, 1f, 1f));
            StartCoroutine(Run());
            return true;
        }

        private IEnumerator Run()
        {
            try
            {
                yield return AnimatePortals(OpenSeconds, 0f, 1f);
                DesktopEffectFeedback.EmitBurst(_entrance!.Position, _entrance.Tint, 18, 1.7f);

                var enterElapsed = 0f;
                while (enterElapsed < EnterSeconds)
                {
                    enterElapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(enterElapsed / EnterSeconds);
                    var eased = EaseInCubic(t);
                    var position = Vector3.Lerp(_iconStart, _entrance.Position, eased);
                    position.z = Mathf.Lerp(_iconStart.z, _iconStart.z - 0.32f, eased);
                    var twist = Mathf.Sin(t * Mathf.PI * 5f) * 16f;
                    _icon!.SetPortalVisual(
                        position,
                        _iconRotation * Quaternion.Euler(0f, 0f, twist),
                        Mathf.Lerp(1f, 0.04f, eased),
                        t < 0.88f);
                    AnimatePortalRings(enterElapsed);
                    yield return null;
                }

                _icon!.SetPortalVisual(_entrance!.Position, _iconRotation, 0.001f, false);
                yield return new WaitForSecondsRealtime(0.24f);

                _icon.SetPortalVisual(_exit!.Position, _iconRotation, 0.05f, true);
                DesktopEffectFeedback.EmitBurst(_exit.Position, _exit.Tint, 22, 2.0f);
                var emergeElapsed = 0f;
                while (emergeElapsed < EmergeSeconds)
                {
                    emergeElapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(emergeElapsed / EmergeSeconds);
                    var bounce = Mathf.Sin(t * Mathf.PI * 0.5f);
                    var position = _exit.Position + Vector3.up * (0.12f * (1f - t));
                    _icon.SetPortalVisual(
                        position,
                        _iconRotation * Quaternion.Euler(0f, 0f, (1f - t) * 18f),
                        Mathf.Lerp(0.05f, 1f, bounce),
                        true);
                    AnimatePortalRings(emergeElapsed + EnterSeconds);
                    yield return null;
                }

                var returnElapsed = 0f;
                while (returnElapsed < ReturnSeconds)
                {
                    returnElapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(returnElapsed / ReturnSeconds);
                    var eased = t * t * (3f - 2f * t);
                    var position = Vector3.Lerp(_exit.Position, _iconStart, eased);
                    position.y += Mathf.Sin(t * Mathf.PI) * Mathf.Min(1.15f, Mathf.Abs(_iconStart.x - _exit.Position.x) * 0.16f + 0.4f);
                    position.z = Mathf.Lerp(_exit.Position.z - 0.2f, _iconStart.z, eased);
                    var wobble = Mathf.Sin(t * Mathf.PI * 4f) * (1f - t) * 12f;
                    var bounceScale = t > 0.78f
                        ? 1f + Mathf.Sin((t - 0.78f) / 0.22f * Mathf.PI * 2f) * 0.13f * (1f - t)
                        : 1f;
                    _icon.SetPortalVisual(
                        position,
                        _iconRotation * Quaternion.Euler(0f, 0f, wobble),
                        bounceScale,
                        true);
                    AnimatePortalRings(returnElapsed + 2f);
                    yield return null;
                }

                DesktopEffectFeedback.EmitBurst(_iconStart, new Color(0.55f, 0.92f, 1f, 1f), 12, 1.15f);
                yield return AnimatePortals(0.24f, 1f, 0f);
            }
            finally
            {
                RestoreIcon();
                Destroy(gameObject);
            }
        }

        private IEnumerator AnimatePortals(float duration, float from, float to)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var value = Mathf.Lerp(from, to, t);
                SetPortalScale(_entrance!, value);
                SetPortalScale(_exit!, value);
                AnimatePortalRings(elapsed);
                yield return null;
            }
            SetPortalScale(_entrance!, to);
            SetPortalScale(_exit!, to);
        }

        private void AnimatePortalRings(float time)
        {
            AnimatePortal(_entrance!, time, 1f);
            AnimatePortal(_exit!, time, -1f);
        }

        private static void AnimatePortal(PortalVisual portal, float time, float direction)
        {
            var pulse = 1f + Mathf.Sin(time * 5.2f) * 0.055f;
            portal.Root.localScale = Vector3.one * pulse;
            portal.OuterRing.Rotate(0f, 0f, direction * 52f * Time.unscaledDeltaTime);
            portal.InnerRing.Rotate(0f, 0f, -direction * 83f * Time.unscaledDeltaTime);
            var shimmer = 0.82f + Mathf.Sin(time * 8f) * 0.12f;
            portal.Aura.color = new Color(portal.Tint.r, portal.Tint.g, portal.Tint.b, 0.44f * shimmer);
            portal.Disk.color = new Color(0.055f, 0.025f, 0.13f, 0.79f * shimmer);
            portal.Outer.color = new Color(portal.Tint.r, portal.Tint.g, portal.Tint.b, 0.94f * shimmer);
            portal.Inner.color = new Color(0.78f, 0.98f, 1f, 0.88f * shimmer);
        }

        private static void SetPortalScale(PortalVisual portal, float value)
        {
            var scale = Mathf.Max(0.001f, value);
            portal.Root.localScale = Vector3.one * (0.62f * scale);
            var tint = portal.Tint;
            portal.Aura.color = new Color(tint.r, tint.g, tint.b, 0.44f * scale);
            portal.Disk.color = new Color(0.055f, 0.025f, 0.13f, 0.79f * scale);
            portal.Outer.color = new Color(tint.r, tint.g, tint.b, 0.94f * scale);
            portal.Inner.color = new Color(0.78f, 0.98f, 1f, 0.88f * scale);
        }

        private PortalVisual CreatePortal(string name, Vector3 position, Color tint)
        {
            s_ringSprite ??= CreateRingSprite();
            s_diskSprite ??= CreateDiskSprite();
            var rootObject = new GameObject(name);
            rootObject.transform.SetParent(transform, false);
            rootObject.transform.position = position + new Vector3(0f, 0f, -0.65f);
            var visual = new PortalVisual
            {
                Root = rootObject.transform,
                Position = position,
                Tint = tint,
                Aura = CreateSprite(rootObject.transform, "Aura", s_diskSprite, new Color(tint.r, tint.g, tint.b, 0f), 1080, Vector3.one * 1.62f),
                Disk = CreateSprite(rootObject.transform, "Event Horizon", s_diskSprite, new Color(0.055f, 0.025f, 0.13f, 0f), 1081, Vector3.one * 0.92f),
                Outer = CreateSprite(rootObject.transform, "Energy Ring", s_ringSprite, new Color(tint.r, tint.g, tint.b, 0f), 1082, Vector3.one * 1.42f),
                Inner = CreateSprite(rootObject.transform, "Inner Ring", s_ringSprite, new Color(0.78f, 0.98f, 1f, 0f), 1083, Vector3.one * 1.02f),
            };
            visual.OuterRing = visual.Outer.transform;
            visual.InnerRing = visual.Inner.transform;
            return visual;
        }

        private static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite, Color color, int order, Vector3 scale)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localScale = scale;
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static Sprite CreateRingSprite()
        {
            const int size = 128;
            var texture = CreateTexture(size, "Desktop portal energy ring");
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f - 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var normalized = distance / radius;
                    var ring = 1f - Mathf.SmoothStep(0.035f, 0.095f, Mathf.Abs(normalized - 0.76f));
                    var halo = Mathf.Clamp01(1f - Mathf.Abs(normalized - 0.76f) / 0.25f) * 0.28f;
                    var alpha = Mathf.Clamp01(ring + halo) * Mathf.Clamp01((1f - normalized) * 9f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite CreateDiskSprite()
        {
            const int size = 128;
            var texture = CreateTexture(size, "Desktop portal glow");
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f - 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / radius;
                    var alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.62f, 1f, distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Texture2D CreateTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        private static Vector3 ChooseExitPosition(Vector3 entrance, float width, float height)
        {
            var minimumDistance = Mathf.Min(width, height) * 0.3f;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var candidate = new Vector3(
                    Random.Range(-width * 0.42f, width * 0.42f),
                    Random.Range(-height * 0.4f, height * 0.4f),
                    entrance.z);
                if (Vector2.Distance(candidate, entrance) >= minimumDistance)
                {
                    return candidate;
                }
            }
            return new Vector3(-entrance.x * 0.72f, entrance.y, entrance.z);
        }

        private static float EaseInCubic(float value) => value * value * value;

        private void RestoreIcon()
        {
            if (_holding && _icon != null)
            {
                _icon.CompletePortalHold();
                _holding = false;
            }
        }

        private void OnDestroy()
        {
            RestoreIcon();
        }
    }
}
