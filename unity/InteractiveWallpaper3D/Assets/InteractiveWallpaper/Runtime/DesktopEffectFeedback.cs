#nullable enable

using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class DesktopEffectFeedback : MonoBehaviour
    {
        private sealed class Particle
        {
            public Transform Transform = null!;
            public SpriteRenderer Renderer = null!;
            public Vector3 Velocity;
            public float AngularVelocity;
            public float Age;
            public float Lifetime;
            public Color Color;
        }

        private const int MaximumConcurrentEffects = 3;
        private static Sprite? s_circleSprite;
        private static int s_activeEffectCount;
        private bool _countedAsActive;
        private Particle[] _particles = System.Array.Empty<Particle>();

        public static void EmitBurst(
            Vector3 position,
            Color color,
            int particleCount = 20,
            float speed = 2.4f)
        {
            if (s_activeEffectCount >= MaximumConcurrentEffects)
            {
                return;
            }
            var effect = new GameObject("Desktop Effect Burst");
            effect.transform.position = position;
            var feedback = effect.AddComponent<DesktopEffectFeedback>();
            feedback._countedAsActive = true;
            s_activeEffectCount++;
            feedback.Initialize(color, Mathf.Clamp(particleCount, 6, 64), speed);
        }

        private void Initialize(Color color, int particleCount, float speed)
        {
            s_circleSprite ??= CreateCircleSprite();
            _particles = new Particle[particleCount];
            for (var index = 0; index < particleCount; index++)
            {
                var angle = Mathf.PI * 2f * index / particleCount + Random.Range(-0.16f, 0.16f);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var particleObject = new GameObject($"Particle {index + 1}");
                particleObject.transform.SetParent(transform, false);
                particleObject.transform.localPosition = direction * Random.Range(0.02f, 0.16f);
                particleObject.transform.localScale = Vector3.one * Random.Range(0.04f, 0.09f);
                var renderer = particleObject.AddComponent<SpriteRenderer>();
                renderer.sprite = s_circleSprite;
                renderer.color = color;
                renderer.sortingOrder = 100;
                _particles[index] = new Particle
                {
                    Transform = particleObject.transform,
                    Renderer = renderer,
                    Velocity = direction * Random.Range(speed * 0.65f, speed * 1.15f),
                    AngularVelocity = Random.Range(-360f, 360f),
                    Lifetime = Random.Range(0.45f, 0.8f),
                    Color = color,
                };
            }
        }

        private void Update()
        {
            var allFinished = true;
            foreach (var particle in _particles)
            {
                if (particle.Age >= particle.Lifetime)
                {
                    continue;
                }
                allFinished = false;
                particle.Age += Time.deltaTime;
                particle.Velocity += Vector3.down * (2.2f * Time.deltaTime);
                particle.Transform.localPosition += particle.Velocity * Time.deltaTime;
                particle.Transform.Rotate(0f, 0f, particle.AngularVelocity * Time.deltaTime);
                var progress = Mathf.Clamp01(particle.Age / particle.Lifetime);
                particle.Transform.localScale *= 1f - Time.deltaTime * 0.9f;
                particle.Renderer.color = new Color(
                    particle.Color.r,
                    particle.Color.g,
                    particle.Color.b,
                    1f - progress);
            }
            if (allFinished)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_countedAsActive)
            {
                s_activeEffectCount = Mathf.Max(0, s_activeEffectCount - 1);
                _countedAsActive = false;
            }
        }
        private static Sprite CreateCircleSprite()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = "Desktop effect circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((center - distance) / 2f) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
        }
    }
}