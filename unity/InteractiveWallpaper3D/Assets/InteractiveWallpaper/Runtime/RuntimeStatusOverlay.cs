#nullable enable

using UnityEngine;

namespace InteractiveWallpaper
{
    public sealed class RuntimeStatusOverlay : MonoBehaviour
    {
        private const float PanelWidth = 6.4f;
        public static RuntimeStatusOverlay? Instance { get; private set; }
        private const float PanelHeight = 1.25f;
        private GameObject? _overlayRoot;
        private TextMesh? _text;
        private TextMesh? _shadow;
        private float _nextTextUpdate;
        private bool _pendingVisibilityToggle;

        private void Awake()
        {
            Instance = this;
        }

        public void ToggleVisibility()
        {
            if (_overlayRoot == null)
            {
                _pendingVisibilityToggle = !_pendingVisibilityToggle;
                return;
            }

            var visible = !_overlayRoot.activeSelf;
            _overlayRoot.SetActive(visible);
            Debug.Log(visible ? "Runtime status overlay visible" : "Runtime status overlay hidden");
        }
        private void Start()
        {
            var font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" },
                48);
            _overlayRoot = new GameObject("Runtime Status Overlay");
            DontDestroyOnLoad(_overlayRoot);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_overlayRoot.transform, false);
            var panelRenderer = panel.AddComponent<SpriteRenderer>();
            panelRenderer.sprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f),
                1f);
            panelRenderer.color = new Color(0.015f, 0.02f, 0.035f, 0.78f);
            panelRenderer.sortingOrder = 1998;
            panel.transform.localScale = new Vector3(PanelWidth * 0.5f, PanelHeight * 0.5f, 1f);

            _shadow = CreateText("Shadow", font, Color.black, 1999);
            _shadow.transform.SetParent(_overlayRoot.transform, false);
            _shadow.transform.localPosition = new Vector3(-PanelWidth * 0.5f + 0.11f, PanelHeight * 0.5f - 0.09f, -0.02f);

            _text = CreateText("Text", font, Color.white, 2000);
            _text.transform.SetParent(_overlayRoot.transform, false);
            _text.transform.localPosition = new Vector3(-PanelWidth * 0.5f + 0.09f, PanelHeight * 0.5f - 0.07f, -0.04f);
            UpdateText();
            PositionOverlay();
            if (_pendingVisibilityToggle)
            {
                _pendingVisibilityToggle = false;
                _overlayRoot.SetActive(false);
                Debug.Log("Runtime status overlay hidden");
            }
        }

        private void LateUpdate()
        {
            PositionOverlay();
            if (Time.unscaledTime >= _nextTextUpdate)
            {
                _nextTextUpdate = Time.unscaledTime + 0.2f;
                UpdateText();
            }
        }

        private static TextMesh CreateText(string name, Font font, Color color, int sortingOrder)
        {
            var textObject = new GameObject(name);
            var text = textObject.AddComponent<TextMesh>();
            text.font = font;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.fontSize = 48;
            text.characterSize = 0.026f;
            text.lineSpacing = 0.88f;
            text.color = color;
            text.richText = false;
            var renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = sortingOrder;
            return text;
        }

        private void PositionOverlay()
        {
            var camera = Camera.main;
            if (_overlayRoot == null || camera == null || !camera.orthographic)
            {
                return;
            }
            var worldHeight = camera.orthographicSize * 2f;
            var worldWidth = worldHeight * camera.aspect;
            _overlayRoot.transform.position = new Vector3(
                camera.transform.position.x - worldWidth * 0.5f + PanelWidth * 0.5f + 0.08f,
                camera.transform.position.y + worldHeight * 0.5f - PanelHeight * 0.5f - 0.08f,
                -1.2f);
            _overlayRoot.transform.rotation = Quaternion.identity;
        }

        private void UpdateText()
        {
            var bootstrap = InteractiveWallpaperBootstrap.Instance;
            if (bootstrap == null || _text == null || _shadow == null)
            {
                return;
            }
            var value =
                "交互式壁纸\n" +
                "桌面：" + (bootstrap.Snapshot?.itemCount.ToString() ?? "…") + " 项｜" + bootstrap.Status + "\n" +
                "快捷键 Ctrl+Alt：S喷嚏 G坠落 K踢 W扫 B冲击\n" +
                "P传送门 M机器人 T秋风 R复位 H面板 Q退出\n" +
                "人物：" + (InteractiveWallpaperBootstrap.HumanCharacterEnabled
                    ? (VideoCharacterPerformance.Instance?.ActionStatus ?? ProceduralCharacterController.Instance?.ActionStatus ?? "正在加载…")
                    : "已关闭（猫咪模式）") + "\n" +
                "图标：" + (ProxyIconWorld.Instance?.Status ?? "正在加载…");
            _text.text = value;
            _shadow.text = value;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_overlayRoot != null)
            {
                Destroy(_overlayRoot);
                _overlayRoot = null;
            }
        }
    }
}
