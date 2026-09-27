#nullable enable

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InteractiveWallpaper
{
    /// <summary>A broad, turbulent wind field carrying modeled leaves, petals and air tracers.</summary>
    public sealed class DesktopWeatherEffect : MonoBehaviour
    {
        private sealed class WindIcon
        {
            public ProxyIconInteraction Icon = null!;
            public bool InitialImpulseApplied;
            public bool Affected;
        }

        private sealed class FoliageParticle
        {
            public Transform Transform = null!;
            public MeshRenderer Renderer = null!;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public float Age;
            public float Lifetime;
            public bool IsPetal;
        }

        private sealed class AirTracer
        {
            public Transform Transform = null!;
            public SpriteRenderer Renderer = null!;
            public Vector3 Velocity;
            public float Age;
            public float Lifetime;
            public float BaseAlpha;
            public bool IsCloud;
        }

        private sealed class StarParticle
        {
            public Transform Transform = null!;
            public SpriteRenderer Renderer = null!;
            public Vector3 Velocity;
            public float Age;
            public float Lifetime;
            public float BaseAlpha;
            public float PulseRate;
            public float Phase;
            public float Spin;
            public Vector3 BaseScale;
            public Color Tint;
        }

        private struct WindSample
        {
            public Vector3 Velocity;
            public float Strength;
        }

        private const int MaximumFoliageParticles = 320;
        private const int CloudCount = 40;
        private const int MoteCount = 104;
        private const int StarCount = 80;
        private static Mesh? s_leafMesh;
        private static Mesh? s_petalMesh;
        private static Sprite? s_softCloudSprite;
        private static Sprite? s_moteSprite;
        private static Sprite? s_starSprite;

        private readonly List<WindIcon> _icons = new List<WindIcon>();
        private readonly List<FoliageParticle> _foliage = new List<FoliageParticle>();
        private readonly List<AirTracer> _clouds = new List<AirTracer>();
        private readonly List<AirTracer> _motes = new List<AirTracer>();
        private readonly List<StarParticle> _stars = new List<StarParticle>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Material> _leafMaterials = new List<Material>();
        private readonly List<Material> _leafVeinMaterials = new List<Material>();
        private readonly List<Material> _petalMaterials = new List<Material>();
        private readonly List<Material> _petalVeinMaterials = new List<Material>();
        private ProxyIconWorld? _world;
        private int _affectedIconCount;
        private bool _releasedAffectedIcons;
        private float _worldHalfWidth;
        private Vector2 _windDirection;
        private Vector2 _windPerpendicular;
        private float _crosswindCenter;
        private float _crosswindHalfSpan;
        private float _windStartDistance;
        private float _windEndDistance;
        private float _frontShear;
        private float _travelSeconds;
        private float _foliageSpawnTimer;
        private float _cloudSpawnTimer;
        private float _moteSpawnTimer;
        private float _starSpawnTimer;
        private int _nextFoliage;
        private int _nextCloud;
        private int _nextMote;
        private int _nextStar;

        public int TargetIconCount => _icons.Count;

        public bool Begin(ProxyIconWorld world, float worldWidth, float worldHeight)
        {
            _world = world;
            _worldHalfWidth = worldWidth * 0.5f;
            var angle = Random.Range(0f, Mathf.PI * 2f);
            _windDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
            _windPerpendicular = new Vector2(-_windDirection.y, _windDirection.x);
            var projectedHalfDepth = Mathf.Abs(_windDirection.x) * _worldHalfWidth +
                Mathf.Abs(_windDirection.y) * worldHeight * 0.5f;
            _windStartDistance = -projectedHalfDepth - 1.1f;
            _windEndDistance = projectedHalfDepth + 1.1f;
            _crosswindHalfSpan = Mathf.Abs(_windPerpendicular.x) * _worldHalfWidth +
                Mathf.Abs(_windPerpendicular.y) * worldHeight * 0.5f + 0.25f;
            _crosswindCenter = Random.Range(-_crosswindHalfSpan * 0.06f, _crosswindHalfSpan * 0.06f);
            _frontShear = Random.Range(-0.32f, 0.32f);
            _travelSeconds = Mathf.Clamp((_windEndDistance - _windStartDistance + 2.2f) / 5.8f, 2.3f, 5.5f);

            CollectIcons(world);
            BuildAirTracerPools();
            BuildFoliagePool();
            BuildStarPool();
            StartCoroutine(Play());
            return true;
        }

        private void CollectIcons(ProxyIconWorld world)
        {
            var candidates = new List<ProxyIconInteraction>();
            foreach (var icon in world.Icons)
            {
                if (icon.CanCharacterGrab && !icon.IsCatStolen)
                {
                    candidates.Add(icon);
                }
            }
            candidates.Sort((left, right) =>
            {
                var leftCross = Vector2.Dot(left.WorldPosition, _windPerpendicular);
                var rightCross = Vector2.Dot(right.WorldPosition, _windPerpendicular);
                return Mathf.Abs(leftCross - _crosswindCenter).CompareTo(Mathf.Abs(rightCross - _crosswindCenter));
            });

            foreach (var icon in candidates)
            {
                _icons.Add(new WindIcon { Icon = icon });
            }
        }

        private void BuildAirTracerPools()
        {
            s_softCloudSprite ??= CreateSoftSprite("Turbulent wind cloud", 64, true);
            s_moteSprite ??= CreateSoftSprite("Airborne wind tracers", 32, false);
            CreateAirTracerPool(CloudCount, true);
            CreateAirTracerPool(MoteCount, false);
        }

        private void CreateAirTracerPool(int count, bool isCloud)
        {
            var destination = isCloud ? _clouds : _motes;
            var sprite = isCloud ? s_softCloudSprite : s_moteSprite;
            var sortingOrder = isCloud ? 1150 : 1170;
            for (var index = 0; index < count; index++)
            {
                var tracerObject = new GameObject(isCloud ? $"Soft Wind Volume {index + 1}" : $"Airflow Tracer {index + 1}");
                tracerObject.transform.SetParent(transform, false);
                var renderer = tracerObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = sortingOrder;
                renderer.enabled = false;
                destination.Add(new AirTracer
                {
                    Transform = tracerObject.transform,
                    Renderer = renderer,
                    IsCloud = isCloud,
                });
            }
        }

        private void BuildStarPool()
        {
            s_starSprite ??= CreateStarSprite();
            for (var index = 0; index < StarCount; index++)
            {
                var starObject = new GameObject($"Wind Sparkle {index + 1}");
                starObject.transform.SetParent(transform, false);
                var renderer = starObject.AddComponent<SpriteRenderer>();
                renderer.sprite = s_starSprite;
                renderer.sortingOrder = 1185;
                renderer.enabled = false;
                _stars.Add(new StarParticle { Transform = starObject.transform, Renderer = renderer });
            }
        }

        private static Sprite CreateStarSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Four-point wind sparkle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radius = center - 1f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = (x - center) / radius;
                    var ny = (y - center) / radius;
                    var distance = Mathf.Sqrt(nx * nx + ny * ny);
                    var angle = Mathf.Atan2(ny, nx);
                    var fourPointEdge = 0.16f + 0.78f * Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 5f);
                    var core = 1f - SmoothThreshold(fourPointEdge - 0.16f, fourPointEdge, distance);
                    var glow = Mathf.Exp(-distance * distance * 8f) * 0.22f;
                    var alpha = Mathf.Clamp01(core + glow * (1f - core));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite CreateSoftSprite(string name, int size, bool cloudy)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radius = center - 1f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = (x - center) / radius;
                    var ny = (y - center) / radius;
                    var angle = Mathf.Atan2(ny, nx);
                    var distance = Mathf.Sqrt(nx * nx + ny * ny);
                    var noise = Mathf.Sin(angle * 5.1f + distance * 9f) * 0.035f +
                        Mathf.Sin(angle * 8.7f - distance * 13f) * 0.018f +
                        Mathf.Cos(nx * 11f + ny * 7f) * 0.018f;
                    var edge = cloudy ? 0.84f + noise : 0.72f;
                    var alpha = 1f - SmoothThreshold(edge - (cloudy ? 0.18f : 0.28f), edge, distance);
                    if (cloudy)
                    {
                        var inner = 0.68f + 0.22f * Mathf.Sin(nx * 7f + ny * 5f) * Mathf.Cos(ny * 8f - nx * 3f);
                        alpha *= Mathf.Clamp01(inner) * 0.75f;
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void BuildFoliagePool()
        {
            s_leafMesh ??= CreateFoliageMesh(false);
            s_petalMesh ??= CreateFoliageMesh(true);
            CreateMaterials(
                new[]
                {
                    new Color(0.52f, 0.145f, 0.025f),
                    new Color(0.38f, 0.038f, 0.022f),
                    new Color(0.62f, 0.31f, 0.045f),
                    new Color(0.34f, 0.075f, 0.022f),
                    new Color(0.46f, 0.12f, 0.035f),
                    new Color(0.58f, 0.39f, 0.075f),
                },
                _leafMaterials,
                _leafVeinMaterials,
                "Autumn Leaf");
            CreateMaterials(
                new[]
                {
                    new Color(0.84f, 0.18f, 0.31f),
                    new Color(0.67f, 0.055f, 0.2f),
                    new Color(0.94f, 0.36f, 0.42f),
                    new Color(0.88f, 0.54f, 0.48f),
                    new Color(0.69f, 0.29f, 0.49f),
                },
                _petalMaterials,
                _petalVeinMaterials,
                "Windblown Petal");

            for (var index = 0; index < MaximumFoliageParticles; index++)
            {
                var particleObject = new GameObject($"3D Windblown Foliage {index + 1}");
                particleObject.transform.SetParent(transform, false);
                particleObject.AddComponent<MeshFilter>();
                var renderer = particleObject.AddComponent<MeshRenderer>();
                renderer.sortingOrder = 1200;
                renderer.enabled = false;
                _foliage.Add(new FoliageParticle { Transform = particleObject.transform, Renderer = renderer });
            }
        }

        private void CreateMaterials(Color[] colors, List<Material> bodyMaterials, List<Material> veinMaterials, string prefix)
        {
            foreach (var color in colors)
            {
                bodyMaterials.Add(CreateFoliageMaterial(prefix, color));
                var veinColor = Color.Lerp(color, new Color(0.16f, 0.055f, 0.015f), 0.42f);
                veinMaterials.Add(CreateFoliageMaterial(prefix + " Veins", veinColor));
            }
        }

        private Material CreateFoliageMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = name,
                renderQueue = 3100,
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.24f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.24f);
            _materials.Add(material);
            return material;
        }

        private IEnumerator Play()
        {
            var elapsed = 0f;
            try
            {
                DesktopEffectFeedback.EmitBurst(
                    ToWorld(WindFrontAt(0f, _crosswindCenter), -0.3f),
                    new Color(0.66f, 0.78f, 0.85f),
                    18,
                    2.0f);

                // Leave enough time for the last leaves, petals and air tracers to drift away.
                var totalDuration = _travelSeconds + 4.8f;
                while (elapsed < totalDuration)
                {
                    var delta = Time.unscaledDeltaTime;
                    elapsed += delta;
                    var progress = Mathf.Clamp01(elapsed / _travelSeconds);
                    ApplyWindFieldToIcons(elapsed, progress);
                    if (progress >= 1f && !_releasedAffectedIcons)
                    {
                        ReleaseAffectedIconsToGravity();
                    }
                    UpdateFoliage(delta, elapsed, progress);
                    UpdateAirTracers(delta, elapsed, progress);
                    UpdateStars(delta, elapsed, progress);

                    if (elapsed < _travelSeconds * 0.92f)
                    {
                        _foliageSpawnTimer += delta;
                        _cloudSpawnTimer += delta;
                        _moteSpawnTimer += delta;
                        _starSpawnTimer += delta;
                        while (_foliageSpawnTimer >= 0.015f)
                        {
                            _foliageSpawnTimer -= 0.015f;
                            SpawnFoliage(progress);
                        }
                        while (_cloudSpawnTimer >= 0.12f)
                        {
                            _cloudSpawnTimer -= 0.12f;
                            SpawnAirTracer(true, progress);
                        }
                        while (_moteSpawnTimer >= 0.035f)
                        {
                            _moteSpawnTimer -= 0.035f;
                            SpawnAirTracer(false, progress);
                        }
                        while (_starSpawnTimer >= 0.055f)
                        {
                            _starSpawnTimer -= 0.055f;
                            SpawnStar(progress);
                        }
                    }
                    yield return null;
                }

                DesktopEffectFeedback.EmitBurst(
                    ToWorld(WindFrontAt(1f, _crosswindCenter), -0.3f),
                    new Color(0.74f, 0.39f, 0.13f),
                    14,
                    1.65f);
            }
            finally
            {
                ReleaseAffectedIconsToGravity();
                Destroy(gameObject);
            }
        }

        private void ReleaseAffectedIconsToGravity()
        {
            if (_releasedAffectedIcons)
            {
                return;
            }
            _releasedAffectedIcons = true;
            foreach (var slot in _icons)
            {
                if (slot.Affected && slot.Icon != null)
                {
                    slot.Icon.EndWindPhysics();
                }
            }
        }

        private void ApplyWindFieldToIcons(float time, float progress)
        {
            foreach (var slot in _icons)
            {
                if (slot.Icon == null || !slot.Icon.CanCharacterGrab)
                {
                    continue;
                }
                var sample = SampleWind(slot.Icon.WorldPosition, time, progress);
                if (sample.Strength < 0.035f)
                {
                    continue;
                }

                if (!slot.InitialImpulseApplied)
                {
                    var impulse = sample.Velocity.normalized * Random.Range(0.24f, 0.36f) * sample.Strength;
                    var torque = Random.Range(0.004f, 0.009f) * (Random.value < 0.5f ? -1f : 1f);
                    if (slot.Icon.ApplyWindImpulse(impulse, torque))
                    {
                        slot.Affected = true;
                        _affectedIconCount++;
                        _world?.ReportWindFieldHitCount(_affectedIconCount);
                    }
                    slot.InitialImpulseApplied = true;
                }

                var gustAcceleration = sample.Velocity * 0.66f;
                var twist = Mathf.Sin(slot.Icon.WorldPosition.x * 1.4f + slot.Icon.WorldPosition.y * 2.1f + time * 3f) * sample.Strength * 0.45f;
                slot.Icon.ApplyWindAcceleration(gustAcceleration, twist);
            }
        }

        private static float SmoothThreshold(float lower, float upper, float value)
        {
            var t = Mathf.Clamp01((value - lower) / Mathf.Max(0.0001f, upper - lower));
            return t * t * (3f - 2f * t);
        }

        private Vector2 WindFrontAt(float progress, float crosswind)
        {
            var along = Mathf.Lerp(_windStartDistance, _windEndDistance, progress) +
                (crosswind - _crosswindCenter) * _frontShear;
            return _windDirection * along + _windPerpendicular * crosswind;
        }

        private static Vector3 ToWorld(Vector2 point, float z) => new Vector3(point.x, point.y, z);

        private WindSample SampleWind(Vector3 position, float time, float progress)
        {
            var screenPosition = new Vector2(position.x, position.y);
            var longitudinal = Vector2.Dot(screenPosition, _windDirection);
            var crosswind = Vector2.Dot(screenPosition, _windPerpendicular);
            var frontAlong = Mathf.Lerp(_windStartDistance, _windEndDistance, progress) +
                (crosswind - _crosswindCenter) * _frontShear;
            var behindFront = frontAlong - longitudinal;
            var frontBand = Mathf.Exp(-0.5f * Mathf.Pow(behindFront / 1.3f, 2f));
            var wakeBand = behindFront > 0f
                ? 0.78f * Mathf.Exp(-behindFront / 5.0f)
                : 0f;
            var prefrontBand = 0.16f * Mathf.Exp(-0.5f * Mathf.Pow((behindFront + 1.25f) / 1.15f, 2f));
            var longitudinalProfile = Mathf.Max(frontBand, Mathf.Max(wakeBand, prefrontBand));
            var crosswindNorm = (crosswind - _crosswindCenter) / _crosswindHalfSpan;
            var crosswindProfile = 1f - SmoothThreshold(1.02f, 1.22f, Mathf.Abs(crosswindNorm));
            var startFade = SmoothThreshold(0f, 0.055f, progress);
            var endFade = 1f - SmoothThreshold(0.91f, 1f, progress);
            var strength = Mathf.Clamp01(longitudinalProfile * crosswindProfile * startFade * endFade);

            var phaseA = longitudinal * 0.78f + crosswind * 1.34f - time * 2.05f;
            var phaseB = longitudinal * 0.47f - crosswind * 1.12f + time * 1.53f;
            var speedNoise = Mathf.Sin(phaseA * 0.82f) * 0.72f + Mathf.Cos(phaseB) * 0.38f;
            var curl = Mathf.Sin(phaseA * 0.34f + time * 0.48f) * 0.55f +
                Mathf.Cos(phaseB * 0.29f - time * 0.37f) * 0.3f +
                Mathf.Sin(phaseA * 0.71f - phaseB * 0.24f) * 0.18f;
            var flowDirection = _windDirection * Mathf.Cos(curl) + _windPerpendicular * Mathf.Sin(curl);
            var targetVelocity = flowDirection * (4.4f + speedNoise * 0.7f);
            return new WindSample
            {
                Velocity = targetVelocity * strength,
                Strength = strength,
            };
        }

        private void SpawnAirTracer(bool cloud, float progress)
        {
            var pool = cloud ? _clouds : _motes;
            if (pool.Count == 0)
            {
                return;
            }
            var index = cloud ? _nextCloud++ % pool.Count : _nextMote++ % pool.Count;
            var tracer = pool[index];
            var crosswind = _crosswindCenter + Random.Range(-_crosswindHalfSpan, _crosswindHalfSpan);
            var frontPoint = WindFrontAt(progress, crosswind) -
                _windDirection * Random.Range(0.3f, 4.0f);
            tracer.Transform.position = ToWorld(frontPoint, cloud ? Random.Range(-0.54f, -0.48f) : Random.Range(-0.6f, -0.52f));
            tracer.Transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-180f, 180f));
            if (cloud)
            {
                var width = Random.Range(0.8f, 1.9f);
                tracer.Transform.localScale = new Vector3(width, width * Random.Range(0.48f, 1.0f), 1f);
                tracer.BaseAlpha = Random.Range(0.045f, 0.12f);
                tracer.Lifetime = Random.Range(2.6f, 4.0f);
                tracer.Velocity = ToWorld(_windDirection * Random.Range(1.1f, 2.4f) + _windPerpendicular * Random.Range(-0.35f, 0.35f), 0f);
                tracer.Renderer.color = new Color(0.76f, 0.83f, 0.87f, tracer.BaseAlpha);
            }
            else
            {
                var size = Random.Range(0.025f, 0.075f);
                tracer.Transform.localScale = Vector3.one * size;
                tracer.BaseAlpha = Random.Range(0.2f, 0.48f);
                tracer.Lifetime = Random.Range(2.4f, 3.8f);
                tracer.Velocity = ToWorld(_windDirection * Random.Range(1.8f, 3.8f) + _windPerpendicular * Random.Range(-0.6f, 0.65f), 0f);
                tracer.Renderer.color = new Color(0.82f, 0.88f, 0.9f, tracer.BaseAlpha);
            }
            tracer.Age = 0f;
            tracer.Renderer.enabled = true;
        }

        private void UpdateAirTracers(float delta, float time, float progress)
        {
            UpdateAirTracerList(_clouds, delta, time, progress);
            UpdateAirTracerList(_motes, delta, time, progress);
        }

        private void UpdateAirTracerList(List<AirTracer> tracers, float delta, float time, float progress)
        {
            foreach (var tracer in tracers)
            {
                if (!tracer.Renderer.enabled)
                {
                    continue;
                }
                tracer.Age += delta;
                var sample = SampleWind(tracer.Transform.position, time, progress);
                var drag = tracer.IsCloud ? 1.5f : 2.5f;
                if (sample.Strength > 0.01f)
                {
                    tracer.Velocity += (sample.Velocity - tracer.Velocity) * Mathf.Clamp01(drag * sample.Strength * delta);
                }
                else
                {
                    tracer.Velocity *= Mathf.Max(0f, 1f - 0.28f * delta);
                }
                tracer.Transform.position += tracer.Velocity * delta;
                if (tracer.IsCloud)
                {
                    var sway = Mathf.Sin(tracer.Age * 2.6f + tracer.Transform.position.x) * 9f * delta;
                    tracer.Transform.Rotate(0f, 0f, sway);
                }
                var fadeIn = Mathf.Clamp01(tracer.Age / 0.34f);
                var fadeOut = 1f - SmoothThreshold(0.68f, 1f, tracer.Age / tracer.Lifetime);
                var color = tracer.Renderer.color;
                color.a = tracer.BaseAlpha * fadeIn * fadeOut;
                tracer.Renderer.color = color;
                if (tracer.Age >= tracer.Lifetime)
                {
                    tracer.Renderer.enabled = false;
                }
            }
        }

        private void SpawnStar(float progress)
        {
            if (_stars.Count == 0)
            {
                return;
            }
            var star = _stars[_nextStar++ % _stars.Count];
            var crosswind = _crosswindCenter + Random.Range(-_crosswindHalfSpan, _crosswindHalfSpan);
            var starPosition = WindFrontAt(progress, crosswind) - _windDirection * Random.Range(0.15f, 2.8f);
            star.Transform.position = ToWorld(starPosition, Random.Range(-0.64f, -0.56f));
            star.BaseScale = Vector3.one * Random.Range(0.07f, 0.17f);
            star.Transform.localScale = star.BaseScale;
            star.Transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            star.Velocity = ToWorld(_windDirection * Random.Range(1.2f, 3.2f) + _windPerpendicular * Random.Range(-0.75f, 0.75f), 0f);
            star.Age = 0f;
            star.Lifetime = Random.Range(1.8f, 3.2f);
            star.BaseAlpha = Random.Range(0.65f, 1f);
            star.PulseRate = Random.Range(7f, 13f);
            star.Phase = Random.Range(0f, Mathf.PI * 2f);
            star.Spin = Random.Range(-100f, 100f);
            star.Tint = Random.value < 0.68f
                ? new Color(1f, Random.Range(0.72f, 0.92f), Random.Range(0.38f, 0.7f), 1f)
                : new Color(0.72f, 0.9f, 1f, 1f);
            star.Renderer.color = star.Tint;
            star.Renderer.enabled = true;
        }

        private void UpdateStars(float delta, float time, float progress)
        {
            foreach (var star in _stars)
            {
                if (!star.Renderer.enabled)
                {
                    continue;
                }
                star.Age += delta;
                var sample = SampleWind(star.Transform.position, time, progress);
                if (sample.Strength > 0.01f)
                {
                    star.Velocity += (sample.Velocity - star.Velocity) * Mathf.Clamp01(2.8f * sample.Strength * delta);
                }
                else
                {
                    star.Velocity *= Mathf.Max(0f, 1f - 0.22f * delta);
                }
                star.Transform.position += star.Velocity * delta;
                star.Transform.Rotate(0f, 0f, star.Spin * delta);
                var normalizedAge = star.Age / star.Lifetime;
                var fadeIn = Mathf.Clamp01(star.Age / 0.12f);
                var fadeOut = 1f - SmoothThreshold(0.68f, 1f, normalizedAge);
                var twinkle = 0.36f + 0.64f * (0.5f + 0.5f * Mathf.Sin(star.Age * star.PulseRate + star.Phase));
                var color = star.Tint;
                color.a = star.BaseAlpha * fadeIn * fadeOut * twinkle;
                star.Renderer.color = color;
                star.Transform.localScale = star.BaseScale * (0.76f + twinkle * 0.4f);
                if (normalizedAge >= 1f)
                {
                    star.Renderer.enabled = false;
                }
            }
        }

        private void SpawnFoliage(float progress)
        {
            var particle = _foliage[_nextFoliage];
            _nextFoliage = (_nextFoliage + 1) % _foliage.Count;
            var isPetal = Random.value < 0.4f;
            var bodyMaterials = isPetal ? _petalMaterials : _leafMaterials;
            var veinMaterials = isPetal ? _petalVeinMaterials : _leafVeinMaterials;
            var palette = Random.Range(0, bodyMaterials.Count);
            particle.IsPetal = isPetal;
            particle.Renderer.sharedMaterials = new[] { bodyMaterials[palette], veinMaterials[palette] };
            particle.Renderer.GetComponent<MeshFilter>().sharedMesh = isPetal ? s_petalMesh : s_leafMesh;

            var crosswind = _crosswindCenter + Random.Range(-_crosswindHalfSpan, _crosswindHalfSpan);
            var foliagePosition = WindFrontAt(progress, crosswind) - _windDirection * Random.Range(0.3f, 3.3f);
            particle.Transform.position = ToWorld(foliagePosition, Random.Range(-0.88f, -0.72f));
            var size = isPetal ? Random.Range(0.18f, 0.31f) : Random.Range(0.22f, 0.42f);
            particle.Transform.localScale = new Vector3(size * Random.Range(0.86f, 1.18f), size, size * 0.65f);
            particle.Transform.rotation = Quaternion.Euler(
                Random.Range(-38f, 38f), Random.Range(-48f, 48f), Random.Range(-70f, 70f));
            var foliageSpeed = Random.Range(isPetal ? 2.8f : 3.4f, isPetal ? 4.8f : 5.8f);
            var foliageDrift = _windDirection * foliageSpeed + _windPerpendicular * Random.Range(-0.75f, 0.75f);
            particle.Velocity = new Vector3(foliageDrift.x, foliageDrift.y, Random.Range(-0.2f, 0.2f));
            particle.AngularVelocity = isPetal
                ? new Vector3(Random.Range(-65f, 65f), Random.Range(-85f, 85f), Random.Range(-150f, 150f))
                : new Vector3(Random.Range(-100f, 100f), Random.Range(-125f, 125f), Random.Range(-230f, 230f));
            particle.Age = 0f;
            particle.Lifetime = isPetal ? Random.Range(3.2f, 4.8f) : Random.Range(2.8f, 4.1f);
            particle.Renderer.enabled = true;
        }

        private void UpdateFoliage(float delta, float time, float progress)
        {
            foreach (var particle in _foliage)
            {
                if (!particle.Renderer.enabled)
                {
                    continue;
                }
                particle.Age += delta;
                var position = particle.Transform.position;
                var sample = SampleWind(position, time, progress);
                var drag = particle.IsPetal ? 2.6f : 1.9f;
                if (sample.Strength > 0.01f)
                {
                    particle.Velocity += (sample.Velocity - particle.Velocity) * Mathf.Clamp01(drag * sample.Strength * delta);
                }
                var flutter = Mathf.Sin(particle.Age * (particle.IsPetal ? 7.5f : 10f) + position.x * 1.7f);
                particle.Velocity.y += ((particle.IsPetal ? -0.24f : -0.72f) + flutter * (particle.IsPetal ? 1.0f : 1.55f)) * delta;
                particle.Velocity.z *= Mathf.Max(0f, 1f - 0.4f * delta);
                particle.Transform.position += particle.Velocity * delta;
                particle.Transform.Rotate(particle.AngularVelocity * delta, Space.Self);
                particle.AngularVelocity.x += Mathf.Sin(particle.Age * 6f) * (particle.IsPetal ? 7f : 11f) * delta;
                particle.AngularVelocity.y += Mathf.Cos(particle.Age * 5f) * (particle.IsPetal ? 8f : 13f) * delta;
                if (particle.Age >= particle.Lifetime)
                {
                    particle.Renderer.enabled = false;
                }
            }
        }

        private static Mesh CreateFoliageMesh(bool petal)
        {
            const int stations = 9;
            var vertices = new List<Vector3>(112);
            var bodyTriangles = new List<int>(96);
            var veinTriangles = new List<int>(160);
            var widths = new float[stations];
            var centers = new Vector3[stations];

            for (var index = 0; index < stations; index++)
            {
                var t = index / (float)(stations - 1);
                var x = (t - 0.5f) * (petal ? 0.94f : 1.12f);
                var baseWidth = petal ? 0.215f : 0.255f;
                var exponent = petal ? 0.58f : 0.82f;
                var width = baseWidth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), exponent);
                if (petal)
                {
                    width *= 0.64f + 0.36f * t;
                }
                var centerY = Mathf.Sin(t * Mathf.PI * (petal ? 0.85f : 1.35f)) * (petal ? 0.012f : 0.022f);
                var centerZ = Mathf.Sin(t * Mathf.PI) * (petal ? 0.09f : 0.07f);
                widths[index] = width;
                centers[index] = new Vector3(x, centerY, centerZ);
                vertices.Add(new Vector3(x, centerY - width, centerZ - width * (petal ? 0.025f : 0.055f)));
                vertices.Add(new Vector3(x, centerY, centerZ + 0.012f));
                vertices.Add(new Vector3(x, centerY + width, centerZ - width * (petal ? 0.025f : 0.055f)));
            }

            for (var index = 0; index < stations - 1; index++)
            {
                var left = index * 3;
                var next = left + 3;
                AddQuad(bodyTriangles, left, left + 1, next, next + 1);
                AddQuad(bodyTriangles, left + 1, left + 2, next + 1, next + 2);
            }

            for (var index = 0; index < stations - 1; index++)
            {
                var first = centers[index];
                var second = centers[index + 1];
                var halfWidth0 = Mathf.Lerp(0.007f, 0.014f, widths[index] / (petal ? 0.215f : 0.255f));
                var halfWidth1 = Mathf.Lerp(0.007f, 0.014f, widths[index + 1] / (petal ? 0.215f : 0.255f));
                AddRibbon(vertices, veinTriangles,
                    new Vector3(first.x, first.y - halfWidth0, first.z + 0.022f),
                    new Vector3(first.x, first.y + halfWidth0, first.z + 0.022f),
                    new Vector3(second.x, second.y - halfWidth1, second.z + 0.022f),
                    new Vector3(second.x, second.y + halfWidth1, second.z + 0.022f));
            }

            if (!petal)
            {
                for (var station = 2; station < stations - 1; station++)
                {
                    var center = centers[station];
                    var taper = widths[station] / 0.255f;
                    var branchLength = 0.72f * taper;
                    var nextCenter = centers[station + 1];
                    var previousCenter = centers[station - 1];
                    AddRibbon(vertices, veinTriangles,
                        new Vector3(center.x - 0.003f, center.y, center.z + 0.026f),
                        new Vector3(center.x + 0.003f, center.y, center.z + 0.026f),
                        new Vector3(nextCenter.x, center.y + widths[station] * branchLength, center.z - 0.005f),
                        new Vector3(nextCenter.x, center.y + widths[station] * branchLength + 0.009f, center.z - 0.005f),
                        true);
                    AddRibbon(vertices, veinTriangles,
                        new Vector3(center.x - 0.003f, center.y, center.z + 0.026f),
                        new Vector3(center.x + 0.003f, center.y, center.z + 0.026f),
                        new Vector3(previousCenter.x, center.y - widths[station] * branchLength, center.z - 0.005f),
                        new Vector3(previousCenter.x, center.y - widths[station] * branchLength - 0.009f, center.z - 0.005f),
                        true);
                }
            }

            var mesh = new Mesh { name = petal ? "Curved 3D Flower Petal" : "Curved 3D Autumn Leaf with Veins" };
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bodyTriangles, 0);
            mesh.SetTriangles(veinTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(b); triangles.Add(d); triangles.Add(c);
        }

        private static void AddRibbon(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool reverse = false)
        {
            var index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            if (reverse)
            {
                triangles.Add(index + 1); triangles.Add(index); triangles.Add(index + 2);
                triangles.Add(index + 1); triangles.Add(index + 2); triangles.Add(index + 3);
                return;
            }
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            triangles.Add(index + 1); triangles.Add(index + 3); triangles.Add(index + 2);
        }
    }
}
