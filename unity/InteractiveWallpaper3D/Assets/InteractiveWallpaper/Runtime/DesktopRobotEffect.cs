#nullable enable

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InteractiveWallpaper
{
    /// <summary>A whimsical desktop robot that retrieves icons with a telescoping hook built from runtime 3D shapes.</summary>
    public sealed class DesktopRobotEffect : MonoBehaviour
    {
        private const float FrontDepth = -0.72f;
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly List<Transform> _wheels = new List<Transform>();

        private ProxyIconInteraction? _icon;
        private Transform? _robot;
        private Transform? _head;
        private Transform? _leftArm;
        private Transform? _rightArm;
        private Transform? _antennaTip;
        private Transform? _hookRoot;
        private LineRenderer? _cable;
        private LineRenderer? _hookLine;
        private Light? _keyLight;
        private Vector3 _iconStart;
        private Quaternion _iconRotation;
        private Vector3 _approach;
        private Vector3 _robotStart;
        private Vector3 _robotExit;
        private bool _holding;

        public bool Begin(ProxyIconInteraction icon, float worldWidth, float worldHeight)
        {
            _icon = icon;
            if (!_icon.BeginPortalHold())
            {
                return false;
            }

            _holding = true;
            _iconStart = icon.WorldPosition;
            _iconRotation = icon.transform.rotation;

            var side = _iconStart.x >= 0f ? -1f : 1f;
            var halfWidth = worldWidth * 0.5f;
            var bottomY = -worldHeight * 0.5f + 0.72f;
            _robotStart = new Vector3(side * (halfWidth + 0.82f), bottomY, FrontDepth);
            _approach = new Vector3(
                Mathf.Clamp(_iconStart.x, -halfWidth + 0.48f, halfWidth - 0.48f),
                bottomY,
                FrontDepth);
            _robotExit = new Vector3(-side * (halfWidth + 0.9f), bottomY, FrontDepth);

            BuildRobot();
            _robot!.position = _robotStart;
            SetCableVisible(false);
            StartCoroutine(Play());
            return true;
        }

        private IEnumerator Play()
        {
            try
            {
                DesktopEffectFeedback.EmitBurst(_robotStart, new Color(0.2f, 0.85f, 1f), 14, 1.4f);
                yield return MoveRobot(_robotStart, _approach, 1.0f);

                // The robot stays at the bottom edge; only its winch and hook travel up to the target.
                for (var elapsed = 0f; elapsed < 0.68f; elapsed += Time.unscaledDeltaTime)
                {
                    AnimateRobot(elapsed, 0.25f);
                    _robot!.position = _approach + Vector3.up * (Mathf.Sin(elapsed * 8f) * 0.02f);
                    _head!.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(elapsed * 13f) * 10f);
                    _antennaTip!.localScale = Vector3.one * (1f + Mathf.Sin(elapsed * 18f) * 0.17f);
                    yield return null;
                }

                _head!.localRotation = Quaternion.identity;
                _antennaTip!.localScale = Vector3.one;
                _hookRoot!.gameObject.SetActive(true);
                SetCableVisible(true);
                var winch = WinchPoint();
                var hookAtIcon = new Vector3(_iconStart.x, _iconStart.y + 0.27f, FrontDepth - 0.22f);
                yield return MoveHook(winch, hookAtIcon, 1.0f, false);
                DesktopEffectFeedback.EmitBurst(hookAtIcon, new Color(1f, 0.68f, 0.2f), 12, 1.0f);

                // The hook catches the top edge; the proxy gives a small tug before reeling starts.
                for (var elapsed = 0f; elapsed < 0.34f; elapsed += Time.unscaledDeltaTime)
                {
                    var t = elapsed / 0.34f;
                    var tug = Mathf.Sin(t * Mathf.PI * 5f) * 0.045f;
                    var caught = _iconStart + Vector3.up * tug;
                    caught.z = FrontDepth - 0.36f;
                    _icon!.SetPortalVisual(caught, _iconRotation * Quaternion.Euler(0f, 0f, tug * 100f), 1f, true);
                    AnimateRobot(elapsed + 1f, 0.2f);
                    UpdateCable(elapsed);
                    yield return null;
                }

                var dockHook = WinchPoint() + Vector3.up * 0.4f;
                yield return MoveHook(hookAtIcon, dockHook, 1.05f, true);
                DesktopEffectFeedback.EmitBurst(_icon!.WorldPosition, new Color(0.95f, 0.72f, 0.28f), 14, 1.2f);

                for (var elapsed = 0f; elapsed < 0.55f; elapsed += Time.unscaledDeltaTime)
                {
                    var sway = Mathf.Sin(elapsed * 8f) * 0.035f;
                    var heldPosition = _hookRoot!.position + Vector3.down * 0.31f + Vector3.right * sway;
                    _icon!.SetPortalVisual(heldPosition, _iconRotation * Quaternion.Euler(0f, 0f, sway * 110f), 1f, true);
                    _rightArm!.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(elapsed * 9f) * 13f);
                    AnimateRobot(elapsed + 2f, 0.15f);
                    UpdateCable(elapsed + 2f);
                    yield return null;
                }

                _rightArm!.localRotation = Quaternion.identity;
                yield return ReturnIconAndRetractHook();
                _icon!.SetPortalVisual(_iconStart, _iconRotation, 1f, true);
                _icon.CompletePortalHold();
                _holding = false;
                SetCableVisible(false);
                _hookRoot!.gameObject.SetActive(false);
                DesktopEffectFeedback.EmitBurst(_iconStart, new Color(0.55f, 0.92f, 1f), 12, 1.1f);

                for (var elapsed = 0f; elapsed < 0.4f; elapsed += Time.unscaledDeltaTime)
                {
                    _rightArm!.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Sin(elapsed * 16f) * 46f);
                    AnimateRobot(elapsed + 3f, 0.25f);
                    yield return null;
                }
                _rightArm!.localRotation = Quaternion.identity;
                yield return MoveRobot(_approach, _robotExit, 0.85f);
            }
            finally
            {
                SetCableVisible(false);
                RestoreIcon();
                Destroy(gameObject);
            }
        }

        private IEnumerator MoveRobot(Vector3 from, Vector3 to, float duration)
        {
            var elapsed = 0f;
            var direction = Mathf.Sign(to.x - from.x);
            _robot!.rotation = Quaternion.Euler(0f, 0f, -direction * 4f);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                var position = Vector3.Lerp(from, to, eased);
                position.y += Mathf.Sin(t * Mathf.PI) * 0.13f;
                _robot.position = position;
                _robot.rotation = Quaternion.Euler(0f, 0f, -direction * (4f + Mathf.Sin(t * Mathf.PI * 2f) * 2f));
                AnimateRobot(elapsed, 1f);
                yield return null;
            }
            _robot.position = to;
            _robot.rotation = Quaternion.identity;
        }

        private IEnumerator MoveHook(Vector3 from, Vector3 to, float duration, bool carryIcon)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                var position = Vector3.Lerp(from, to, eased);
                position.x += Mathf.Sin(t * Mathf.PI) * 0.09f;
                position.z = FrontDepth - 0.22f;
                _hookRoot!.position = position;
                _hookRoot.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 4f) * (1f - t) * 9f);
                if (carryIcon)
                {
                    var iconPosition = position + Vector3.down * 0.31f;
                    iconPosition.z = FrontDepth - 0.36f;
                    _icon!.SetPortalVisual(iconPosition, _iconRotation, 1f, true);
                }
                UpdateCable(elapsed);
                AnimateRobot(elapsed, 0.2f);
                yield return null;
            }
            _hookRoot!.position = to;
            _hookRoot.rotation = Quaternion.identity;
            UpdateCable(duration);
            if (carryIcon)
            {
                var iconPosition = to + Vector3.down * 0.31f;
                iconPosition.z = FrontDepth - 0.36f;
                _icon!.SetPortalVisual(iconPosition, _iconRotation, 1f, true);
            }
        }

        private IEnumerator ReturnIconAndRetractHook()
        {
            var start = _icon!.WorldPosition;
            var hookStart = _hookRoot!.position;
            var hookEnd = WinchPoint();
            const float duration = 0.9f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                var hookPosition = Vector3.Lerp(hookStart, hookEnd, eased);
                hookPosition.x += Mathf.Sin(t * Mathf.PI * 2f) * 0.025f * (1f - t);
                _hookRoot.position = hookPosition;
                _hookRoot.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 3f) * 5f * (1f - t));

                var iconPosition = Vector3.Lerp(start, _iconStart, eased);
                iconPosition.y += Mathf.Sin(t * Mathf.PI) * 0.44f;
                iconPosition.z = Mathf.Lerp(FrontDepth - 0.36f, _iconStart.z, eased);
                var wobble = Mathf.Sin(t * Mathf.PI * 2f) * (1f - t) * 12f;
                var scale = t > 0.84f ? 1f + Mathf.Sin((t - 0.84f) / 0.16f * Mathf.PI * 2f) * 0.11f * (1f - t) : 1f;
                _icon.SetPortalVisual(iconPosition, _iconRotation * Quaternion.Euler(0f, 0f, wobble), scale, true);
                UpdateCable(elapsed + 4f);
                AnimateRobot(elapsed + 4f, 0.1f);
                yield return null;
            }
            _hookRoot.position = hookEnd;
            _hookRoot.rotation = Quaternion.identity;
            UpdateCable(duration);
        }

        private Vector3 WinchPoint()
        {
            return _robot!.TransformPoint(new Vector3(0f, 0.72f, -0.5f));
        }

        private void UpdateCable(float time)
        {
            if (_cable == null || _hookRoot == null || _robot == null)
            {
                return;
            }
            var start = WinchPoint();
            var end = _hookRoot.position + Vector3.up * 0.16f;
            const int points = 6;
            _cable.positionCount = points;
            for (var index = 0; index < points; index++)
            {
                var t = index / (float)(points - 1);
                var point = Vector3.Lerp(start, end, t);
                point.x += Mathf.Sin(t * Mathf.PI + time * 8f) * 0.035f * Mathf.Sin(t * Mathf.PI);
                point.z = FrontDepth - 0.2f;
                _cable.SetPosition(index, point);
            }
        }

        private void SetCableVisible(bool visible)
        {
            if (_cable != null)
            {
                _cable.enabled = visible;
            }
            if (_hookRoot != null)
            {
                _hookRoot.gameObject.SetActive(visible);
            }
        }

        private void AnimateRobot(float time, float movement)
        {
            if (_robot == null)
            {
                return;
            }
            for (var index = 0; index < _wheels.Count; index++)
            {
                _wheels[index].Rotate(0f, 0f, 480f * movement * Time.unscaledDeltaTime, Space.Self);
            }
            if (_leftArm != null)
            {
                _leftArm.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 6f) * (1f - movement) * 7f);
            }
            if (_keyLight != null)
            {
                _keyLight.intensity = 1.35f + Mathf.Sin(time * 9f) * 0.24f;
            }
        }

        private void BuildRobot()
        {
            _robot = new GameObject("Mini Magnet Robot").transform;
            _robot.SetParent(transform, false);
            _robot.localPosition = Vector3.zero;

            var teal = CreateMaterial("Enamel Teal", new Color(0.035f, 0.54f, 0.68f), 0.48f, 0.62f);
            var porcelain = CreateMaterial("Warm Porcelain", new Color(0.83f, 0.92f, 0.89f), 0.18f, 0.72f);
            var dark = CreateMaterial("Midnight Graphite", new Color(0.025f, 0.065f, 0.12f), 0.58f, 0.76f);
            var amber = CreateMaterial("Signal Amber", new Color(1f, 0.49f, 0.08f), 0.3f, 0.55f);
            var cyan = CreateMaterial("Magnet Cyan", new Color(0.1f, 0.86f, 1f), 0.1f, 0.45f, true);
            var eye = CreateMaterial("Robot Eyes", new Color(0.17f, 0.86f, 1f), 0.1f, 0.2f, true);

            CreatePart("Hover Shadow", PrimitiveType.Sphere, new Vector3(0f, -0.49f, 0.12f), new Vector3(0.84f, 0.1f, 0.25f), dark);
            CreatePart("Core Body", PrimitiveType.Cube, new Vector3(0f, -0.02f, 0f), new Vector3(0.56f, 0.52f, 0.34f), teal);
            CreatePart("Chest Plate", PrimitiveType.Cube, new Vector3(0f, -0.055f, -0.185f), new Vector3(0.39f, 0.34f, 0.045f), porcelain);
            CreatePart("Power Core", PrimitiveType.Sphere, new Vector3(0f, -0.09f, -0.226f), new Vector3(0.155f, 0.155f, 0.055f), cyan);
            _head = new GameObject("Search Head Pivot").transform;
            _head.SetParent(_robot, false);
            _head.localPosition = new Vector3(0f, 0.37f, 0f);
            CreatePartUnder(_head, "Head", PrimitiveType.Sphere, new Vector3(0f, 0f, -0.015f), new Vector3(0.48f, 0.36f, 0.31f), porcelain);
            CreatePartUnder(_head, "Face Visor", PrimitiveType.Cube, new Vector3(0f, -0.01f, -0.168f), new Vector3(0.385f, 0.182f, 0.065f), dark);
            CreatePartUnder(_head, "Left Eye", PrimitiveType.Sphere, new Vector3(-0.105f, -0.002f, -0.212f), new Vector3(0.061f, 0.075f, 0.035f), eye);
            CreatePartUnder(_head, "Right Eye", PrimitiveType.Sphere, new Vector3(0.105f, -0.002f, -0.212f), new Vector3(0.061f, 0.075f, 0.035f), eye);
            CreatePartUnder(_head, "Antenna Stem", PrimitiveType.Cylinder, new Vector3(0f, 0.25f, -0.015f), new Vector3(0.055f, 0.14f, 0.055f), amber);
            _antennaTip = CreatePartUnder(_head, "Antenna Beacon", PrimitiveType.Sphere, new Vector3(0f, 0.42f, -0.015f), new Vector3(0.115f, 0.115f, 0.115f), cyan);

            CreatePart("Left Shoulder", PrimitiveType.Sphere, new Vector3(-0.34f, 0.035f, -0.02f), new Vector3(0.19f, 0.19f, 0.2f), amber);
            CreatePart("Right Shoulder", PrimitiveType.Sphere, new Vector3(0.34f, 0.035f, -0.02f), new Vector3(0.19f, 0.19f, 0.2f), amber);
            _leftArm = CreatePart("Left Arm", PrimitiveType.Capsule, new Vector3(-0.39f, -0.125f, -0.035f), new Vector3(0.145f, 0.255f, 0.145f), teal);
            _rightArm = CreatePart("Right Arm", PrimitiveType.Capsule, new Vector3(0.39f, -0.12f, -0.035f), new Vector3(0.145f, 0.255f, 0.145f), teal);
            CreatePart("Left Claw", PrimitiveType.Sphere, new Vector3(-0.4f, -0.285f, -0.085f), new Vector3(0.12f, 0.105f, 0.12f), porcelain);
            CreatePart("Right Claw", PrimitiveType.Sphere, new Vector3(0.4f, -0.285f, -0.085f), new Vector3(0.12f, 0.105f, 0.12f), porcelain);

            var leftWheel = CreatePart("Left Hover Wheel", PrimitiveType.Cylinder, new Vector3(-0.28f, -0.35f, 0.015f), new Vector3(0.15f, 0.125f, 0.15f), dark);
            leftWheel.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var rightWheel = CreatePart("Right Hover Wheel", PrimitiveType.Cylinder, new Vector3(0.28f, -0.35f, 0.015f), new Vector3(0.15f, 0.125f, 0.15f), dark);
            rightWheel.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _wheels.Add(leftWheel);
            _wheels.Add(rightWheel);
            CreatePart("Wheel Hub L", PrimitiveType.Sphere, new Vector3(-0.28f, -0.35f, -0.113f), new Vector3(0.075f, 0.075f, 0.035f), amber);
            CreatePart("Wheel Hub R", PrimitiveType.Sphere, new Vector3(0.28f, -0.35f, -0.113f), new Vector3(0.075f, 0.075f, 0.035f), amber);

            _keyLight = _robot.gameObject.AddComponent<Light>();
            _keyLight.type = LightType.Point;
            _keyLight.color = new Color(0.15f, 0.77f, 1f);
            _keyLight.intensity = 1.35f;
            _keyLight.range = 2.4f;
            _keyLight.shadows = LightShadows.None;
            _keyLight.transform.localPosition = new Vector3(0f, 0.28f, -0.42f);

            BuildHook(amber);
        }

        private void BuildHook(Material hookMaterial)
        {
            _hookRoot = new GameObject("Deployable Desktop Hook").transform;
            _hookRoot.SetParent(transform, false);
            _hookRoot.gameObject.SetActive(false);
            CreatePartUnder(_hookRoot, "Hook Socket", PrimitiveType.Sphere, new Vector3(0f, 0.16f, 0f), new Vector3(0.075f, 0.075f, 0.075f), hookMaterial);

            var hookObject = new GameObject("Curved Retrieval Hook");
            hookObject.transform.SetParent(_hookRoot, false);
            _hookLine = hookObject.AddComponent<LineRenderer>();
            _hookLine.useWorldSpace = false;
            _hookLine.positionCount = 7;
            _hookLine.numCapVertices = 4;
            _hookLine.numCornerVertices = 4;
            _hookLine.widthMultiplier = 0.042f;
            _hookLine.sortingOrder = 1450;
            var hookLineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Retrieval Hook Material", renderQueue = 3100 };
            _ownedMaterials.Add(hookLineMaterial);
            _hookLine.sharedMaterial = hookLineMaterial;
            _hookLine.startColor = new Color(1f, 0.72f, 0.28f, 1f);
            _hookLine.endColor = new Color(1f, 0.48f, 0.08f, 1f);
            _hookLine.SetPositions(new[]
            {
                new Vector3(0f, 0.16f, 0f),
                new Vector3(0f, 0.07f, 0f),
                new Vector3(-0.015f, -0.035f, 0f),
                new Vector3(-0.04f, -0.11f, 0f),
                new Vector3(-0.095f, -0.15f, 0f),
                new Vector3(-0.15f, -0.135f, 0f),
                new Vector3(-0.18f, -0.075f, 0f),
            });

            var cableObject = new GameObject("Reeling Cable");
            cableObject.transform.SetParent(transform, false);
            _cable = cableObject.AddComponent<LineRenderer>();
            _cable.useWorldSpace = true;
            _cable.positionCount = 6;
            _cable.numCapVertices = 2;
            _cable.widthMultiplier = 0.026f;
            _cable.sortingOrder = 1440;
            var cableMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Reeling Cable Material", renderQueue = 3100 };
            _ownedMaterials.Add(cableMaterial);
            _cable.sharedMaterial = cableMaterial;
            _cable.startColor = new Color(0.96f, 0.7f, 0.24f, 0.96f);
            _cable.endColor = new Color(0.94f, 0.84f, 0.57f, 0.9f);
            _cable.enabled = false;
        }

        private Transform CreatePart(string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            return CreatePartUnder(_robot!, name, type, localPosition, localScale, material);
        }

        private Transform CreatePartUnder(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 1320;
            return part.transform;
        }

        private Material CreateMaterial(string name, Color color, float metallic, float smoothness, bool emission = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, renderQueue = 3100 };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.8f);
            }
            _ownedMaterials.Add(material);
            return material;
        }

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
            foreach (var material in _ownedMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
            _ownedMaterials.Clear();
        }
    }
}
