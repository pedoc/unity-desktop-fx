#nullable enable

using System;
using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class ProceduralCharacterController : MonoBehaviour
    {
        private enum ActionState
        {
            Idle,
            MovingIntoReach,
            Reaching,
            Holding,
            KickMoving,
            KickAnticipation,
            KickStrike,
            KickRecovery,
            Cooldown,
        }

        private const string PrimaryCharacterResourcePath = "Characters/MakeHumanDefault/DefaultHuman";
        private const string FallbackCharacterResourcePath = "Characters/KenneyBlocky/character-p";
        private const float CharacterHeight = 2.15f;
        private const float GrabDistance = 0.16f;
        private const float MaximumReach = 1.45f;
        private const float KickAnticipationDuration = 0.50f;
        private const float KickStrikeDuration = 0.32f;
        private const float KickRecoveryDuration = 0.55f;

        private Rigidbody? _rootBody;
        private Rigidbody? _handBody;
        private Transform? _visualRoot;
        private Vector3 _visualBaseLocalPosition;
        private Animator? _animator;
        private Transform? _rightUpperArm;
        private Transform? _rightLowerArm;
        private Transform? _rightHand;
        private Transform? _rightUpperLeg;
        private Transform? _rightLowerLeg;
        private Transform? _rightFoot;
        private Quaternion _upperArmBindRotation;
        private Quaternion _lowerArmBindRotation;
        private Quaternion _upperLegBindRotation;
        private Quaternion _lowerLegBindRotation;
        private float _upperArmLength;
        private float _lowerArmLength;
        private float _upperLegLength;
        private float _lowerLegLength;
        private Vector3 _rightFootRootOffset;
        private ProxyIconInteraction? _target;
        private ActionState _state;
        private Vector3 _desiredHandPosition;
        private Vector3 _desiredFootPosition;
        private Vector3 _kickIdleFootPosition;
        private Vector3 _kickBackFootPosition;
        private Vector3 _kickContactPosition;
        private Vector3 _kickFootVelocity;
        private float _desiredRootX;
        private float _stateTime;
        private float _cooldownUntil;
        private float _movementVelocityX;
        private bool _initialized;
        private bool _kickContactApplied;

        public static ProceduralCharacterController? Instance { get; private set; }
        public string ActionStatus { get; private set; } = "待机";

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _rootBody = gameObject.AddComponent<Rigidbody>();
            _rootBody.mass = 10f;
            _rootBody.linearDamping = 3f;
            _rootBody.angularDamping = 8f;
            _rootBody.useGravity = false;
            _rootBody.isKinematic = true;
            _rootBody.interpolation = RigidbodyInterpolation.Interpolate;
            _rootBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rootBody.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;

            var collider = gameObject.AddComponent<CapsuleCollider>();
            collider.radius = 0.34f;
            collider.height = 1.95f;
            collider.center = new Vector3(0f, 0.98f, 0f);

            var handAnchor = new GameObject("Character Hand Physics Anchor");
            _handBody = handAnchor.AddComponent<Rigidbody>();
            _handBody.isKinematic = true;
            _handBody.useGravity = false;
            _handBody.interpolation = RigidbodyInterpolation.Interpolate;

            CreateCharacterVisual();
            _state = ActionState.Idle;
        }

        public bool RequestKick(ProxyIconInteraction target)
        {
            var world = ProxyIconWorld.Instance;
            if (_state is ActionState.KickMoving or ActionState.KickAnticipation or ActionState.KickStrike or ActionState.KickRecovery)
            {
                return false;
            }
            if (!_initialized || _rootBody == null || world == null || target == null || !target.CanCharacterGrab)
            {
                Debug.Log("Character kick rejected: controller or target is not ready.");
                return false;
            }

            var relativeHeight = target.WorldPosition.y - transform.position.y;
            if (relativeHeight < 0.08f || relativeHeight > 1.55f)
            {
                ActionStatus = "目标高度不适合踢击";
                Debug.Log($"Character kick rejected: target height {relativeHeight:F2} is outside the kick range.");
                return false;
            }

            if (_state == ActionState.Holding && _target != null)
            {
                _target.ReleaseFromCharacter(Vector3.zero);
            }

            _target = target;
            var standOffset = target.WorldPosition.x >= 0f ? -0.78f : 0.78f;
            _desiredRootX = Mathf.Clamp(
                target.WorldPosition.x + standOffset,
                -world.WorldWidth * 0.5f + 0.6f,
                world.WorldWidth * 0.5f - 0.6f);
            _kickContactApplied = false;
            ActionStatus = $"正在接近 {target.DisplayName}";
            Debug.Log($"Character kick accepted: {target.DisplayName}; targetHeight={relativeHeight:F2}");
            ChangeState(ActionState.KickMoving);
            return true;
        }

        private void CreateCharacterVisual()
        {
            var prefab = Resources.Load<GameObject>(PrimaryCharacterResourcePath);
            var characterName = "MakeHuman Default Character";
            if (prefab == null)
            {
                Debug.LogWarning($"Primary character model was not found: {PrimaryCharacterResourcePath}; using fallback.");
                prefab = Resources.Load<GameObject>(FallbackCharacterResourcePath);
                characterName = "Kenney Blocky Character";
            }
            if (prefab == null)
            {
                Debug.LogError($"No character model was found in Resources: {PrimaryCharacterResourcePath} or {FallbackCharacterResourcePath}");
                return;
            }

            var instance = Instantiate(prefab, transform, false);
            instance.name = characterName;
            _visualRoot = instance.transform;
            _visualRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            FitVisualToHeight(instance, CharacterHeight);
            ApplyUrpMaterials(instance);
            _visualBaseLocalPosition = _visualRoot.localPosition;

            _animator = instance.GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman)
            {
                return;
            }

            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (_rightUpperArm != null && _rightLowerArm != null && _rightHand != null)
            {
                _upperArmBindRotation = _rightUpperArm.localRotation;
                _lowerArmBindRotation = _rightLowerArm.localRotation;
                _upperArmLength = Vector3.Distance(_rightUpperArm.position, _rightLowerArm.position);
                _lowerArmLength = Vector3.Distance(_rightLowerArm.position, _rightHand.position);
            }

            _rightUpperLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rightLowerLeg = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (_rightUpperLeg != null && _rightLowerLeg != null && _rightFoot != null)
            {
                _upperLegBindRotation = _rightUpperLeg.localRotation;
                _lowerLegBindRotation = _rightLowerLeg.localRotation;
                _upperLegLength = Vector3.Distance(_rightUpperLeg.position, _rightLowerLeg.position);
                _lowerLegLength = Vector3.Distance(_rightLowerLeg.position, _rightFoot.position);
                _rightFootRootOffset = transform.InverseTransformPoint(_rightFoot.position);
            }
        }

        private void Update()
        {
            var world = ProxyIconWorld.Instance;
            if (world == null || world.WorldHeight <= 0f || _rootBody == null || _handBody == null)
            {
                return;
            }

            if (!_initialized)
            {
                var floorY = -world.WorldHeight * 0.5f;
                transform.position = new Vector3(world.WorldWidth * 0.32f, floorY + 0.02f, -0.15f);
                _desiredRootX = transform.position.x;
                _desiredHandPosition = IdleHandPosition();
                _desiredFootPosition = IdleFootPosition();
                _handBody.position = _desiredHandPosition;
                _initialized = true;
            }

            _stateTime += Time.deltaTime;
            switch (_state)
            {
                case ActionState.Idle:
                    ActionStatus = "待机";
                    SetHandTarget(Vector3.Lerp(_desiredHandPosition, IdleHandPosition(), Time.deltaTime * 5f));
                    SetFootTarget(Vector3.Lerp(_desiredFootPosition, IdleFootPosition(), Time.deltaTime * 6f));
                    if (Time.unscaledTime >= _cooldownUntil && world.Selected?.CanCharacterGrab == true)
                    {
                        _target = world.Selected;
                        if (IsReachableHeight(_target.WorldPosition))
                        {
                            _desiredRootX = Mathf.Clamp(
                                _target.WorldPosition.x + 0.9f,
                                -world.WorldWidth * 0.5f + 0.6f,
                                world.WorldWidth * 0.5f - 0.6f);
                            ChangeState(ActionState.MovingIntoReach);
                        }
                        else
                        {
                            BeginCooldown();
                        }
                    }
                    break;

                case ActionState.MovingIntoReach:
                    ActionStatus = "正在接近抓取目标";
                    if (_target == null || !_target.CanCharacterGrab)
                    {
                        BeginCooldown();
                        break;
                    }
                    SetHandTarget(Vector3.Lerp(_desiredHandPosition, IdleHandPosition(), Time.deltaTime * 5f));
                    if (ReachedDesiredRootPosition())
                    {
                        ChangeState(ActionState.Reaching);
                    }
                    break;

                case ActionState.Reaching:
                    ActionStatus = "正在伸手";
                    if (_target == null || !_target.CanCharacterGrab || !IsReachableFromShoulder(_target.WorldPosition))
                    {
                        BeginCooldown();
                        break;
                    }
                    SetHandTarget(Vector3.MoveTowards(
                        _desiredHandPosition,
                        _target.WorldPosition,
                        Time.deltaTime * 1.9f));
                    if (Vector3.Distance(_desiredHandPosition, _target.WorldPosition) <= GrabDistance &&
                        _target.GrabByCharacter(_handBody))
                    {
                        ChangeState(ActionState.Holding);
                    }
                    break;

                case ActionState.Holding:
                    ActionStatus = "持有图标";
                    if (_target == null)
                    {
                        BeginCooldown();
                        break;
                    }
                    var holdPosition = transform.position + new Vector3(-0.42f, 1.25f, 0f);
                    SetHandTarget(Vector3.Lerp(_desiredHandPosition, holdPosition, Time.deltaTime * 3.5f));
                    if (_stateTime >= 1.8f)
                    {
                        _target.ReleaseFromCharacter(new Vector3(-0.8f, 1.6f, 0f));
                        _target = null;
                        BeginCooldown();
                    }
                    break;

                case ActionState.KickMoving:
                    ActionStatus = "正在移动到踢击位置";
                    SetHandTarget(Vector3.Lerp(_desiredHandPosition, IdleHandPosition(), Time.deltaTime * 5f));
                    SetFootTarget(Vector3.Lerp(_desiredFootPosition, IdleFootPosition(), Time.deltaTime * 8f));
                    if (_target == null || !_target.CanCharacterGrab)
                    {
                        BeginCooldown();
                        break;
                    }
                    if (ReachedDesiredRootPosition())
                    {
                        _kickIdleFootPosition = IdleFootPosition();
                        var kickDirection = Mathf.Sign(_target.WorldPosition.x - transform.position.x);
                        if (Mathf.Approximately(kickDirection, 0f))
                        {
                            kickDirection = -1f;
                        }
                        _kickBackFootPosition = _kickIdleFootPosition + new Vector3(-kickDirection * 0.28f, 0.16f, 0f);
                        _kickContactPosition = _target.WorldPosition;
                        ActionStatus = "踢击蓄力";
                        ChangeState(ActionState.KickAnticipation);
                    }
                    break;

                case ActionState.KickAnticipation:
                    ActionStatus = "踢击蓄力";
                    SetFootTarget(Vector3.Lerp(
                        _kickIdleFootPosition,
                        _kickBackFootPosition,
                        CharacterImpactMath.SmoothActionProgress(_stateTime, KickAnticipationDuration)));
                    if (_stateTime >= KickAnticipationDuration)
                    {
                        ChangeState(ActionState.KickStrike);
                    }
                    break;

                case ActionState.KickStrike:
                    ActionStatus = "踢击接触";
                    if (_target == null || !_target.CanCharacterGrab)
                    {
                        ChangeState(ActionState.KickRecovery);
                        break;
                    }
                    _kickContactPosition = _target.WorldPosition;
                    var strikeProgress = CharacterImpactMath.SmoothActionProgress(_stateTime, KickStrikeDuration);
                    SetFootTarget(Vector3.Lerp(_kickBackFootPosition, _kickContactPosition, strikeProgress));
                    if (!_kickContactApplied && strikeProgress >= 0.68f)
                    {
                        _kickContactApplied = true;
                        var contactVelocity = Vector3.ClampMagnitude(_kickFootVelocity, 9f);
                        if (_target.ApplyCharacterImpact(contactVelocity, 3.2f, out var impulse))
                        {
                            DesktopEffectFeedback.EmitBurst(
                                _target.WorldPosition + new Vector3(0f, 0f, -0.2f),
                                new Color(1f, 0.48f, 0.12f),
                                22,
                                3.1f);
                            Debug.Log($"Character kick contact: {_target.DisplayName}; footVelocity={contactVelocity.magnitude:F2}; impulse={impulse.magnitude:F2}");
                        }
                    }
                    if (_stateTime >= KickStrikeDuration)
                    {
                        ChangeState(ActionState.KickRecovery);
                    }
                    break;

                case ActionState.KickRecovery:
                    ActionStatus = "踢击收势";
                    SetFootTarget(Vector3.Lerp(
                        _kickContactPosition,
                        _kickIdleFootPosition,
                        CharacterImpactMath.SmoothActionProgress(_stateTime, KickRecoveryDuration)));
                    if (_stateTime >= KickRecoveryDuration)
                    {
                        _target = null;
                        BeginCooldown();
                    }
                    break;

                case ActionState.Cooldown:
                    ActionStatus = "动作恢复";
                    SetHandTarget(Vector3.Lerp(_desiredHandPosition, IdleHandPosition(), Time.deltaTime * 4f));
                    SetFootTarget(Vector3.Lerp(_desiredFootPosition, IdleFootPosition(), Time.deltaTime * 6f));
                    if (Time.unscaledTime >= _cooldownUntil)
                    {
                        ChangeState(ActionState.Idle);
                    }
                    break;
            }

            UpdateVisualMotion();
        }

        private void FixedUpdate()
        {
            if (!_initialized || _rootBody == null || _handBody == null)
            {
                return;
            }

            var shouldMove = _state is ActionState.MovingIntoReach or ActionState.KickMoving;
            var position = _rootBody.position;
            var nextX = shouldMove
                ? Mathf.MoveTowards(position.x, _desiredRootX, Time.fixedDeltaTime * 2.0f)
                : position.x;
            _movementVelocityX = (nextX - position.x) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            _rootBody.MovePosition(new Vector3(nextX, position.y, position.z));
            _handBody.MovePosition(_desiredHandPosition);
        }

        private void LateUpdate()
        {
            if (_rightUpperArm != null && _rightLowerArm != null && _rightHand != null)
            {
                if (_state is ActionState.Reaching or ActionState.Holding)
                {
                    ApplyTwoBoneIk(
                        _rightUpperArm,
                        _rightLowerArm,
                        _rightHand,
                        _desiredHandPosition,
                        _upperArmLength,
                        _lowerArmLength);
                }
                else
                {
                    _rightUpperArm.localRotation = Quaternion.Slerp(
                        _rightUpperArm.localRotation,
                        _upperArmBindRotation,
                        Time.deltaTime * 5f);
                    _rightLowerArm.localRotation = Quaternion.Slerp(
                        _rightLowerArm.localRotation,
                        _lowerArmBindRotation,
                        Time.deltaTime * 5f);
                }
            }

            if (_rightUpperLeg != null && _rightLowerLeg != null && _rightFoot != null)
            {
                if (_state is ActionState.KickAnticipation or ActionState.KickStrike or ActionState.KickRecovery)
                {
                    ApplyTwoBoneIk(
                        _rightUpperLeg,
                        _rightLowerLeg,
                        _rightFoot,
                        _desiredFootPosition,
                        _upperLegLength,
                        _lowerLegLength);
                }
                else
                {
                    _rightUpperLeg.localRotation = Quaternion.Slerp(
                        _rightUpperLeg.localRotation,
                        _upperLegBindRotation,
                        Time.deltaTime * 7f);
                    _rightLowerLeg.localRotation = Quaternion.Slerp(
                        _rightLowerLeg.localRotation,
                        _lowerLegBindRotation,
                        Time.deltaTime * 7f);
                }
            }
        }

        private void SetHandTarget(Vector3 target)
        {
            _desiredHandPosition = target;
        }

        private void SetFootTarget(Vector3 target)
        {
            var deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            _kickFootVelocity = (target - _desiredFootPosition) / deltaTime;
            _desiredFootPosition = target;
        }

        private bool ReachedDesiredRootPosition()
        {
            return _rootBody != null &&
                Mathf.Abs(_rootBody.position.x - _desiredRootX) < 0.08f &&
                Mathf.Abs(_movementVelocityX) < 0.25f;
        }

        private bool IsReachableHeight(Vector3 target)
        {
            return target.y <= transform.position.y + CharacterHeight + 0.15f;
        }

        private bool IsReachableFromShoulder(Vector3 target)
        {
            var shoulder = _rightUpperArm != null
                ? _rightUpperArm.position
                : transform.position + new Vector3(-0.25f, 1.45f, 0f);
            return Vector2.Distance(shoulder, target) <= MaximumReach;
        }

        private static void ApplyTwoBoneIk(
            Transform upper,
            Transform lower,
            Transform end,
            Vector3 target,
            float upperLength,
            float lowerLength)
        {
            if (upperLength <= 0f || lowerLength <= 0f)
            {
                return;
            }
            var root = upper.position;
            SolveTwoBoneIk(root, target, upperLength, lowerLength, out var joint, out var solvedEnd);
            RotateBoneTowards(upper, lower.position - root, joint - root);
            var lowerPosition = lower.position;
            RotateBoneTowards(lower, end.position - lowerPosition, solvedEnd - lowerPosition);
        }

        private static void RotateBoneTowards(Transform bone, Vector3 currentDirection, Vector3 targetDirection)
        {
            if (currentDirection.sqrMagnitude < 0.000001f || targetDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }
            bone.rotation = Quaternion.FromToRotation(currentDirection, targetDirection) * bone.rotation;
        }

        private void UpdateVisualMotion()
        {
            if (_visualRoot == null)
            {
                return;
            }
            var speed = Mathf.Abs(_movementVelocityX);
            var bob = speed > 0.1f
                ? Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.055f
                : Mathf.Sin(Time.time * 2.2f) * 0.018f;
            _visualRoot.localPosition = _visualBaseLocalPosition + Vector3.up * bob;
            var movementLean = Mathf.Clamp(-_movementVelocityX * 4f, -7f, 7f);
            var kickLean = _state switch
            {
                ActionState.KickAnticipation => -7f,
                ActionState.KickStrike => 5f,
                ActionState.KickRecovery => 2f,
                _ => 0f,
            };
            _visualRoot.localRotation = Quaternion.Euler(0f, 180f, movementLean + kickLean);
        }

        private Vector3 IdleHandPosition()
        {
            return transform.position + new Vector3(-0.48f, 1.08f, 0f);
        }

        private Vector3 IdleFootPosition()
        {
            if (_rightFootRootOffset != Vector3.zero)
            {
                return transform.TransformPoint(_rightFootRootOffset);
            }
            return transform.position + new Vector3(-0.2f, 0.12f, 0f);
        }

        private void ChangeState(ActionState state)
        {
            _state = state;
            _stateTime = 0f;
        }

        private void BeginCooldown()
        {
            _target = null;
            _cooldownUntil = Time.unscaledTime + 1.1f;
            ChangeState(ActionState.Cooldown);
        }

        private static void FitVisualToHeight(GameObject instance, float targetHeight)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            if (bounds.size.y <= 0.0001f)
            {
                return;
            }

            var sourceHeight = bounds.size.y;
            var sourceMinimumY = bounds.min.y - instance.transform.position.y;
            var scale = targetHeight / sourceHeight;
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = Vector3.up * (-sourceMinimumY * scale);
            Debug.Log($"Character visual fitted: sourceHeight={sourceHeight:F3}, scale={scale:F3}, footOffset={instance.transform.localPosition.y:F3}");
        }

        private static void ApplyUrpMaterials(GameObject instance)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return;
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials.Length == 0)
                {
                    sourceMaterials = new Material[] { null! };
                }

                var convertedMaterials = new Material[sourceMaterials.Length];
                for (var index = 0; index < sourceMaterials.Length; index++)
                {
                    convertedMaterials[index] = CreateUrpMaterial(shader, sourceMaterials[index], renderer.name);
                }
                renderer.sharedMaterials = convertedMaterials;
            }
        }

        private static Material CreateUrpMaterial(Shader shader, Material? source, string rendererName)
        {
            var materialName = source != null ? source.name : rendererName;
            var material = new Material(shader)
            {
                name = materialName + " URP",
            };

            if (source?.mainTexture != null)
            {
                material.SetTexture("_BaseMap", source.mainTexture);
            }
            var color = source != null && source.HasProperty("_Color")
                ? source.color
                : Color.white;
            material.SetColor("_BaseColor", color);

            Texture? normalMap = null;
            if (source != null && source.HasProperty("_BumpMap"))
            {
                normalMap = source.GetTexture("_BumpMap");
            }
            if (normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
                material.EnableKeyword("_NORMALMAP");
            }

            var materialIdentity = (materialName + " " + rendererName).ToLowerInvariant();
            var isHair = materialIdentity.Contains("hair") || materialIdentity.Contains("bob");
            material.SetFloat("_Smoothness", isHair ? 0.24f : 0.16f);
            if (isHair)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.35f);
                material.SetFloat("_Cull", 0f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.doubleSidedGI = true;
            }
            return material;
        }
        private static void SolveTwoBoneIk(
            Vector3 root,
            Vector3 requestedTarget,
            float upperLength,
            float lowerLength,
            out Vector3 joint,
            out Vector3 end)
        {
            var toTarget = requestedTarget - root;
            toTarget.z = 0f;
            var distance = Mathf.Clamp(
                toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.001f,
                upperLength + lowerLength - 0.001f);
            var direction = toTarget.sqrMagnitude > 0.000001f
                ? toTarget.normalized
                : Vector3.left;
            end = root + direction * distance;

            var along =
                (upperLength * upperLength - lowerLength * lowerLength + distance * distance) /
                (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var perpendicular = new Vector3(-direction.y, direction.x, 0f);
            joint = root + direction * along + perpendicular * height;
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