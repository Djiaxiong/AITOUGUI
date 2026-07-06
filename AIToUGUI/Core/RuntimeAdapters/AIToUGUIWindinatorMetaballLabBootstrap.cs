using System;
using AIToUGUI;
using Riten.Windinator.Shapes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AIToUGUI
{
    public sealed class AIToUGUIWindinatorMetaballLabBootstrap : MonoBehaviour
    {
        private const string DemoSceneName = "AIToUGUI_WindinatorMetaballLab";
        private const string DemoRootName = "AIToUGUI_WindinatorMetaballLab";

        private Font _defaultFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapOnLabScene()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!string.Equals(scene.name, DemoSceneName, StringComparison.Ordinal))
            {
                return;
            }

            if (FindObjectOfType<AIToUGUIWindinatorMetaballLabBootstrap>() != null)
            {
                return;
            }

            var root = new GameObject(DemoRootName, typeof(RectTransform));
            root.AddComponent<AIToUGUIWindinatorMetaballLabBootstrap>().Build();
        }

        private void Build()
        {
            var layerRoot = UISystem.Instance.GetLayerRoot(UILayer.Normal);
            if (layerRoot == null)
            {
                Debug.LogWarning("[AIToUGUI] Windinator metaball lab skipped because UISystem layer root is missing.", this);
                Destroy(gameObject);
                return;
            }

            _defaultFont = LoadBuiltinFont();

            transform.SetParent(layerRoot, false);
            var rootRect = GetComponent<RectTransform>();
            StretchFullScreen(rootRect);
            EnsureCanvasChannels(layerRoot.GetComponentInParent<Canvas>());

            var backdrop = CreateRect("Backdrop", rootRect, Vector2.zero, Vector2.zero);
            StretchFullScreen(backdrop);
            ConfigureWindinatorShape(
                backdrop.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.04f, 0.07f, 0.10f, 1f),
                true,
                new Color(0.10f, 0.18f, 0.24f, 1f),
                AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight,
                0f,
                Vector4.zero,
                0f,
                0f,
                Color.clear,
                0f,
                0f,
                Color.clear,
                false,
                Color.clear,
                0f,
                0f);

            var stage = CreateRect("Stage", rootRect, new Vector2(920f, 700f), Vector2.zero);
            ConfigureWindinatorShape(
                stage.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.07f, 0.11f, 0.16f, 0.94f),
                true,
                new Color(0.12f, 0.20f, 0.27f, 0.94f),
                AIToUGUIGradientDirection.Vertical,
                34f,
                Vector4.one * 34f,
                0f,
                1.2f,
                new Color(0.72f, 0.90f, 1f, 0.16f),
                16f,
                32f,
                new Color(0f, 0f, 0f, 0.42f),
                false,
                Color.clear,
                0f,
                0f);

            CreateText(
                "Title",
                stage,
                new Vector2(760f, 52f),
                new Vector2(0f, 286f),
                34,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white,
                "Windinator Metaball Lab");

            CreateText(
                "Hint",
                stage,
                new Vector2(800f, 32f),
                new Vector2(0f, 244f),
                18,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                new Color(0.88f, 0.94f, 1f, 0.78f),
                "Move inside the lab to stretch the blob. Click once to trigger a pulse.");

            var labFrame = CreateRect("LabFrame", stage, new Vector2(820f, 430f), new Vector2(0f, 6f));
            ConfigureWindinatorShape(
                labFrame.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.02f, 0.05f, 0.08f, 0.96f),
                true,
                new Color(0.08f, 0.13f, 0.18f, 0.96f),
                AIToUGUIGradientDirection.Vertical,
                28f,
                Vector4.one * 28f,
                0f,
                1.4f,
                new Color(0.72f, 0.90f, 1f, 0.18f),
                10f,
                24f,
                new Color(0f, 0f, 0f, 0.36f),
                true,
                new Color(0.34f, 0.71f, 1f, 0.18f),
                20f,
                0.65f);

            var surface = CreateRect("MetaballSurface", labFrame, new Vector2(760f, 360f), Vector2.zero);
            var canvasGraphic = surface.gameObject.AddComponent<CanvasGraphic>();
            canvasGraphic.raycastTarget = true;
            canvasGraphic.maskable = true;
            canvasGraphic.Quality = 1.4f;
            canvasGraphic.color = Color.white;
            canvasGraphic.LeftUpColor = new Color(0.95f, 0.98f, 1f, 0.98f);
            canvasGraphic.RightUpColor = new Color(0.84f, 0.95f, 1f, 0.98f);
            canvasGraphic.RightDownColor = new Color(0.54f, 0.82f, 1f, 0.96f);
            canvasGraphic.LeftDownColor = new Color(0.72f, 0.90f, 1f, 0.98f);
            canvasGraphic.SetOutline(new Color(1f, 1f, 1f, 0.16f), 2f);
            canvasGraphic.SetShadow(new Color(0.05f, 0.18f, 0.32f, 0.52f), 18f, 26f);
            canvasGraphic.SetMargin(48f);

            var lab = surface.gameObject.AddComponent<AIToUGUIWindinatorMetaballSurface>();
            lab.Configure(
                corePosition: new Vector2(-148f, 0f),
                followerRestPosition: new Vector2(148f, 0f),
                coreRadius: 78f,
                followerRadius: 66f,
                blend: 44f,
                breakDistance: 326f);
            AIToUGUIInteractionUtility.EnsureInteractionEnvironment(lab);

            CreateText(
                "Caption",
                stage,
                new Vector2(720f, 28f),
                new Vector2(0f, -262f),
                16,
                FontStyle.Italic,
                TextAnchor.MiddleCenter,
                new Color(0.80f, 0.88f, 0.96f, 0.68f),
                "Near = merge, far = split. This is the raw Windinator sticky response.");
        }

        private static void EnsureCanvasChannels(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.rootCanvas.additionalShaderChannels |=
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.TexCoord3;
        }

        private static void ConfigureWindinatorShape(
            GameObject target,
            AIToUGUIWindinatorShapeKind shapeKind,
            Color fillColor,
            bool useGradient,
            Color gradientColor,
            AIToUGUIGradientDirection gradientDirection,
            float cornerRadius,
            Vector4 cornerRadii,
            float shapeAmount,
            float outlineWidth,
            Color outlineColor,
            float shadowSize,
            float shadowBlur,
            Color shadowColor,
            bool enableGlow,
            Color glowColor,
            float glowBlur,
            float glowIntensity)
        {
            if (target == null)
            {
                return;
            }

            var adapter = target.GetComponent<AIToUGUIWindinatorShapeAdapter>() ??
                          target.AddComponent<AIToUGUIWindinatorShapeAdapter>();

            adapter.Configure(
                shapeKind,
                true,
                fillColor,
                useGradient,
                gradientColor,
                gradientDirection,
                cornerRadius,
                cornerRadii,
                shapeAmount,
                outlineWidth,
                outlineColor,
                shadowSize,
                shadowBlur,
                shadowColor,
                enableGlow,
                glowColor,
                glowBlur,
                glowIntensity);
        }

        private Text CreateText(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 anchoredPosition,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color,
            string value)
        {
            var rect = CreateRect(name, parent, size, anchoredPosition);
            var text = rect.gameObject.AddComponent<Text>();
            text.raycastTarget = false;
            text.font = _defaultFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = value;
            return text;
        }

        private static RectTransform CreateRect(string name, RectTransform parent, Vector2 size, Vector2 anchoredPosition)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void StretchFullScreen(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        private static Font LoadBuiltinFont()
        {
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGraphic))]
    public sealed class AIToUGUIWindinatorMetaballSurface : CanvasDrawer, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
    {
        [SerializeField] private Vector2 _corePosition = new Vector2(-148f, 0f);
        [SerializeField] private Vector2 _followerRestPosition = new Vector2(148f, 0f);
        [SerializeField] private float _coreRadius = 78f;
        [SerializeField] private float _followerRadius = 66f;
        [SerializeField] private float _blend = 44f;
        [SerializeField] private float _smoothTime = 0.075f;
        [SerializeField] private float _maxSpeed = 3200f;
        [SerializeField] private float _breakDistance = 326f;
        [SerializeField] private float _detachedTrailWindow = 42f;
        [SerializeField] private float _padding = 40f;

        private RectTransform _rectTransform;
        private Vector2 _currentFollowerPosition;
        private Vector2 _targetFollowerPosition;
        private Vector2 _moveVelocity;
        private float _pulse;
        private float _pulseVelocity;
        private bool _hovering;

        public void Configure(
            Vector2 corePosition,
            Vector2 followerRestPosition,
            float coreRadius,
            float followerRadius,
            float blend,
            float breakDistance)
        {
            _corePosition = corePosition;
            _followerRestPosition = followerRestPosition;
            _coreRadius = coreRadius;
            _followerRadius = followerRadius;
            _blend = blend;
            _breakDistance = breakDistance;
            ResetFollower();
            SetDirty();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovering = true;
            UpdatePointerTarget(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovering = false;
            _targetFollowerPosition = _followerRestPosition;
            SetDirty();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            _hovering = true;
            UpdatePointerTarget(eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _pulse = 1f;
            _pulseVelocity = 0f;
            UpdatePointerTarget(eventData);
            SetDirty();
        }

        private void Awake()
        {
            CacheReferences();
            ResetFollower();
        }

        private void OnEnable()
        {
            CacheReferences();
            ResetFollower();
            SetDirty();
        }

        private void OnValidate()
        {
            CacheReferences();
            SetDirty();
        }

        protected override void Update()
        {
            var followerBefore = _currentFollowerPosition;
            var pulseBefore = _pulse;

            if (Application.isPlaying)
            {
                var target = _hovering ? _targetFollowerPosition : _followerRestPosition;
                _currentFollowerPosition = Vector2.SmoothDamp(
                    _currentFollowerPosition,
                    target,
                    ref _moveVelocity,
                    _smoothTime,
                    _maxSpeed,
                    Time.unscaledDeltaTime);

                _pulse = Mathf.SmoothDamp(
                    _pulse,
                    0f,
                    ref _pulseVelocity,
                    0.18f,
                    Mathf.Infinity,
                    Time.unscaledDeltaTime);
            }

            base.Update();

            if ((followerBefore - _currentFollowerPosition).sqrMagnitude > 0.0001f ||
                Mathf.Abs(pulseBefore - _pulse) > 0.0001f)
            {
                SetDirty();
            }
        }

        protected override void Draw(CanvasGraphic canvas, Vector2 size)
        {
            var direction = ResolveDirection();
            var normal = new Vector2(-direction.y, direction.x);
            var distance = Vector2.Distance(_corePosition, _currentFollowerPosition);
            var stickyStart = (_coreRadius + _followerRadius) * 0.78f;
            var sticky = 1f - Mathf.Clamp01((distance - stickyStart) / Mathf.Max(1f, _breakDistance - stickyStart));
            sticky = Mathf.SmoothStep(0f, 1f, sticky);

            var motion = _moveVelocity.magnitude;
            var pulseStretch = _pulse * 18f;
            var followerRadius = _followerRadius + Mathf.Clamp(motion * 0.012f + pulseStretch, 0f, 28f);
            var coreRadius = _coreRadius + _pulse * 12f;

            canvas.CircleBrush.Draw(_corePosition, coreRadius, _blend * 1.18f);

            if (sticky > 0.015f)
            {
                DrawBridge(canvas, coreRadius, followerRadius, sticky, direction, normal);
            }
            else
            {
                DrawDetachedDroplets(canvas, distance, direction, normal);
            }

            var followerOffset = direction * (_pulse * 10f);
            canvas.CircleBrush.Draw(_currentFollowerPosition + followerOffset, followerRadius, _blend * 1.08f);
        }

        private void DrawBridge(
            CanvasGraphic canvas,
            float coreRadius,
            float followerRadius,
            float sticky,
            Vector2 direction,
            Vector2 normal)
        {
            var axis = _currentFollowerPosition - _corePosition;
            var distance = axis.magnitude;
            if (distance <= 0.001f)
            {
                return;
            }

            var segmentCount = Mathf.Clamp(
                Mathf.CeilToInt(distance / Mathf.Max(Mathf.Min(coreRadius, followerRadius) * 0.34f, 14f)),
                5,
                16);
            var bridgeBlend = Mathf.Lerp(_blend * 0.65f, _blend * 1.36f, sticky);
            var neckRadius = Mathf.Lerp(8f, Mathf.Min(coreRadius, followerRadius) * 0.54f, sticky);

            for (var i = 1; i < segmentCount; i++)
            {
                var t = i / (float)segmentCount;
                var smoothT = Mathf.SmoothStep(0f, 1f, t);
                var center = Vector2.LerpUnclamped(_corePosition, _currentFollowerPosition, smoothT);
                var arc = Mathf.Sin(smoothT * Mathf.PI) * Mathf.Lerp(0f, 18f, sticky + _pulse * 0.35f);
                center += normal * arc * 0.10f;

                var bodyRadius = Mathf.Lerp(coreRadius, followerRadius, smoothT);
                var profile = 1f - Mathf.Abs(smoothT * 2f - 1f);
                var radius = Mathf.Lerp(neckRadius, bodyRadius, 0.35f + profile * 0.65f);
                radius += Mathf.Sin(smoothT * Mathf.PI) * (_pulse * 6f + sticky * 4f);

                canvas.CircleBrush.Draw(center, Mathf.Max(6f, radius), bridgeBlend);
            }

            if (sticky < 0.30f)
            {
                var dripStrength = 1f - Mathf.Clamp01(sticky / 0.30f);
                var drift = Vector2.Lerp(_corePosition, _currentFollowerPosition, 0.46f);
                var dropA = drift - normal * (12f + 10f * dripStrength) - direction * (4f + 6f * dripStrength);
                var dropB = drift + normal * (10f + 7f * dripStrength) + direction * (2f + 3f * dripStrength);
                canvas.CircleBrush.Draw(dropA, 8f + 8f * dripStrength, 18f + 12f * dripStrength);
                canvas.CircleBrush.Draw(dropB, 6f + 6f * dripStrength, 16f + 8f * dripStrength);
            }
        }

        private void DrawDetachedDroplets(CanvasGraphic canvas, float distance, Vector2 direction, Vector2 normal)
        {
            var detachedDistance = distance - _breakDistance;
            if (detachedDistance <= 0f || detachedDistance >= _detachedTrailWindow)
            {
                return;
            }

            var linger = 1f - Mathf.Clamp01(detachedDistance / Mathf.Max(1f, _detachedTrailWindow));
            linger = Mathf.SmoothStep(0f, 1f, linger);
            if (linger <= 0.02f)
            {
                return;
            }

            var mid = Vector2.Lerp(_corePosition, _currentFollowerPosition, 0.32f);
            var pullOffset = direction * Mathf.Lerp(0f, 10f, 1f - linger);
            var mainRadius = Mathf.Lerp(0f, 14f, linger);
            var secondaryRadius = Mathf.Lerp(0f, 9f, linger);

            canvas.CircleBrush.Draw(mid - pullOffset, mainRadius, Mathf.Lerp(0f, 22f, linger));
            canvas.CircleBrush.Draw(
                mid - direction * Mathf.Lerp(14f, 24f, 1f - linger) + normal * Mathf.Lerp(3f, 8f, linger),
                secondaryRadius,
                Mathf.Lerp(0f, 16f, linger));
        }

        private Vector2 ResolveDirection()
        {
            var direction = _moveVelocity.sqrMagnitude > 0.0001f
                ? _moveVelocity.normalized
                : (_currentFollowerPosition - _corePosition).normalized;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector2.right;
            }

            return direction;
        }

        private void UpdatePointerTarget(PointerEventData eventData)
        {
            if (_rectTransform == null || eventData == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rectTransform,
                    eventData.position,
                    eventData.pressEventCamera ?? eventData.enterEventCamera,
                    out var localPoint))
            {
                return;
            }

            _targetFollowerPosition = ClampTarget(localPoint);
            SetDirty();
        }

        private Vector2 ClampTarget(Vector2 value)
        {
            var rect = _rectTransform != null ? _rectTransform.rect : new Rect(-380f, -180f, 760f, 360f);
            var limitRadius = Mathf.Max(_coreRadius, _followerRadius) + _padding;
            var halfWidth = rect.width * 0.5f - limitRadius;
            var halfHeight = rect.height * 0.5f - limitRadius;

            value.x = Mathf.Clamp(value.x, -halfWidth, halfWidth);
            value.y = Mathf.Clamp(value.y, -halfHeight, halfHeight);
            return value;
        }

        private void CacheReferences()
        {
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }
        }

        private void ResetFollower()
        {
            _currentFollowerPosition = _followerRestPosition;
            _targetFollowerPosition = _followerRestPosition;
            _moveVelocity = Vector2.zero;
            _pulse = 0f;
            _pulseVelocity = 0f;
        }
    }
}
