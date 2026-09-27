#nullable enable

using UnityEngine;

namespace InteractiveWallpaper
{
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class ProxyIconInteraction : MonoBehaviour
    {
        private const float DoubleClickWindowSeconds = 0.45f;
        private const float DragThresholdPixels = 4f;

        private readonly ProxyIconStateMachine _stateMachine = new ProxyIconStateMachine();
        private string _stableId = string.Empty;
        private string _displayName = string.Empty;
        private SpriteRenderer? _renderer;
        private Rigidbody? _body;
        private Rigidbody? _characterAnchor;
        private ConfigurableJoint? _grabJoint;
        private Vector3 _desktopPosition;
        private Vector3 _mouseDownScreenPosition;
        private float _lastClickReleaseTime = float.NegativeInfinity;
        private bool _dragStarted;
        private Color _baseColor = Color.white;
        private bool _catStolen;
        private Vector3 _portalOriginalLocalPosition;
        private Quaternion _portalOriginalLocalRotation;
        private Vector3 _portalOriginalLocalScale;
        private bool _portalOriginalColliderEnabled;
        private Renderer[]? _portalRenderers;
        private int[]? _portalSortingOrders;
        private bool[]? _portalRendererEnabled;
        private bool _portalHoldActive;
        private bool _windPhysicsActive;
        private float _originalLinearDamping;
        private float _originalAngularDamping;

        public string DisplayName => _displayName;
        public ProxyIconMotionState MotionState => _stateMachine.State;
        public Vector3 WorldPosition => transform.position;
        public Vector3 DesktopWorldPosition => transform.parent != null
            ? transform.parent.TransformPoint(_desktopPosition)
            : _desktopPosition;
        public Vector2 NavigationHalfExtents =>
            _renderer != null
                ? new Vector2(Mathf.Max(0.18f, _renderer.bounds.extents.x), Mathf.Max(0.22f, _renderer.bounds.extents.y))
                : new Vector2(0.24f, 0.28f);
        public bool IsCatStolen => _catStolen;
        public bool CanCharacterGrab =>
            !_catStolen &&
            _stateMachine.State is ProxyIconMotionState.DesktopPinned or ProxyIconMotionState.Dynamic;

        public void Initialize(
            string stableId,
            string displayName,
            Vector3 desktopPosition,
            SpriteRenderer spriteRenderer)
        {
            _stableId = stableId;
            _displayName = displayName;
            _desktopPosition = desktopPosition;
            _renderer = spriteRenderer;
            _baseColor = spriteRenderer.color;
            _catStolen = false;
            _body = GetComponent<Rigidbody>();
            if (_body != null)
            {
                _originalLinearDamping = _body.linearDamping;
                _originalAngularDamping = _body.angularDamping;
            }
            ConfigurePinnedBody();
            ProxyIconWorld.Instance?.Register(this);
        }

        public void SetSelected(bool selected)
        {
            if (_renderer != null)
            {
                _renderer.color = selected
                    ? Color.Lerp(_baseColor, new Color(0.35f, 0.75f, 1f, 1f), 0.38f)
                    : _baseColor;
            }
        }

        public void SetDynamic(Vector3 impulse)
        {
            if (_body == null)
            {
                return;
            }
            if (_windPhysicsActive)
            {
                RestoreWindPhysicsTuning();
            }
            if (!_stateMachine.TryTransition(ProxyIconMotionState.Dynamic))
            {
                return;
            }
            ClearGrabJoint();
            _characterAnchor = null;
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationY;
            _body.AddForce(impulse, ForceMode.Impulse);
            ProxyIconWorld.Instance?.NotifyStateChanged(this);
        }

        public bool ApplyWindAcceleration(Vector3 acceleration, float angularAcceleration)
        {
            if (_body == null || !CanCharacterGrab)
            {
                return false;
            }

            if (!BeginWindPhysics())
            {
                return false;
            }

            _body.AddForce(acceleration, ForceMode.Acceleration);
            _body.AddTorque(Vector3.forward * angularAcceleration, ForceMode.Acceleration);
            return true;
        }

        public bool ApplyWindImpulse(Vector3 impulse, float angularImpulse)
        {
            if (_body == null || !CanCharacterGrab)
            {
                return false;
            }

            if (!BeginWindPhysics())
            {
                return false;
            }

            // Apply the impulse directly as a velocity change as well as the ongoing
            // forces; this makes a short wind-front impact visible even between fixed ticks.
            _body.linearVelocity += impulse / Mathf.Max(0.01f, _body.mass);
            _body.AddTorque(Vector3.forward * angularImpulse, ForceMode.Impulse);
            _body.WakeUp();
            return true;
        }

        public bool BeginVisualCharacterHold()
        {
            if (_body == null || !CanCharacterGrab ||
                !_stateMachine.TryTransition(ProxyIconMotionState.CharacterGrabbed))
            {
                return false;
            }

            ClearGrabJoint();
            _characterAnchor = null;
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.constraints = RigidbodyConstraints.FreezeAll;
            if (_renderer != null)
            {
                _renderer.sortingOrder = 1100;
            }
            ProxyIconWorld.Instance?.NotifyStateChanged(this);
            return true;
        }

        public void SetVisualCharacterHold(Vector3 worldPosition, Quaternion rotation)
        {
            transform.SetPositionAndRotation(worldPosition, rotation);
            if (_body != null)
            {
                _body.position = worldPosition;
                _body.rotation = rotation;
            }
        }

        public bool BeginPortalHold()
        {
            // Portal travel is a visual desktop event. Do not interrupt an icon already
            // being dragged, simulated, held by the cat, or hidden.
            if (_portalHoldActive || _stateMachine.State != ProxyIconMotionState.DesktopPinned)
            {
                return false;
            }

            _portalOriginalLocalPosition = transform.localPosition;
            _portalOriginalLocalRotation = transform.localRotation;
            _portalOriginalLocalScale = transform.localScale;
            _portalRenderers = GetComponentsInChildren<Renderer>(true);
            _portalSortingOrders = new int[_portalRenderers.Length];
            _portalRendererEnabled = new bool[_portalRenderers.Length];
            for (var index = 0; index < _portalRenderers.Length; index++)
            {
                _portalSortingOrders[index] = _portalRenderers[index].sortingOrder;
                _portalRendererEnabled[index] = _portalRenderers[index].enabled;
            }

            if (!BeginVisualCharacterHold())
            {
                _portalRenderers = null;
                _portalSortingOrders = null;
                _portalRendererEnabled = null;
                return false;
            }

            var collider = GetComponent<Collider>();
            _portalOriginalColliderEnabled = collider != null && collider.enabled;
            if (collider != null)
            {
                collider.enabled = false;
            }
            for (var index = 0; index < _portalRenderers.Length; index++)
            {
                _portalRenderers[index].sortingOrder = 1100 + index;
            }
            _portalHoldActive = true;
            return true;
        }

        public void SetPortalVisual(Vector3 worldPosition, Quaternion rotation, float scale, bool visible)
        {
            if (!_portalHoldActive)
            {
                return;
            }

            transform.SetPositionAndRotation(worldPosition, rotation);
            transform.localScale = _portalOriginalLocalScale * Mathf.Max(0.001f, scale);
            if (_body != null)
            {
                _body.position = worldPosition;
                _body.rotation = rotation;
            }

            if (_portalRenderers != null && _portalRendererEnabled != null)
            {
                for (var index = 0; index < _portalRenderers.Length; index++)
                {
                    if (_portalRenderers[index] != null)
                    {
                        _portalRenderers[index].enabled = visible && _portalRendererEnabled[index];
                    }
                }
            }
        }

        public void CompletePortalHold()
        {
            if (!_portalHoldActive)
            {
                return;
            }

            CompleteVisualDesktopRestore();
            transform.localPosition = _portalOriginalLocalPosition;
            transform.localRotation = _portalOriginalLocalRotation;
            transform.localScale = _portalOriginalLocalScale;

            if (_portalRenderers != null && _portalSortingOrders != null && _portalRendererEnabled != null)
            {
                for (var index = 0; index < _portalRenderers.Length; index++)
                {
                    if (_portalRenderers[index] != null)
                    {
                        _portalRenderers[index].sortingOrder = _portalSortingOrders[index];
                        _portalRenderers[index].enabled = _portalRendererEnabled[index];
                    }
                }
            }

            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = _portalOriginalColliderEnabled;
            }
            _portalRenderers = null;
            _portalSortingOrders = null;
            _portalRendererEnabled = null;
            _portalHoldActive = false;
        }

