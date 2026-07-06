using UnityEngine;
using UnityEngine.UI;
using TrueShadowComponent = LeTai.TrueShadow.TrueShadow;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AIToUGUIWindinatorShapeAdapter : MonoBehaviour
    {
        // Windinator 这条链路只负责“非标准矩形拓扑”或需要更自由轮廓控制的节点。
        // 常规矩形/圆角矩形仍由 AIToUGUIShapeAdapter(DTT) 承载，避免两套后端抢同一个节点。
        private const string InternalFillLayerName = "__ai_WindinatorFill";

        [SerializeField] private AIToUGUIWindinatorShapeKind _shapeKind = AIToUGUIWindinatorShapeKind.PerCornerRoundedRect;
        [SerializeField] private bool _enableFill = true;
        [SerializeField] private Color _fillColor = new Color(0.13f, 0.16f, 0.2f, 0.96f);
        [SerializeField] private bool _useGradient;
        [SerializeField] private Color _gradientColor = new Color(0.08f, 0.1f, 0.14f, 0.96f);
        [SerializeField] private AIToUGUIGradientDirection _gradientDirection = AIToUGUIGradientDirection.Vertical;
        [SerializeField] private float _cornerRadius = 18f;
        [SerializeField] private Vector4 _cornerRadii = new Vector4(18f, 18f, 18f, 18f);
        [SerializeField] private float _shapeAmount = 26f;
        [SerializeField] private float _outlineWidth = 1f;
        [SerializeField] private Color _outlineColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private float _shadowSize = 12f;
        [SerializeField] private float _shadowBlur = 28f;
        [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.28f);
        [SerializeField] private bool _enableGlow;
        [SerializeField] private Color _glowColor = new Color(0.45f, 0.76f, 1f, 0.35f);
        [SerializeField] private float _glowBlur = 24f;
        [SerializeField] private float _glowIntensity = 1f;

        private static readonly AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;

        private RectangleGraphic _rectangleGraphic;
        private PolygonGraphic _polygonGraphic;
        [SerializeField] private RectTransform _fillLayerTransform;
        [SerializeField] private RectangleGraphic _fillRectangleGraphic;
        [SerializeField] private PolygonGraphic _fillPolygonGraphic;
        private AIToUGUIShadowEffect _shadowEffect;
        private RectTransform _rectTransform;
        private bool _applyPending;
        private bool _isApplying;
        [SerializeField] private bool _hasConfiguredState;
#if UNITY_EDITOR
        private bool _editorApplyQueued;
#endif

        public void Configure(
            AIToUGUIWindinatorShapeKind shapeKind,
            bool enableFill,
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
            _shapeKind = shapeKind;
            _enableFill = enableFill;
            _fillColor = fillColor;
            _useGradient = useGradient;
            _gradientColor = gradientColor;
            _gradientDirection = gradientDirection;
            _cornerRadius = cornerRadius;
            _cornerRadii = cornerRadii;
            _shapeAmount = shapeAmount;
            _outlineWidth = outlineWidth;
            _outlineColor = outlineColor;
            _shadowSize = shadowSize;
            _shadowBlur = shadowBlur;
            _shadowColor = shadowColor;
            _enableGlow = enableGlow;
            _glowColor = glowColor;
            _glowBlur = glowBlur;
            _glowIntensity = glowIntensity;
            _hasConfiguredState = true;
            ApplyNow();
        }

        private void Awake()
        {
            if (_hasConfiguredState)
            {
                ApplyNow();
            }
        }

        private void OnEnable()
        {
            if (_hasConfiguredState)
            {
                ApplyNow();
            }
        }

        private void LateUpdate()
        {
            if (_applyPending)
            {
                _applyPending = false;
                ApplyNow();
            }
        }

        private void OnValidate()
        {
            if (_hasConfiguredState)
            {
                QueueApply();
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_hasConfiguredState)
            {
                QueueApply();
            }
        }

        private void OnTransformParentChanged()
        {
            EnsureCanvasChannels();
        }

        public void ApplyNow()
        {
            if (!_hasConfiguredState || _isApplying)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                QueueApply();
                return;
            }
#endif

            _isApplying = true;
            try
            {
                if (GetComponent<CanvasRenderer>() == null)
                {
                    gameObject.AddComponent<CanvasRenderer>();
                }

                EnsureCanvasChannels();
                var graphic = EnsureGraphic();
                if (graphic == null)
                {
                    return;
                }

                ConfigureGraphic(graphic);
                ConfigureShadowEffect();
            }
            finally
            {
                _isApplying = false;
            }
        }

        private void QueueApply()
        {
            if (!_hasConfiguredState)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _applyPending = true;
                if (_editorApplyQueued)
                {
                    return;
                }

                _editorApplyQueued = true;
                EditorApplication.delayCall += ApplyQueuedInEditor;
                return;
            }
