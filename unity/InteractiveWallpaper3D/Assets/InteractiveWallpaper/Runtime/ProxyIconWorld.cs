#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class ProxyIconWorld : MonoBehaviour
    {
        private sealed class VisualHeldIcon
        {
            public ProxyIconInteraction Icon = null!;
            public Vector3 Offset;
        }

        private sealed class VisualRestoreIcon
        {
            public ProxyIconInteraction Icon = null!;
            public Vector3 Start;
            public Vector3 Target;
            public float Delay;
            public float Duration;
            public float Elapsed;
        }

        private readonly List<ProxyIconInteraction> _icons = new List<ProxyIconInteraction>();
        private readonly List<VisualHeldIcon> _visualHeldIcons = new List<VisualHeldIcon>();
        private readonly List<VisualRestoreIcon> _visualRestoreIcons = new List<VisualRestoreIcon>();
        private float _nextHeavyEffectTime;
        private float _nextKickEffectTime;

        public static ProxyIconWorld? Instance { get; private set; }
        public IReadOnlyList<ProxyIconInteraction> Icons => _icons;
        public ProxyIconInteraction? Selected { get; private set; }
        public float WorldWidth { get; private set; }
        public float WorldHeight { get; private set; }
        public string Status { get; private set; } = "尚未选择代理图标";

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            UpdateVisualRestores();
        }

        public void Initialize(float worldWidth, float worldHeight)
        {
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            CreateBoundary("Bottom", new Vector3(0f, -worldHeight * 0.5f - 0.25f, 0f), new Vector3(worldWidth + 1f, 0.5f, 2f));
            CreateBoundary("Top", new Vector3(0f, worldHeight * 0.5f + 0.25f, 0f), new Vector3(worldWidth + 1f, 0.5f, 2f));
            CreateBoundary("Left", new Vector3(-worldWidth * 0.5f - 0.25f, 0f, 0f), new Vector3(0.5f, worldHeight + 1f, 2f));
            CreateBoundary("Right", new Vector3(worldWidth * 0.5f + 0.25f, 0f, 0f), new Vector3(0.5f, worldHeight + 1f, 2f));
        }

        public void Register(ProxyIconInteraction icon)
        {
            if (!_icons.Contains(icon))
            {
                _icons.Add(icon);
            }
        }

        public void Select(ProxyIconInteraction icon)
        {
            if (Selected == icon)
            {
                return;
            }
            Selected?.SetSelected(false);
            Selected = icon;
            Selected.SetSelected(true);
            Status = $"已选择：{icon.DisplayName}（{icon.MotionState}）";
        }

        public void NotifyStateChanged(ProxyIconInteraction icon)
        {
            if (Selected == icon)
            {
                Status = $"已选择：{icon.DisplayName}（{icon.MotionState}）";
            }
        }

        public void DropAll()
        {
            if (!TryBeginHeavyEffect())
            {
                return;
            }
            var affected = 0;
            for (var index = 0; index < _icons.Count; index++)
            {
                var icon = _icons[index];
                if (!icon.CanCharacterGrab)
                {
                    continue;
                }
                var horizontal = ((index % 9) - 4) * 0.035f;
                icon.SetDynamic(new Vector3(horizontal, 0.12f, 0f));
                affected++;
            }
            DesktopEffectFeedback.EmitBurst(
                new Vector3(0f, WorldHeight * 0.28f, -0.2f),
                new Color(0.35f, 0.75f, 1f),
                26,
                2.0f);
            Status = $"重力坠落：{affected} 个图标进入物理状态";
            Debug.Log($"Desktop effect: gravity drop ({affected} icons)");
        }

        public void KickSelected()
        {
            if (Time.unscaledTime < _nextKickEffectTime)
            {
                return;
            }
            _nextKickEffectTime = Time.unscaledTime + 0.35f;
            if (Selected == null)
            {
                ProxyIconInteraction? candidate = null;
                if (MouseWorldPosition.TryGetWorldPosition(out var mouseWorld))
                {
                    candidate = FindNearestInteractiveIcon(mouseWorld, 1.8f);
                }
                candidate ??= FindLowestInteractiveIcon();
                if (candidate != null)
                {
                    Select(candidate);
                }
            }
            if (Selected == null || !Selected.CanCharacterGrab)
            {
                Status = "请先选择一个靠近人物底部区域的可互动图标";
                Debug.Log("Character kick request ignored: no interactive icon is available.");
                return;
            }

            var character = ProceduralCharacterController.Instance;
            if (character == null || !character.RequestKick(Selected))
            {
                Status = $"当前无法踢击：{Selected.DisplayName}";
                Debug.Log($"Character kick request rejected by controller: {Selected.DisplayName}");
                return;
            }
            Status = $"人物正在准备踢击：{Selected.DisplayName}";
            Debug.Log($"Character kick requested: {Selected.DisplayName}");
        }

        public void SweepAll()
        {
            if (!TryBeginHeavyEffect())
            {
                return;
            }
            var sweepOrigin = MouseWorldPosition.TryGetWorldPosition(out var mouseWorld)
                ? mouseWorld
                : MouseWorldPosition.GetRandomWorldPosition();
            var direction = Mathf.Abs(sweepOrigin.x) > 0.001f ? Mathf.Sign(sweepOrigin.x) : 1f;
            var affected = 0;
            foreach (var icon in _icons)
            {
                if (!icon.CanCharacterGrab)
                {
                    continue;
                }
                var distance = Mathf.Abs(icon.WorldPosition.y - sweepOrigin.y);
                var falloff = Mathf.Clamp01(distance / Mathf.Max(0.1f, WorldHeight * 0.5f));
                var strength = (3.4f + falloff * 0.6f) * direction;
                var vertical = 0.45f + (1f - falloff) * 0.15f;
                icon.SetDynamic(new Vector3(strength, vertical, 0f));
                affected++;
            }
            DesktopEffectFeedback.EmitBurst(
                sweepOrigin + new Vector3(0f, 0f, -0.2f),
                new Color(0.25f, 0.95f, 0.85f),
                32,
                3.2f);
            Status = string.Format("横向扫落：{0} 个图标从 ({1:F1}, {2:F1}) 开始", affected, sweepOrigin.x, sweepOrigin.y);
            Debug.Log(string.Format("Desktop effect: sweep all ({0} icons from {1:F1}, {2:F1})", affected, sweepOrigin.x, sweepOrigin.y));
        }

        public void SneezeBurst(Vector3 origin)
        {
            if (!TryBeginHeavyEffect())
            {
                return;
            }

            var maximumDistance = Mathf.Sqrt(WorldWidth * WorldWidth + WorldHeight * WorldHeight) * 0.78f;
            var affected = 0;
            for (var index = 0; index < _icons.Count; index++)
            {
                var icon = _icons[index];
                if (!icon.CanCharacterGrab)
                {
                    continue;
                }

                var delta = icon.WorldPosition - origin;
                delta.z = 0f;
                var distance = Mathf.Max(0.05f, delta.magnitude);
                var attenuation = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, maximumDistance));
                var leftBias = Mathf.Clamp01((origin.x - icon.WorldPosition.x + WorldWidth * 0.18f) / (WorldWidth * 0.72f));
                var horizontal = -(2.1f + attenuation * (2.5f + leftBias * 3.6f));
                var vertical = 0.22f + attenuation * 0.72f + ((index % 7) - 3) * 0.055f;
                icon.SetDynamic(new Vector3(horizontal, vertical, 0f));
                affected++;
            }

            DesktopEffectFeedback.EmitBurst(
                origin,
                new Color(0.72f, 0.92f, 1f),
                64,
                5.2f);
            Status = $"人物喷嚏：{affected} 个图标被主动吹落";
            Debug.Log($"Desktop effect: character sneeze burst ({affected} icons from {origin.x:F1}, {origin.y:F1})");
        }
        public void BeginCharacterPickup(Vector3 anchor)
        {
            _visualHeldIcons.Clear();
            _visualRestoreIcons.Clear();

            var candidates = new List<ProxyIconInteraction>();
            foreach (var icon in _icons)
            {
                if (icon.CanCharacterGrab)
                {
                    candidates.Add(icon);
                }
            }
            candidates.Sort((left, right) =>
                Vector3.SqrMagnitude(left.WorldPosition - anchor).CompareTo(
                    Vector3.SqrMagnitude(right.WorldPosition - anchor)));

            var count = Mathf.Min(16, candidates.Count);
            for (var index = 0; index < count; index++)
            {
                var icon = candidates[index];
                if (!icon.BeginVisualCharacterHold())
                {
                    continue;
                }

                var column = index % 4;
                var row = index / 4;
                _visualHeldIcons.Add(new VisualHeldIcon
                {
                    Icon = icon,
                    Offset = new Vector3((column - 1.5f) * 0.18f, (1.5f - row) * 0.11f, -0.05f),
                });
            }

            Status = $"人物正在收拢 {_visualHeldIcons.Count} 个图标";
            Debug.Log($"Desktop effect: character pickup started ({_visualHeldIcons.Count} icons)");
        }

        public void UpdateCharacterPickup(Vector3 anchor)
        {
            foreach (var held in _visualHeldIcons)
            {
                held.Icon.SetVisualCharacterHold(
                    anchor + held.Offset,
                    Quaternion.identity);
            }
        }

        public void ReleaseCharacterPickupAndRestore()
        {
            _visualRestoreIcons.Clear();
            var restoreIndex = 0;
            foreach (var icon in _icons)
            {
                if (icon.MotionState == ProxyIconMotionState.Dynamic)
                {
                    icon.BeginVisualCharacterHold();
                }
                if (icon.MotionState != ProxyIconMotionState.CharacterGrabbed)
                {
                    continue;
                }

                _visualRestoreIcons.Add(new VisualRestoreIcon
                {
                    Icon = icon,
                    Start = icon.WorldPosition,
                    Target = icon.DesktopWorldPosition,
                    Delay = restoreIndex * 0.012f,
                    Duration = 0.78f + (restoreIndex % 5) * 0.04f,
                });
                restoreIndex++;
            }
            _visualHeldIcons.Clear();
            Status = $"人物释放图标，正在恢复 {_visualRestoreIcons.Count} 个桌面项目";
            Debug.Log($"Desktop effect: character restore started ({_visualRestoreIcons.Count} icons)");
        }

        private void UpdateVisualRestores()
        {
            for (var index = _visualRestoreIcons.Count - 1; index >= 0; index--)
            {
                var restore = _visualRestoreIcons[index];
                restore.Elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01((restore.Elapsed - restore.Delay) / restore.Duration);
                var eased = 1f - Mathf.Pow(1f - normalized, 3f);
                var position = Vector3.Lerp(restore.Start, restore.Target, eased);
                restore.Icon.SetVisualCharacterHold(position, Quaternion.identity);
                if (normalized >= 1f)
                {
                    restore.Icon.CompleteVisualDesktopRestore();
                    _visualRestoreIcons.RemoveAt(index);
                }
            }
        }

        public void Shockwave()
        {
            if (!TryBeginHeavyEffect())
            {
                return;
            }
            var origin = MouseWorldPosition.TryGetWorldPosition(out var mouseWorld)
                ? mouseWorld
                : MouseWorldPosition.GetRandomWorldPosition();
            var maximumDistance = Mathf.Sqrt(WorldWidth * WorldWidth + WorldHeight * WorldHeight) * 0.5f;
            var affected = 0;
            foreach (var icon in _icons)
            {
                if (!icon.CanCharacterGrab)
                {
                    continue;
                }
                var delta = icon.WorldPosition - origin;
                delta.z = 0f;
                var distance = Mathf.Max(0.05f, delta.magnitude);
                var attenuation = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, maximumDistance));
                var strength = 1.6f + attenuation * 3.8f;
                var direction = delta / distance;
                icon.SetDynamic(direction * strength + Vector3.up * 0.5f);
                affected++;
            }
            DesktopEffectFeedback.EmitBurst(
                origin + new Vector3(0f, 0f, -0.2f),
                new Color(1f, 0.85f, 0.2f),
                48,
                4.0f);
            Status = "中心冲击波：" + affected + " 个图标从 (" + origin.x.ToString("F1") + ", " + origin.y.ToString("F1") + ") 位置震开";
            Debug.Log("Desktop effect: shockwave (" + affected + " icons from " + origin.x.ToString("F1") + ", " + origin.y.ToString("F1") + ")");
        }
        public void StartPortalEffect()
        {
            if (GetComponent<DesktopPortalEffect>() != null)
            {
                Status = "传送门效果正在进行中";
                return;
            }

            var target = FindNearestPinnedIconToMouse(out var mousePosition);
            var usesMouse = target != null;
            target ??= GetRandomAvailablePinnedIcon();
            if (target == null)
            {
                Status = "没有可供传送的桌面图标";
                return;
            }

            var entrance = usesMouse ? mousePosition : target.WorldPosition;
            entrance.z = target.WorldPosition.z;
            var effectObject = new GameObject("Desktop Portal Effect");
            var effect = effectObject.AddComponent<DesktopPortalEffect>();
            if (!effect.Begin(target, entrance, WorldWidth, WorldHeight))
            {
                Destroy(effectObject);
                Status = "无法启动传送门：目标图标当前不可交互";
                return;
            }

            Select(target);
            Status = "桌面传送门：" + target.DisplayName + " 正在穿越";
        }

        public void StartRobotEffect()
        {
            if (GetComponent<DesktopRobotEffect>() != null)
            {
                Status = "钩索机器人正在取放图标";
                return;
            }

            var target = FindNearestPinnedIconToMouse(out _);
            target ??= GetRandomAvailablePinnedIcon();
            if (target == null)
            {
                Status = "没有可供机器人互动的桌面图标";
                return;
            }

            var effectObject = new GameObject("Desktop Magnet Robot Effect");
            var effect = effectObject.AddComponent<DesktopRobotEffect>();
            if (!effect.Begin(target, WorldWidth, WorldHeight))
            {
                Destroy(effectObject);
                Status = "机器人无法锁定该图标";
                return;
            }

            Select(target);
            Status = "钩索机器人正在回收：" + target.DisplayName;
        }

        public void ReportWindFieldHitCount(int count)
        {
            Status = count == 0
                ? "秋风风场正在掠过桌面"
                : $"秋风风场已推动 {count} 个代理图标";
        }

        public void StartWeatherEffect()
        {
            if (GetComponent<DesktopWeatherEffect>() != null)
            {
                Status = "秋风效果正在进行中";
                return;
            }

            var effectObject = new GameObject("Desktop Autumn Gust Effect");
            var effect = effectObject.AddComponent<DesktopWeatherEffect>();
            if (!effect.Begin(this, WorldWidth, WorldHeight))
            {
                Destroy(effectObject);
                Status = "无法启动秋风效果";
                return;
            }

            Status = effect.TargetIconCount > 0
                ? $"秋风风场已覆盖 {effect.TargetIconCount} 个图标，等待风前沿掠过"
                : "秋风已启动，但没有找到可互动的代理图标";
        }

        private ProxyIconInteraction? FindNearestPinnedIconToMouse(out Vector3 mousePosition)
        {
            mousePosition = Vector3.zero;
            if (!MouseWorldPosition.TryGetWorldPosition(out var cursor))
            {
                return null;
            }

            ProxyIconInteraction? nearest = null;
            var nearestDistance = 1.25f;
            foreach (var icon in _icons)
            {
                if (icon.MotionState != ProxyIconMotionState.DesktopPinned || icon.IsCatStolen)
                {
                    continue;
                }

                var distance = Vector2.Distance(icon.WorldPosition, cursor);
                if (distance < nearestDistance)
                {
                    nearest = icon;
                    nearestDistance = distance;
                }
            }

            if (nearest != null)
            {
                mousePosition = cursor;
            }
            return nearest;
        }

        private ProxyIconInteraction? GetRandomAvailablePinnedIcon()
        {
            var candidates = new List<ProxyIconInteraction>();
            foreach (var icon in _icons)
            {
                if (icon.MotionState == ProxyIconMotionState.DesktopPinned && !icon.IsCatStolen)
                {
                    candidates.Add(icon);
                }
            }
            return candidates.Count == 0
                ? null
                : candidates[Random.Range(0, candidates.Count)];
        }

        public ProxyIconInteraction? GetRandomAvailableIcon()
        {
            var candidates = new List<ProxyIconInteraction>();
            foreach (var icon in _icons)
            {
                if (icon.CanCharacterGrab && !icon.IsCatStolen)
                {
                    candidates.Add(icon);
                }
            }

            return candidates.Count == 0
                ? null
                : candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }
        public void ResetAll()
        {
            _visualHeldIcons.Clear();
            _visualRestoreIcons.Clear();
            foreach (var icon in _icons)
            {
                icon.ResetToDesktopPosition();
            }
            DesktopEffectFeedback.EmitBurst(
                new Vector3(0f, -WorldHeight * 0.3f, -0.2f),
                new Color(0.35f, 1f, 0.45f),
                20,
                1.6f);
            Status = $"已复位 {_icons.Count} 个代理图标";
            Debug.Log($"Desktop effect: reset all ({_icons.Count} icons)");
        }

        private ProxyIconInteraction? FindLowestInteractiveIcon()
        {
            ProxyIconInteraction? lowest = null;
            foreach (var icon in _icons)
            {
                if (!icon.CanCharacterGrab)
                {
                    continue;
                }
                if (lowest == null || icon.WorldPosition.y < lowest.WorldPosition.y)
                {
                    lowest = icon;
                }
            }
            return lowest;
        }
        private ProxyIconInteraction? FindNearestInteractiveIcon(Vector3 position, float maximumDistance)
        {
            ProxyIconInteraction? nearest = null;
            var nearestDistance = maximumDistance;
            foreach (var icon in _icons)
            {
                if (!icon.CanCharacterGrab)
                {
                    continue;
                }
                var distance = Vector2.Distance(icon.WorldPosition, position);
                if (distance < nearestDistance)
                {
                    nearest = icon;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }
        private bool TryBeginHeavyEffect()
        {
            if (Time.unscaledTime < _nextHeavyEffectTime)
            {
                return false;
            }
            _nextHeavyEffectTime = Time.unscaledTime + 0.65f;
            return true;
        }
        private void CreateBoundary(string boundaryName, Vector3 position, Vector3 size)
        {
            var boundary = new GameObject($"Desktop Boundary {boundaryName}");
            boundary.transform.SetParent(transform, false);
            boundary.transform.localPosition = position;
            var collider = boundary.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}

