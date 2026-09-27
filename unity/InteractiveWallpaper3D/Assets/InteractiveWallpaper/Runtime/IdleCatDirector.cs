#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class IdleCatDirector : MonoBehaviour
    {
        public enum CatState
        {
            Hidden,
            EdgePeeking,
            Watching,
            SneakingToTarget,
            ReachingPaw,
            CarryingIcon,
            HidingWithIcon,
            Startled,
            RunningAway,
        }

        private static readonly string[] CatResourcePaths =
        {
            // Keep the bright orange variant as the default until every UV texture is visually approved.
            "Characters/Cat/CatOrangeTabby",
        };

        private static bool IsDebugIdleMode =>
            Application.isEditor ||
            Debug.isDebugBuild ||
            string.Equals(
                Environment.GetEnvironmentVariable("INTERACTIVE_WALLPAPER_DEBUG_BUILD"),
                "1",
                StringComparison.OrdinalIgnoreCase);

        private static float IdleThresholdSeconds => IsDebugIdleMode ? 10f : 60f;
        private const float EdgePeekDurationSeconds = 1.8f;
        private const float WatchDurationSeconds = 1.25f;
        private const float ReachDurationSeconds = 0.75f;
        private const float StartledDurationSeconds = 0.28f;
        private const float TheftCooldownSeconds = 1.5f;
        private const float SneakSpeed = 0.55f;
        private const float CarrySpeed = 0.72f;
        private const float EscapeSpeed = 2.4f;
        private const float CatVisualScale = 0.56f;
        private const float NavigationCellSize = 0.38f;

        private IdleInputMonitor? _inputMonitor;
        private GameObject? _catInstance;
        private Animator? _animator;
        private ProxyIconInteraction? _targetIcon;
        private CatState _state = CatState.Hidden;
        private float _stateTime;
        private float _nextTheftTime;
        private Vector3 _hidePosition;
        private Vector3 _edgePosition;
        private Vector3 _approachPosition;
        private int _edgeSide = 1;
        private bool _iconHeld;
        private readonly List<Vector3> _sneakPath = new List<Vector3>();
        private readonly List<Vector3> _returnPath = new List<Vector3>();
        private int _sneakPathIndex;
        private int _returnPathIndex;
        private Vector3 _moveVelocity;
        private Transform? _headBone;
        private Transform? _pelvisBone;

        public static IdleCatDirector? Instance { get; private set; }
        public CatState State => _state;
        public string Status { get; private set; } = "猫咪藏在桌面角落";
        public bool IsModelInstalled => _catInstance != null;

        private void Awake()
        {
            Instance = this;
            _inputMonitor = gameObject.AddComponent<IdleInputMonitor>();
            _inputMonitor.InputDetected += OnUserInput;
        }

        private void Start()
        {
            var prefab = LoadRandomCatPrefab();
            if (prefab == null)
            {
                Status = "猫咪功能等待 3D 模型";
                Debug.Log("Idle cat director is dormant: no cat prefab found in Resources/Characters/Cat.");
                enabled = false;
                return;
            }

            _catInstance = Instantiate(prefab, transform, false);
            _catInstance.name = $"Idle Cat 3D Character ({prefab.name})";
            _catInstance.transform.localScale = Vector3.one * CatVisualScale;
            _animator = _catInstance.GetComponentInChildren<Animator>();
            _headBone = FindTransform(_catInstance.transform, "head_05");
            _pelvisBone = FindTransform(_catInstance.transform, "pelvis_01");
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            SetState(CatState.Hidden);
            Debug.Log($"Idle cat 3D character loaded: {prefab.name}; waiting for {IdleThresholdSeconds:F0}s global idle.");
        }

        private static GameObject? LoadRandomCatPrefab()
        {
            var available = new List<GameObject>();
            foreach (var resourcePath in CatResourcePaths)
            {
                var prefab = Resources.Load<GameObject>(resourcePath);
                if (prefab != null && !available.Contains(prefab))
                {
                    available.Add(prefab);
                }
            }

            return available.Count == 0
                ? null
                : available[UnityEngine.Random.Range(0, available.Count)];
        }

        private void Update()
        {
            var world = ProxyIconWorld.Instance;
            if (_inputMonitor == null || _catInstance == null || world == null || world.WorldWidth <= 0f)
            {
                return;
            }

            _stateTime += Time.unscaledDeltaTime;
            switch (_state)
            {
                case CatState.Hidden:
                    if (_inputMonitor.IdleSeconds >= IdleThresholdSeconds &&
                        Time.unscaledTime >= _nextTheftTime)
                    {
                        BeginTheftAttempt(world);
                    }
                    break;
                case CatState.EdgePeeking:
                    MoveTowards(_edgePosition, 1.2f);
                    if (_stateTime >= EdgePeekDurationSeconds)
                    {
                        SetState(CatState.Watching);
                    }
                    break;
                case CatState.Watching:
                    FaceTowards(_targetIcon != null ? _targetIcon.WorldPosition : _approachPosition);
                    if (_stateTime >= WatchDurationSeconds)
                    {
                        SetState(CatState.SneakingToTarget);
                    }
                    break;
                case CatState.SneakingToTarget:
                    if (MoveAlongPath(_sneakPath, ref _sneakPathIndex, SneakSpeed) || _stateTime >= 10f)
                    {
                        SetState(CatState.ReachingPaw);
                    }
                    break;
                case CatState.ReachingPaw:
                    FaceTowards(_targetIcon != null ? _targetIcon.WorldPosition : _approachPosition);
                    if (_stateTime >= ReachDurationSeconds)
                    {
                        TryGrabTarget();
                    }
                    break;
                case CatState.CarryingIcon:
                    UpdateHeldIcon();
                    if (MoveAlongPath(_returnPath, ref _returnPathIndex, CarrySpeed))
                    {
                        SetState(CatState.HidingWithIcon);
                    }
                    break;
                case CatState.HidingWithIcon:
                    UpdateHeldIcon();
                    if (_stateTime >= 0.32f)
                    {
                        HideStolenIcon();
                        _nextTheftTime = Time.unscaledTime + TheftCooldownSeconds;
                        SetState(CatState.Hidden);
                    }
                    break;
                case CatState.Startled:
                    ReleaseHeldIcon();
                    if (_stateTime >= StartledDurationSeconds)
                    {
                        SetState(CatState.RunningAway);
                    }
                    break;
                case CatState.RunningAway:
                    ReleaseHeldIcon();
                    if (MoveTowards(_hidePosition, EscapeSpeed))
                    {
                        _nextTheftTime = Time.unscaledTime + TheftCooldownSeconds;
                        SetState(CatState.Hidden);
                    }
                    break;
            }
        }

        private void BeginTheftAttempt(ProxyIconWorld world)
        {
            _targetIcon = world.GetRandomAvailableIcon();
            if (_targetIcon == null)
            {
                Status = "猫咪找不到还可以偷走的图标";
                _nextTheftTime = Time.unscaledTime + TheftCooldownSeconds;
                return;
            }

            _edgeSide = _targetIcon.WorldPosition.x >= 0f ? 1 : -1;
            var boundaryX = world.WorldWidth * 0.5f;
            var edgeY = -world.WorldHeight * 0.5f + 0.08f;
            var catHalfLength = 1.45f * CatVisualScale;
            _hidePosition = new Vector3(_edgeSide * (boundaryX + catHalfLength + 0.18f), edgeY, -0.35f);
            _edgePosition = new Vector3(_edgeSide * (boundaryX + catHalfLength * 0.42f), edgeY, -0.35f);
            var approachOffset = _targetIcon.NavigationHalfExtents.x + 0.42f;
            _approachPosition = _targetIcon.WorldPosition + new Vector3(_edgeSide * approachOffset, 0f, -0.35f);
            _approachPosition.x = Mathf.Clamp(
                _approachPosition.x,
                -world.WorldWidth * 0.5f + 0.55f,
                world.WorldWidth * 0.5f - 0.55f);
            _approachPosition.y = Mathf.Clamp(
                _approachPosition.y,
                -world.WorldHeight * 0.5f + 0.45f,
                world.WorldHeight * 0.5f - 0.55f);
            BuildNavigationPaths(world);
            _catInstance!.transform.position = _hidePosition;
            FaceTowards(_edgePosition);
            SetState(CatState.EdgePeeking);
            Debug.Log($"Idle cat theft started: target={_targetIcon.DisplayName}, side={_edgeSide}");
        }

        private void TryGrabTarget()
        {
            if (_targetIcon == null || !_targetIcon.CanCharacterGrab)
            {
                SetState(CatState.Hidden);
                _nextTheftTime = Time.unscaledTime + TheftCooldownSeconds;
                return;
            }

            if (_targetIcon.BeginVisualCharacterHold())
            {
                _iconHeld = true;
                SetState(CatState.CarryingIcon);
                return;
            }

            SetState(CatState.Hidden);
            _nextTheftTime = Time.unscaledTime + TheftCooldownSeconds;
        }

        private void BuildNavigationPaths(ProxyIconWorld world)
        {
            _sneakPath.Clear();
            _returnPath.Clear();
            _sneakPathIndex = 0;
            _returnPathIndex = 0;

            var path = FindScreenPath(world, _edgePosition, _approachPosition, _targetIcon);
            if (path.Count == 0)
            {
                path.Add(_edgePosition);
                path.Add(_approachPosition);
            }

            // Skip the first point because the cat is already at the edge entrance.
            for (var i = 1; i < path.Count; i++)
            {
                _sneakPath.Add(path[i]);
            }

            for (var i = path.Count - 1; i >= 0; i--)
            {
                _returnPath.Add(path[i]);
            }
            _returnPath.Add(_hidePosition);
        }

        private static List<Vector3> FindScreenPath(
            ProxyIconWorld world,
            Vector3 start,
            Vector3 destination,
            ProxyIconInteraction? ignoredIcon)
        {
            var step = NavigationCellSize;
            var minX = -world.WorldWidth * 0.5f + 0.42f;
            var maxX = world.WorldWidth * 0.5f - 0.42f;
            var minY = -world.WorldHeight * 0.5f + 0.38f;
            var maxY = world.WorldHeight * 0.5f - 0.38f;
            var columns = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) / step) + 1);
            var rows = Mathf.Max(2, Mathf.CeilToInt((maxY - minY) / step) + 1);
            var total = columns * rows;
            var blocked = new bool[total];
            var cost = new float[total];
            var parent = new int[total];
            var closed = new bool[total];
            for (var i = 0; i < total; i++)
            {
                cost[i] = float.PositiveInfinity;
                parent[i] = -1;
            }

            foreach (var icon in world.Icons)
            {
                if (icon == null || icon == ignoredIcon || icon.IsCatStolen)
                {
                    continue;
                }

                var half = icon.NavigationHalfExtents + new Vector2(0.34f, 0.28f);
                var center = icon.WorldPosition;
                var left = Mathf.Clamp(Mathf.FloorToInt((center.x - half.x - minX) / step), 0, columns - 1);
                var right = Mathf.Clamp(Mathf.CeilToInt((center.x + half.x - minX) / step), 0, columns - 1);
                var bottom = Mathf.Clamp(Mathf.FloorToInt((center.y - half.y - minY) / step), 0, rows - 1);
                var top = Mathf.Clamp(Mathf.CeilToInt((center.y + half.y - minY) / step), 0, rows - 1);
                for (var y = bottom; y <= top; y++)
                for (var x = left; x <= right; x++)
                {
                    blocked[y * columns + x] = true;
                }
            }

            var startCell = ToCell(start, minX, minY, step, columns, rows);
            var goalCell = ToCell(destination, minX, minY, step, columns, rows);
            var startId = startCell.y * columns + startCell.x;
            var goalId = goalCell.y * columns + goalCell.x;
            blocked[startId] = false;
            blocked[goalId] = false;
            cost[startId] = 0f;
            var open = new List<int> { startId };
            var found = false;
            var directions = new (int x, int y, float cost)[]
            {
                (1,0,1f),(-1,0,1f),(0,1,1f),(0,-1,1f),
                (1,1,1.41421356f),(1,-1,1.41421356f),(-1,1,1.41421356f),(-1,-1,1.41421356f),
            };

            while (open.Count > 0)
            {
                var bestIndex = 0;
                var bestId = open[0];
                var bestCell = new Vector2Int(bestId % columns, bestId / columns);
                var bestScore = cost[bestId] + OctileDistance(bestCell, goalCell);
                for (var i = 1; i < open.Count; i++)
                {
                    var id = open[i];
                    var cell = new Vector2Int(id % columns, id / columns);
                    var score = cost[id] + OctileDistance(cell, goalCell);
                    if (score < bestScore)
                    {
                        bestIndex = i;
                        bestId = id;
                        bestCell = cell;
                        bestScore = score;
                    }
                }

                open.RemoveAt(bestIndex);
                if (bestId == goalId)
                {
                    found = true;
                    break;
                }
                if (closed[bestId]) continue;
                closed[bestId] = true;

                foreach (var direction in directions)
                {
                    var nx = bestCell.x + direction.x;
                    var ny = bestCell.y + direction.y;
                    if (nx < 0 || nx >= columns || ny < 0 || ny >= rows) continue;
                    var neighbor = ny * columns + nx;
                    if (blocked[neighbor] || closed[neighbor]) continue;
                    if (direction.x != 0 && direction.y != 0 &&
                        (blocked[bestCell.y * columns + nx] || blocked[ny * columns + bestCell.x]))
                    {
                        continue;
                    }

                    var candidateCost = cost[bestId] + direction.cost;
                    if (candidateCost >= cost[neighbor]) continue;
                    parent[neighbor] = bestId;
                    cost[neighbor] = candidateCost;
                    if (!open.Contains(neighbor)) open.Add(neighbor);
                }
            }

            if (!found)
            {
                return new List<Vector3>();
            }

            var reverse = new List<int>();
            for (var id = goalId; id >= 0; id = parent[id])
            {
                reverse.Add(id);
                if (id == startId) break;
            }
            reverse.Reverse();
            var path = new List<Vector3>(reverse.Count);
            foreach (var id in reverse)
            {
                var x = id % columns;
                var y = id / columns;
                path.Add(new Vector3(minX + x * step, minY + y * step, start.z));
            }
            if (path.Count > 0) path[path.Count - 1] = destination;
            return path;
        }

        private static Vector2Int ToCell(Vector3 point, float minX, float minY, float step, int columns, int rows)
        {
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt((point.x - minX) / step), 0, columns - 1),
                Mathf.Clamp(Mathf.RoundToInt((point.y - minY) / step), 0, rows - 1));
        }

        private static float OctileDistance(Vector2Int a, Vector2Int b)
        {
            var dx = Mathf.Abs(a.x - b.x);
            var dy = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
        }
        private bool MoveAlongPath(List<Vector3> path, ref int index, float speed)
        {
            while (index < path.Count)
            {
                if (!MoveTowards(path[index], speed))
                {
                    return false;
                }
                index++;
            }
            return true;
        }
        private bool MoveTowards(Vector3 target, float speed)
        {
            if (_catInstance == null)
            {
                return true;
            }

            var current = _catInstance.transform.position;
            var delta = target - current;
            if (delta.sqrMagnitude <= 0.0064f)
            {
                _catInstance.transform.position = target;
                _moveVelocity = Vector3.zero;
                return true;
            }

            if (Mathf.Abs(delta.x) > 0.025f)
            {
                FaceTowards(target);
            }
            _catInstance.transform.position = Vector3.SmoothDamp(
                current,
                target,
                ref _moveVelocity,
                0.18f,
                speed,
                Time.unscaledDeltaTime);
            return false;
        }

        private void FaceTowards(Vector3 target)
        {
            if (_catInstance == null)
            {
                return;
            }
            var yaw = target.x >= _catInstance.transform.position.x ? 0f : 180f;
            var targetRotation = Quaternion.Euler(0f, yaw, 0f);
            _catInstance.transform.localRotation = Quaternion.RotateTowards(
                _catInstance.transform.localRotation,
                targetRotation,
                420f * Time.unscaledDeltaTime);
        }

        private static Transform? FindTransform(Transform root, string nameFragment)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        private void UpdateHeldIcon()
        {
            if (!_iconHeld || _targetIcon == null || _catInstance == null)
            {
                return;
            }

            var headPosition = _headBone != null ? _headBone.position : _catInstance.transform.position + Vector3.up * 0.38f;
            var pelvisPosition = _pelvisBone != null ? _pelvisBone.position : _catInstance.transform.position;
            var localHead = _catInstance.transform.InverseTransformPoint(headPosition);
            var localPelvis = _catInstance.transform.InverseTransformPoint(pelvisPosition);
            var forward = Mathf.Sign(localHead.x - localPelvis.x);
            if (Mathf.Abs(forward) < 0.1f) forward = 1f;
            var mouthPosition = headPosition + _catInstance.transform.TransformVector(new Vector3(forward * 0.42f, -0.08f, 0f));
            var holdPosition = mouthPosition + Vector3.back * 0.32f;
            _targetIcon.SetVisualCharacterHold(holdPosition, Quaternion.identity);
        }

        private void ReleaseHeldIcon()
        {
            if (!_iconHeld || _targetIcon == null)
            {
                return;
            }

            var escapeVelocity = new Vector3(-_edgeSide * 0.6f, 0.5f, 0f);
            _targetIcon.ReleaseFromCharacter(escapeVelocity);
            _iconHeld = false;
        }

        private void HideStolenIcon()
        {
            if (_targetIcon != null && _iconHeld)
            {
                _targetIcon.SetCatStolen(true);
            }
            _iconHeld = false;
        }

        public void StartShowcaseTheft()
        {
            var world = ProxyIconWorld.Instance;
            if (InteractiveWallpaperBootstrap.ShowcaseMode && _catInstance != null &&
                _state == CatState.Hidden && world != null)
            {
                BeginTheftAttempt(world);
            }
        }

        private void OnUserInput()
        {
            if (InteractiveWallpaperBootstrap.ShowcaseMode || _catInstance == null || _state == CatState.Hidden)
            {
                return;
            }
            SetState(CatState.Startled);
        }

        private void SetState(CatState state)
        {
            _state = state;
            _stateTime = 0f;
            if (_catInstance != null)
            {
                _catInstance.SetActive(state != CatState.Hidden);
            }
            Status = state switch
            {
                CatState.Hidden => "猫咪藏在桌面角落",
                CatState.EdgePeeking => "猫咪正在从屏幕边缘试探性探头",
                CatState.Watching => "猫咪正在观察随机目标图标",
                CatState.SneakingToTarget => "猫咪正在悄悄接近目标图标",
                CatState.ReachingPaw => "猫咪正在伸爪偷取图标",
                CatState.CarryingIcon => "猫咪正在叼着图标返回角落",
                CatState.HidingWithIcon => "猫咪正在把图标藏起来",
                CatState.Startled => "猫咪受到惊吓，松开图标",
                CatState.RunningAway => "猫咪正在逃回屏幕边缘",
                _ => "猫咪",
            };
            ApplyAnimatorState(state);
        }

        private void ApplyAnimatorState(CatState state)
        {
            if (_animator == null)
            {
                return;
            }
            _animator.ResetTrigger("Peek");
            _animator.ResetTrigger("Sneak");
            _animator.ResetTrigger("Reach");
            _animator.ResetTrigger("Grab");
            _animator.ResetTrigger("Startled");
            _animator.ResetTrigger("RunAway");
            switch (state)
            {
                case CatState.EdgePeeking: _animator.SetTrigger("Peek"); break;
                case CatState.SneakingToTarget: _animator.SetTrigger("Sneak"); break;
                case CatState.ReachingPaw: _animator.SetTrigger("Reach"); break;
                case CatState.CarryingIcon: _animator.SetTrigger("Grab"); break;
                case CatState.Startled: _animator.SetTrigger("Startled"); break;
                case CatState.RunningAway: _animator.SetTrigger("RunAway"); break;
            }
        }

        private void OnDestroy()
        {
            if (_inputMonitor != null)
            {
                _inputMonitor.InputDetected -= OnUserInput;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}