        public void SetCatStolen(bool stolen)
        {
            _catStolen = stolen;
            if (_renderer != null)
            {
                _renderer.enabled = !stolen;
            }
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = !stolen;
            }
            if (stolen)
            {
                ClearGrabJoint();
                _characterAnchor = null;
            }
        }
        public void CompleteVisualDesktopRestore()
        {
            RestoreWindPhysicsTuning();
            _catStolen = false;
            _stateMachine.TryTransition(ProxyIconMotionState.DesktopPinned);
            ClearGrabJoint();
            _characterAnchor = null;
            transform.localPosition = _desktopPosition;
            transform.localRotation = Quaternion.identity;
            if (_renderer != null)
            {
                _renderer.sortingOrder = 10;
            }
            ConfigurePinnedBody();
            if (_renderer != null)
            {
                _renderer.enabled = true;
            }
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = true;
            }
            ProxyIconWorld.Instance?.NotifyStateChanged(this);
        }

        public bool GrabByCharacter(Rigidbody anchor)
        {
            if (!_stateMachine.TryTransition(ProxyIconMotionState.CharacterGrabbed) || _body == null)
            {
                return false;
            }

            ClearGrabJoint();
            _characterAnchor = anchor;
            _body.isKinematic = false;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationY;

            _grabJoint = gameObject.AddComponent<ConfigurableJoint>();
            _grabJoint.connectedBody = anchor;
            _grabJoint.autoConfigureConnectedAnchor = false;
            _grabJoint.anchor = Vector3.zero;
            _grabJoint.connectedAnchor = Vector3.zero;
            _grabJoint.xMotion = ConfigurableJointMotion.Limited;
            _grabJoint.yMotion = ConfigurableJointMotion.Limited;
            _grabJoint.zMotion = ConfigurableJointMotion.Locked;
            _grabJoint.angularXMotion = ConfigurableJointMotion.Locked;
            _grabJoint.angularYMotion = ConfigurableJointMotion.Locked;
            _grabJoint.angularZMotion = ConfigurableJointMotion.Limited;
            _grabJoint.linearLimit = new SoftJointLimit { limit = 0.08f };
            _grabJoint.angularZLimit = new SoftJointLimit { limit = 10f };
            var drive = new JointDrive
            {
                positionSpring = 220f,
                positionDamper = 28f,
                maximumForce = 600f,
            };
            _grabJoint.xDrive = drive;
            _grabJoint.yDrive = drive;
            _grabJoint.zDrive = drive;
            _grabJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            _grabJoint.projectionDistance = 0.12f;
            _grabJoint.projectionAngle = 12f;
            _body.WakeUp();
            ProxyIconWorld.Instance?.NotifyStateChanged(this);
            return true;
        }

        public bool ApplyCharacterImpact(
            Vector3 limbVelocity,
            float strength,
            out Vector3 appliedImpulse)
        {
            appliedImpulse = Vector3.zero;
            if (_body == null || !CanCharacterGrab)
            {
                return false;
            }

            var targetVelocity = _body.isKinematic ? Vector3.zero : _body.linearVelocity;
            SetDynamic(Vector3.zero);
            appliedImpulse = CharacterImpactMath.ComputeImpulse(
                _body.mass,
                limbVelocity,
                targetVelocity,
                strength,
                3.6f);
            _body.AddForce(appliedImpulse, ForceMode.Impulse);
            _body.AddTorque(
                new Vector3(0f, 0f, -appliedImpulse.x * 0.18f),
                ForceMode.Impulse);
            return true;
        }
        public void ReleaseFromCharacter(Vector3 velocity)
        {
            if (_stateMachine.State != ProxyIconMotionState.CharacterGrabbed)
            {
                return;
            }
            var inheritedVelocity = _characterAnchor != null ? _characterAnchor.linearVelocity : Vector3.zero;
            ClearGrabJoint();
            _characterAnchor = null;
            SetDynamic(Vector3.zero);
            if (_body != null)
            {
                _body.linearVelocity = inheritedVelocity + velocity;
            }
        }

        public void ResetToDesktopPosition()
        {
            RestoreWindPhysicsTuning();
            _catStolen = false;
            _stateMachine.TryTransition(ProxyIconMotionState.DesktopPinned);
            ClearGrabJoint();
            _characterAnchor = null;
            transform.localPosition = _desktopPosition;
            transform.localRotation = Quaternion.identity;
            ConfigurePinnedBody();
            if (_renderer != null)
            {
                _renderer.enabled = true;
            }
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = true;
            }
            ProxyIconWorld.Instance?.NotifyStateChanged(this);
        }

        private bool BeginWindPhysics()
        {
            if (_body == null || !CanCharacterGrab)
            {
                return false;
            }

            var wasWindDriven = _windPhysicsActive;
            if (_stateMachine.State != ProxyIconMotionState.Dynamic)
            {
                if (!_stateMachine.TryTransition(ProxyIconMotionState.Dynamic))
                {
                    return false;
                }
                ClearGrabJoint();
                _characterAnchor = null;
                ProxyIconWorld.Instance?.NotifyStateChanged(this);
            }

            _body.isKinematic = false;
            _body.useGravity = false;
            _body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationY;
            _body.linearDamping = Mathf.Max(_originalLinearDamping, 1.35f);
            _body.angularDamping = Mathf.Max(_originalAngularDamping, 1.8f);
            _windPhysicsActive = true;
            if (!wasWindDriven)
            {
                _body.WakeUp();
            }
            return true;
        }

        public void EndWindPhysics()
        {
            if (!_windPhysicsActive || _body == null)
            {
                return;
            }
            RestoreWindPhysicsTuning();
            _body.useGravity = true;
            _body.WakeUp();
        }

        private void RestoreWindPhysicsTuning()
        {
            if (!_windPhysicsActive)
            {
                return;
            }
            _windPhysicsActive = false;
            if (_body != null)
            {
                _body.linearDamping = _originalLinearDamping;
                _body.angularDamping = _originalAngularDamping;
            }
        }

        private void ConfigurePinnedBody()
        {
            if (_body == null)
            {
                return;
            }
            ClearGrabJoint();
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.constraints = RigidbodyConstraints.FreezeAll;
        }

        private void ClearGrabJoint()
        {
            if (_grabJoint == null)
            {
                return;
            }
            _grabJoint.connectedBody = null;
            Destroy(_grabJoint);
            _grabJoint = null;
        }

        private void OnMouseDown()
        {
            ProxyIconWorld.Instance?.Select(this);
            _mouseDownScreenPosition = Input.mousePosition;
            _dragStarted = false;
        }

        private void OnMouseDrag()
        {
            var camera = Camera.main;
            if (camera == null || _body == null)
            {
                return;
            }
            if (!_dragStarted &&
                Vector3.Distance(_mouseDownScreenPosition, Input.mousePosition) < DragThresholdPixels)
            {
                return;
            }

            if (!_dragStarted)
            {
                if (!_stateMachine.TryTransition(ProxyIconMotionState.UserDragging))
                {
                    return;
                }
                _dragStarted = true;
                ClearGrabJoint();
                _body.isKinematic = true;
                _body.useGravity = false;
                _body.constraints = RigidbodyConstraints.FreezeAll;
                ProxyIconWorld.Instance?.NotifyStateChanged(this);
            }

            var screen = Input.mousePosition;
            screen.z = Mathf.Abs(camera.transform.position.z - transform.position.z);
            var world = camera.ScreenToWorldPoint(screen);
            transform.position = new Vector3(world.x, world.y, transform.position.z);
        }

        private void OnMouseUp()
        {
            if (_dragStarted)
            {
                _stateMachine.TryTransition(ProxyIconMotionState.DesktopPinned);
                ConfigurePinnedBody();
                _lastClickReleaseTime = float.NegativeInfinity;
                ProxyIconWorld.Instance?.NotifyStateChanged(this);
                return;
            }

            var now = Time.unscaledTime;
            if (now - _lastClickReleaseTime <= DoubleClickWindowSeconds)
            {
                InteractiveWallpaperBootstrap.Instance?.RequestOpenDesktopItem(_stableId, _displayName);
                _lastClickReleaseTime = float.NegativeInfinity;
            }
            else
            {
                _lastClickReleaseTime = now;
            }
        }
    }
}