#endif

            ApplyNow();
        }

#if UNITY_EDITOR
        private void ApplyQueuedInEditor()
        {
            _editorApplyQueued = false;
            if (this == null || gameObject == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                AIToUGUIEditorUiMutationGuard.QueueEditorCallback(ref _editorApplyQueued, ApplyQueuedInEditor);
                return;
            }
#endif

            _applyPending = false;
            ApplyNow();
        }
#endif

        private void EnsureCanvasChannels()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.rootCanvas.additionalShaderChannels |= RequiredChannels;
            }
        }

        private SignedDistanceFieldGraphic EnsureGraphic()
        {
            CleanupLegacyComponents();

            if (!EnsureCompatibleGraphicTarget())
            {
                return null;
            }

            if (UsesPolygonShape(_shapeKind))
            {
                RemoveConflictingGraphic(ref _rectangleGraphic);
                _polygonGraphic = GetComponent<PolygonGraphic>() ?? gameObject.AddComponent<PolygonGraphic>();
                return _polygonGraphic;
            }

            RemoveConflictingGraphic(ref _polygonGraphic);
            _rectangleGraphic = GetComponent<RectangleGraphic>() ?? gameObject.AddComponent<RectangleGraphic>();
            return _rectangleGraphic;
        }

        private void CleanupLegacyComponents()
        {
            // 节点一旦切到 Windinator，就必须清干净旧的 DTT/过渡组件。
            // 否则最常见的问题就是重复描边、黑阴影叠加、Prefab 上残留 missing script。
            if (GetComponent<DTT.UI.ProceduralUI.RoundedImage>() != null)
            {
                RemoveShadowStack();
            }

            RemoveComponent(GetComponent<AIToUGUIShapeAdapter>());
            RemoveComponent(GetComponent<DTT.UI.ProceduralUI.RoundedImage>());
            RemoveComponent(GetComponent<DTT.UI.ProceduralUI.Border>());
            RemoveComponent(GetComponent<DTT.UI.ProceduralUI.GradientEffect>());
        }

        private bool EnsureCompatibleGraphicTarget()
        {
            var graphics = GetComponents<Graphic>();
            if (graphics == null || graphics.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null ||
                    graphic is RectangleGraphic ||
                    graphic is PolygonGraphic)
                {
                    continue;
                }

                RemoveComponent(graphic);
            }

            graphics = GetComponents<Graphic>();
            if (graphics == null)
            {
                return true;
            }

            var compatibleCount = 0;
            for (var i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] is RectangleGraphic || graphics[i] is PolygonGraphic)
                {
                    compatibleCount++;
                }
            }

            return compatibleCount <= 1;
        }

        private void ConfigureGraphic(SignedDistanceFieldGraphic graphic)
        {
            PrepareGraphic(graphic, AIToUGUIInteractionUtility.ShouldGraphicReceiveRaycasts(this));
            if (graphic != null)
            {
                graphic.GraphicBlur = ResolveGraphicBlur();
            }

            var useLayeredBorder = ShouldUseLayeredBorder();
            if (useLayeredBorder)
            {
                // Windinator 自带 outline 更像“沿轮廓描边”，但很多 UI 目标实际是 CSS 的 border-box 关系。
                // 这里改成外层画边框、内层单独放一层 fill，才能更接近 HTML 里的边框厚度和填充关系。
                var fillGraphic = EnsureFillLayerGraphic();
                useLayeredBorder = fillGraphic != null;
                if (useLayeredBorder)
                {
                    graphic.SetOutline(Color.clear, 0f);
                    ConfigureShapeGeometry(graphic);
                    if (!ShouldUseBuiltinShadow())
                    {
                        ConfigureProceduralGlow(graphic);
                    }
                    ApplySolidFill(graphic, _outlineColor);

                    PrepareGraphic(fillGraphic, false);
                    fillGraphic.GraphicBlur = ResolveGraphicBlur();
                    fillGraphic.SetOutline(Color.clear, 0f);
                    fillGraphic.SetShadow(Color.clear, 0f, 0f);
                    ApplyGradient(fillGraphic, _enableFill, _fillColor, _useGradient, _gradientColor, _gradientDirection);

                    if (fillGraphic is RectangleGraphic fillRectangleGraphic)
                    {
                        ConfigureRectangleGraphic(
                            fillRectangleGraphic,
                            InsetCornerRadii(ResolveRectangleCornerRadii(_cornerRadii, _cornerRadius), _outlineWidth));
                    }
                    else if (fillGraphic is PolygonGraphic fillPolygonGraphic)
                    {
                        ConfigurePolygonGraphic(
                            fillPolygonGraphic,
                            ResolveInsetRectSize(_outlineWidth),
                            Mathf.Max(0f, _cornerRadius - Mathf.Max(0f, _outlineWidth)),
                            _shapeAmount);
                    }

                    fillGraphic.SetVerticesDirty();
                    fillGraphic.SetMaterialDirty();
                    fillGraphic.SetLayoutDirty();
                }
            }

            if (!useLayeredBorder)
            {
                RemoveGeneratedFillLayer();
                graphic.SetOutline(_outlineColor, Mathf.Max(0f, _outlineWidth));
                if (!ShouldUseBuiltinShadow())
                {
                    ConfigureProceduralGlow(graphic);
                }
                ApplyGradient(graphic, _enableFill, _fillColor, _useGradient, _gradientColor, _gradientDirection);
                ConfigureShapeGeometry(graphic);
            }

            if (ShouldUseBuiltinShadow())
            {
                ApplyBuiltinShadow(graphic);
            }

            graphic.SetVerticesDirty();
            graphic.SetMaterialDirty();
            graphic.SetLayoutDirty();
        }

        private void ConfigureShapeGeometry(SignedDistanceFieldGraphic graphic)
        {
            if (_shapeKind == AIToUGUIWindinatorShapeKind.PerCornerRoundedRect && _rectangleGraphic != null)
            {
                ConfigureRectangleGraphic(_rectangleGraphic, ResolveRectangleCornerRadii(_cornerRadii, _cornerRadius));
            }
            else if (_polygonGraphic != null)
            {
                ConfigurePolygonGraphic(_polygonGraphic, ResolveRectSize(), _cornerRadius, _shapeAmount);
            }
        }

        private static void PrepareGraphic(SignedDistanceFieldGraphic graphic, bool raycastTarget)
        {
            if (graphic == null)
            {
                return;
            }

            graphic.raycastTarget = raycastTarget;
            graphic.maskable = true;
            graphic.Texture = null;
            graphic.Alpha = 1f;
            graphic.color = Color.white;
        }

        private void ConfigureRectangleGraphic(RectangleGraphic graphic, Vector4 radii)
        {
            var uniform = ApproximatelyEqual(radii.x, radii.y) &&
                          ApproximatelyEqual(radii.x, radii.z) &&
                          ApproximatelyEqual(radii.x, radii.w);

            graphic.SetMaxRoundness(false);
            graphic.SetUniformRoundness(uniform);
            if (uniform)
            {
                graphic.SetRoundness(new Vector4(radii.x, 0f, 0f, 0f));
            }
            else
            {
                graphic.SetRoundness(ReorderCssCornerRadiiForWindinator(radii) * 2f);
            }
        }

        private static Vector4 ReorderCssCornerRadiiForWindinator(Vector4 cssRadii)
        {
            return new Vector4(cssRadii.y, cssRadii.z, cssRadii.x, cssRadii.w);
        }

        private void ConfigurePolygonGraphic(PolygonGraphic graphic, Vector2 size, float cornerRadius, float shapeAmount)
        {
            var halfWidth = Mathf.Max(1f, size.x * 0.5f);
            var halfHeight = Mathf.Max(1f, size.y * 0.5f);
            var amount = Mathf.Clamp(shapeAmount > 0f ? shapeAmount : cornerRadius, 0f, Mathf.Min(halfWidth, halfHeight) - 1f);
            if (amount <= 0f)
            {
                amount = Mathf.Min(halfWidth, halfHeight) * 0.16f;
            }

            var points = new StaticArray<Vector4>(12);
            switch (_shapeKind)
            {
                case AIToUGUIWindinatorShapeKind.CutCorner:
                    AddPoint(points, new Vector2(-halfWidth + amount, halfHeight));
                    AddPoint(points, new Vector2(halfWidth - amount, halfHeight));
                    AddPoint(points, new Vector2(halfWidth, halfHeight - amount));
                    AddPoint(points, new Vector2(halfWidth, -halfHeight + amount));
                    AddPoint(points, new Vector2(halfWidth - amount, -halfHeight));
                    AddPoint(points, new Vector2(-halfWidth + amount, -halfHeight));
                    AddPoint(points, new Vector2(-halfWidth, -halfHeight + amount));
                    AddPoint(points, new Vector2(-halfWidth, halfHeight - amount));
                    break;

                case AIToUGUIWindinatorShapeKind.Plate:
                    AddPlatePoints(points, halfWidth, halfHeight, amount, 0.32f);
                    break;

                case AIToUGUIWindinatorShapeKind.Banner:
                    AddPlatePoints(points, halfWidth, halfHeight, amount, 0.18f);
                    break;

                default:
                    AddPoint(points, new Vector2(-halfWidth, halfHeight));
                    AddPoint(points, new Vector2(halfWidth, halfHeight));
                    AddPoint(points, new Vector2(halfWidth, -halfHeight));
                    AddPoint(points, new Vector2(-halfWidth, -halfHeight));
                    break;
            }

            graphic.Points = points;
            graphic.Roundness = Mathf.Max(0f, cornerRadius);
        }

        private static void AddPlatePoints(StaticArray<Vector4> points, float halfWidth, float halfHeight, float amount, float shoulderRatio)
        {
            var shoulder = Mathf.Clamp(halfHeight * shoulderRatio, 10f, halfHeight - 2f);
            AddPoint(points, new Vector2(-halfWidth + amount, halfHeight));
            AddPoint(points, new Vector2(halfWidth - amount, halfHeight));
            AddPoint(points, new Vector2(halfWidth, halfHeight - shoulder));
            AddPoint(points, new Vector2(halfWidth, -halfHeight + shoulder));
            AddPoint(points, new Vector2(halfWidth - amount, -halfHeight));
            AddPoint(points, new Vector2(-halfWidth + amount, -halfHeight));
            AddPoint(points, new Vector2(-halfWidth, -halfHeight + shoulder));
            AddPoint(points, new Vector2(-halfWidth, halfHeight - shoulder));
        }

        private static void AddPoint(StaticArray<Vector4> points, Vector2 point)
        {
            points.Add(new Vector4(point.x, point.y, 0f, 0f));
        }

        private static void ApplySolidFill(SignedDistanceFieldGraphic graphic, Color color)
        {
            if (graphic == null)
            {
                return;
            }

            graphic.LeftUpColor = color;
            graphic.RightUpColor = color;
            graphic.RightDownColor = color;
            graphic.LeftDownColor = color;
        }

        private void ApplyGradient(
            SignedDistanceFieldGraphic graphic,
            bool enableFill,
            Color fillColor,
            bool useGradient,
            Color gradientColor,
            AIToUGUIGradientDirection gradientDirection)
        {
            if (!enableFill)
            {
                var transparent = new Color(fillColor.r, fillColor.g, fillColor.b, 0f);
                graphic.LeftUpColor = transparent;
                graphic.RightUpColor = transparent;
                graphic.RightDownColor = transparent;
                graphic.LeftDownColor = transparent;
                return;
            }

            var primary = fillColor;
            var secondary = useGradient ? gradientColor : fillColor;
            switch (gradientDirection)
            {
                case AIToUGUIGradientDirection.Horizontal:
                    graphic.LeftUpColor = primary;
                    graphic.LeftDownColor = primary;
                    graphic.RightUpColor = secondary;
                    graphic.RightDownColor = secondary;
                    break;
                case AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight:
                    graphic.LeftUpColor = primary;
                    graphic.RightDownColor = secondary;
                    graphic.RightUpColor = Color.Lerp(primary, secondary, 0.5f);
                    graphic.LeftDownColor = Color.Lerp(primary, secondary, 0.5f);
                    break;
                case AIToUGUIGradientDirection.DiagonalBottomLeftToTopRight:
                    graphic.LeftDownColor = primary;
                    graphic.RightUpColor = secondary;
                    graphic.LeftUpColor = Color.Lerp(primary, secondary, 0.5f);
                    graphic.RightDownColor = Color.Lerp(primary, secondary, 0.5f);
                    break;
                default:
                    graphic.LeftUpColor = primary;
                    graphic.RightUpColor = primary;
                    graphic.RightDownColor = secondary;
                    graphic.LeftDownColor = secondary;
                    break;
            }
        }

        private void ConfigureShadowEffect()
        {
            if (ShouldUseBuiltinShadow())
            {
                RemoveShadowStack();
                return;
            }

            _shadowEffect = GetComponent<AIToUGUIShadowEffect>() ?? gameObject.AddComponent<AIToUGUIShadowEffect>();
            var useTrueShadowGlow = !ShouldUseProceduralGlow();
            var shadowSize = _shadowSize;
            var shadowBlur = _shadowBlur;
            var shadowColor = _shadowColor;
            if (ShouldToneDownDropShadow())
            {
                var diagonal = IsDiagonalRotation();
                shadowSize *= diagonal ? 0.25f : 0.4f;
                shadowBlur *= diagonal ? 0.55f : 0.8f;
                shadowColor.a *= diagonal ? 0.18f : 0.3f;
            }

            _shadowEffect.Configure(
                shadowSize,
                shadowBlur,
                shadowColor,
                useTrueShadowGlow && _enableGlow,
                _glowColor,
                _glowBlur,
                _glowIntensity);
        }

        private void ApplyBuiltinShadow(SignedDistanceFieldGraphic graphic)
        {
            if (graphic == null)
            {
                return;
            }

            var shadowSize = _shadowSize;
            var shadowBlur = _shadowBlur;
            var shadowColor = _shadowColor;
            if (ShouldToneDownDropShadow())
            {
                var diagonal = IsDiagonalRotation();
                shadowSize *= diagonal ? 0.25f : 0.4f;
                shadowBlur *= diagonal ? 0.55f : 0.8f;
                shadowColor.a *= diagonal ? 0.18f : 0.3f;
            }

            graphic.SetShadow(shadowColor, shadowSize, shadowBlur);
        }

        private void ConfigureProceduralGlow(SignedDistanceFieldGraphic graphic)
        {
            if (graphic == null || !ShouldUseProceduralGlow())
            {
                graphic?.SetShadow(Color.clear, 0f, 0f);
                return;
            }

            var glowColor = _glowColor;
            glowColor.a *= Mathf.Clamp01(_glowIntensity);

            var glowSize = Mathf.Max(_outlineWidth + 1f, _glowBlur * 0.35f);
            var glowBlur = Mathf.Max(0f, _glowBlur * 0.75f);
            graphic.SetShadow(glowColor, glowSize, glowBlur);
        }

        private bool ShouldUseProceduralGlow()
        {
            return UsesPolygonShape(_shapeKind) &&
                   _enableGlow &&
                   _glowColor.a > 0.001f &&
                   _glowBlur > 0.001f;
        }

        private bool ShouldToneDownDropShadow()
        {
            return (ShouldUseLayeredBorder() &&
                    ShouldUseProceduralGlow() &&
                    _shadowColor.a > 0.001f &&
                    IsNearBlack(_shadowColor)) ||
                   IsSlenderShape() ||
                   IsDiagonalRotation();
        }

        private bool ShouldUseBuiltinShadow()
        {
            return ShouldToneDownDropShadow();
        }

        private bool ShouldUseLayeredBorder()
        {
            if (!_enableFill || _outlineWidth <= 0.001f || _outlineColor.a <= 0.001f)
            {
                return false;
            }

            var size = ResolveRectSize();
            return size.x > (_outlineWidth * 2f + 0.5f) &&
                   size.y > (_outlineWidth * 2f + 0.5f);
        }

        private SignedDistanceFieldGraphic EnsureFillLayerGraphic()
        {
            var fillLayer = EnsureFillLayerTransform();
            if (fillLayer == null)
            {
                return null;
            }

            if (UsesPolygonShape(_shapeKind))
            {
                if (_fillRectangleGraphic == null)
                {
                    _fillRectangleGraphic = fillLayer.GetComponent<RectangleGraphic>();
                }

                if (_fillRectangleGraphic != null)
                {
                    RemoveComponent(ref _fillRectangleGraphic);
                }

                _fillPolygonGraphic = fillLayer.GetComponent<PolygonGraphic>() ?? fillLayer.gameObject.AddComponent<PolygonGraphic>();
                return _fillPolygonGraphic;
            }

            if (_fillPolygonGraphic == null)
            {
                _fillPolygonGraphic = fillLayer.GetComponent<PolygonGraphic>();
            }

            if (_fillPolygonGraphic != null)
            {
                RemoveComponent(ref _fillPolygonGraphic);
            }

            _fillRectangleGraphic = fillLayer.GetComponent<RectangleGraphic>() ?? fillLayer.gameObject.AddComponent<RectangleGraphic>();
            return _fillRectangleGraphic;
        }

        private RectTransform EnsureFillLayerTransform()
        {
            if (_fillLayerTransform != null && _fillLayerTransform.parent != transform)
            {
                _fillLayerTransform = null;
            }

            if (_fillLayerTransform == null)
            {
                var existing = transform.Find(InternalFillLayerName);
                if (existing != null)
                {
                    _fillLayerTransform = existing as RectTransform;
                }
            }

#if UNITY_EDITOR
            if (_fillLayerTransform == null && !Application.isPlaying && IsPrefabAssetContext())
            {
                // Prefab Asset 上不能动态改 parent，这里直接跳过，避免编辑器报 prefab 污染错误。
                return null;
            }
#endif

            if (_fillLayerTransform == null)
            {
                var fillLayer = new GameObject(InternalFillLayerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
                _fillLayerTransform = fillLayer.GetComponent<RectTransform>();
                _fillLayerTransform.SetParent(transform, false);
            }

            var layoutElement = _fillLayerTransform.GetComponent<LayoutElement>() ?? _fillLayerTransform.gameObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            if (_fillLayerTransform.GetComponent<CanvasRenderer>() == null)
            {
                _fillLayerTransform.gameObject.AddComponent<CanvasRenderer>();
            }

            _fillLayerTransform.anchorMin = Vector2.zero;
            _fillLayerTransform.anchorMax = Vector2.one;
            _fillLayerTransform.pivot = new Vector2(0.5f, 0.5f);
            _fillLayerTransform.localScale = Vector3.one;
            _fillLayerTransform.localRotation = Quaternion.identity;

            var inset = Mathf.Max(0f, _outlineWidth);
            _fillLayerTransform.offsetMin = new Vector2(inset, inset);
            _fillLayerTransform.offsetMax = new Vector2(-inset, -inset);
            _fillLayerTransform.SetSiblingIndex(0);
            return _fillLayerTransform;
        }

        private void RemoveGeneratedFillLayer()
        {
            if (_fillLayerTransform == null)
            {
                var existing = transform.Find(InternalFillLayerName);
                if (existing != null)
                {
                    _fillLayerTransform = existing as RectTransform;
                }
            }

            _fillRectangleGraphic = null;
            _fillPolygonGraphic = null;

            if (_fillLayerTransform == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_fillLayerTransform.gameObject);
            }
            else
            {
                AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(_fillLayerTransform.gameObject);
            }

            _fillLayerTransform = null;
        }

        private Vector2 ResolveInsetRectSize(float inset)
        {
            var size = ResolveRectSize();
            var shrink = Mathf.Max(0f, inset) * 2f;
            return new Vector2(
                Mathf.Max(1f, size.x - shrink),
                Mathf.Max(1f, size.y - shrink));
        }

        private Vector2 ResolveRectSize()
        {
            var rectTransform = CachedRectTransform;
            if (rectTransform == null)
            {
                return Vector2.one * 100f;
            }

            var size = rectTransform.rect.size;
            if (size.sqrMagnitude > 0.0001f)
            {
                return size;
            }

            size = rectTransform.sizeDelta;
            return size.sqrMagnitude > 0.0001f ? size : Vector2.one * 100f;
        }

        private RectTransform CachedRectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = GetComponent<RectTransform>();
                }

                return _rectTransform;
            }
        }

        private static Vector4 SanitizeCornerRadii(Vector4 value)
        {
            return new Vector4(
                Mathf.Max(0f, value.x),
                Mathf.Max(0f, value.y),
                Mathf.Max(0f, value.z),
                Mathf.Max(0f, value.w));
        }

        private static Vector4 ResolveRectangleCornerRadii(Vector4 cornerRadii, float cornerRadius)
        {
            var radii = SanitizeCornerRadii(cornerRadii);
            if (ApproximatelyEqual(radii.x, 0f) &&
                ApproximatelyEqual(radii.y, 0f) &&
                ApproximatelyEqual(radii.z, 0f) &&
                ApproximatelyEqual(radii.w, 0f))
            {
                var uniformRadius = Mathf.Max(0f, cornerRadius);
                radii = new Vector4(uniformRadius, uniformRadius, uniformRadius, uniformRadius);
            }

            return radii;
        }

        private static Vector4 InsetCornerRadii(Vector4 radii, float inset)
        {
            var clampedInset = Mathf.Max(0f, inset);
            return new Vector4(
                Mathf.Max(0f, radii.x - clampedInset),
                Mathf.Max(0f, radii.y - clampedInset),
                Mathf.Max(0f, radii.z - clampedInset),
                Mathf.Max(0f, radii.w - clampedInset));
        }

        private static bool UsesPolygonShape(AIToUGUIWindinatorShapeKind kind)
        {
            return kind == AIToUGUIWindinatorShapeKind.CutCorner ||
                   kind == AIToUGUIWindinatorShapeKind.Banner ||
                   kind == AIToUGUIWindinatorShapeKind.Plate;
        }

        private static bool ApproximatelyEqual(float a, float b)
        {
            return Mathf.Abs(a - b) <= 0.01f;
        }

        private static bool IsNearBlack(Color color)
        {
            return color.r <= 0.08f &&
                   color.g <= 0.08f &&
                   color.b <= 0.08f;
        }

        private bool IsSlenderShape()
        {
            var size = ResolveRectSize();
            var shortSide = Mathf.Max(0.01f, Mathf.Min(size.x, size.y));
            var longSide = Mathf.Max(size.x, size.y);
            return shortSide <= 12f || longSide / shortSide >= 5f;
        }

        private float ResolveGraphicBlur()
        {
            var size = ResolveRectSize();
            var shortSide = Mathf.Max(1f, Mathf.Min(size.x, size.y));
            var longSide = Mathf.Max(size.x, size.y);
            var aspectRatio = longSide / shortSide;
            var blur = 0f;

            if (shortSide <= 18f || aspectRatio >= 4f)
            {
                blur = -0.35f;
            }
            else if (shortSide <= 28f || aspectRatio >= 2.5f)
            {
                blur = -0.2f;
            }

            if (IsDiagonalRotation())
            {
                blur -= 0.05f;
            }

            return Mathf.Clamp(blur, -0.6f, 0.15f);
        }

        private bool IsDiagonalRotation()
        {
            var rectTransform = CachedRectTransform;
            if (rectTransform == null)
            {
                return false;
            }

            var z = Mathf.Abs(Mathf.DeltaAngle(0f, rectTransform.localEulerAngles.z));
            var remainder = Mathf.Repeat(z, 90f);
            return z > 0.1f && remainder > 1f && remainder < 89f;
        }

#if UNITY_EDITOR
        private bool IsPrefabAssetContext()
        {
            return PrefabUtility.IsPartOfPrefabAsset(gameObject);
        }
#endif

        private void RemoveConflictingGraphic<T>(ref T component) where T : Graphic
        {
            if (component == null)
            {
                component = GetComponent<T>();
            }

            if (component == null)
            {
                return;
            }

            RemoveShadowStack();
            RemoveComponent(ref component);
        }

        private void RemoveShadowStack()
        {
            RemoveComponent(ref _shadowEffect);
            RemoveComponent(GetComponent<AIToUGUITrueShadowHashProvider>());

            var trueShadows = GetComponents<TrueShadowComponent>();
            for (var i = 0; i < trueShadows.Length; i++)
            {
                RemoveComponent(trueShadows[i]);
            }
        }

        private void RemoveComponent<T>(ref T component) where T : Component
        {
            if (component == null)
            {
                component = GetComponent<T>();
            }

            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
            }

            component = null;
        }

        private void RemoveComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
            }
        }

        private void OnDestroy()
        {
            RemoveGeneratedFillLayer();

#if UNITY_EDITOR
            if (_editorApplyQueued)
            {
                EditorApplication.delayCall -= ApplyQueuedInEditor;
                _editorApplyQueued = false;
            }
#endif
        }
    }
}
