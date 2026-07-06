using System;
using System.Reflection;
using DTT.UI.ProceduralUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TrueShadowComponent = LeTai.TrueShadow.TrueShadow;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIToUGUI
{
    public static class AIToUGUIInteractionUtility
    {
        public static bool ShouldGraphicReceiveRaycasts(Component target)
        {
            if (target == null)
            {
                return false;
            }

            var binder = target.GetComponent<AIToUGUIAnimationBinder>();
            return target != null &&
                   (target.GetComponent<Selectable>() != null ||
                    target.GetComponent<BaseElement>() != null ||
                    target.GetComponent<AIToUGUISelectableCard>() != null ||
                    (binder != null && binder.RequiresRaycastTarget));
        }

        public static void EnsureInteractionEnvironment(Component target)
        {
            if (!Application.isPlaying || target == null)
            {
                return;
            }

            EnsureEventSystem();
            EnsureGraphicRaycaster(target);
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                var existingEventSystems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
                for (var i = 0; i < existingEventSystems.Length; i++)
                {
                    var candidate = existingEventSystems[i];
                    if (candidate != null && candidate.isActiveAndEnabled)
                    {
                        eventSystem = candidate;
                        break;
                    }
                }
            }

            if (eventSystem == null)
            {
                var go = new GameObject("EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        private static void EnsureGraphicRaycaster(Component target)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var raycasterOwner = canvas.rootCanvas != null ? canvas.rootCanvas.gameObject : canvas.gameObject;
            if (raycasterOwner.GetComponent<GraphicRaycaster>() == null)
            {
                raycasterOwner.AddComponent<GraphicRaycaster>();
            }
        }
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class AIToUGUIProceduralShapeAdapter : MonoBehaviour
    {
        [SerializeField] protected bool _enableFill = true;
        [SerializeField] protected Color _fillColor = new Color(0.13f, 0.16f, 0.2f, 0.96f);
        [SerializeField] protected bool _useGradient;
        [SerializeField] protected Color _gradientColor = new Color(0.08f, 0.1f, 0.14f, 0.96f);
        [SerializeField] protected AIToUGUIGradientDirection _gradientDirection = AIToUGUIGradientDirection.Vertical;
        [SerializeField] protected float _cornerRadius = 18f;
        [SerializeField] protected bool _useMaxRoundness;
        [SerializeField] protected float _outlineWidth = 1f;
        [SerializeField] protected Color _outlineColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] protected float _shadowSize = 12f;
        [SerializeField] protected float _shadowBlur = 28f;
        [SerializeField] protected Color _shadowColor = new Color(0f, 0f, 0f, 0.28f);
        [SerializeField] protected bool _enableGlow;
        [SerializeField] protected Color _glowColor = new Color(0.45f, 0.76f, 1f, 0.35f);
        [SerializeField] protected float _glowBlur = 24f;
        [SerializeField] protected float _glowIntensity = 1f;

        private static readonly AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;
        private static readonly FieldInfo GradientEffectGradientField = typeof(GradientEffect).GetField("_gradient", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GradientEffectTypeField = typeof(GradientEffect).GetField("_type", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GradientEffectOffsetField = typeof(GradientEffect).GetField("_offset", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GradientEffectScaleField = typeof(GradientEffect).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GradientEffectRotationField = typeof(GradientEffect).GetField("_rotation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GradientEffectBatchingField = typeof(GradientEffect).GetField("_batching", BindingFlags.Instance | BindingFlags.NonPublic);

        private RoundedImage _graphic;
        private Border _border;
        private GradientEffect _gradientEffect;
        private AIToUGUIShadowEffect _shadowEffect;
        private RectTransform _rectTransform;
        private Vector2 _lastAppliedRectSize = new Vector2(-1f, -1f);
        private bool _applyPending;
        private bool _isApplying;
        private bool _graphicCompatibilityWarningIssued;
        [SerializeField] private bool _hasConfiguredState;
#if UNITY_EDITOR
        private bool _editorApplyQueued;
#endif

        public void ApplyPreset(AIToUGUIVisualPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            Configure(
                preset.enableFill,
                preset.fillColor,
                preset.useGradient,
                preset.gradientColor,
                preset.gradientDirection,
                preset.cornerRadius,
                preset.useMaxRoundness,
                preset.outlineWidth,
                preset.outlineColor,
                preset.shadowSize,
                preset.shadowBlur,
                preset.shadowColor,
                preset.enableGlow,
                preset.glowColor,
                preset.glowBlur,
                preset.glowIntensity);
        }

        public void Configure(
            bool enableFill,
            Color fillColor,
            bool useGradient,
            Color gradientColor,
            AIToUGUIGradientDirection gradientDirection,
            float cornerRadius,
            bool useMaxRoundness,
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
            _enableFill = enableFill;
            _fillColor = fillColor;
            _useGradient = useGradient;
            _gradientColor = gradientColor;
            _gradientDirection = gradientDirection;
            _cornerRadius = cornerRadius;
            _useMaxRoundness = useMaxRoundness;
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

        protected virtual void Awake()
        {
            if (!_hasConfiguredState)
            {
                return;
            }

            ApplyNow();
        }

        protected virtual void OnEnable()
        {
            if (!_hasConfiguredState)
            {
                return;
            }

            ApplyNow();
        }

        protected virtual void LateUpdate()
        {
            if (_applyPending)
            {
                _applyPending = false;
                ApplyNow();
            }
        }

        protected virtual void OnValidate()
        {
            if (!_hasConfiguredState)
            {
                return;
            }

            QueueApply();
        }

        protected virtual void OnTransformParentChanged()
        {
            EnsureCanvasChannels();
        }

        protected virtual void OnRectTransformDimensionsChange()
        {
            if (!_hasConfiguredState)
            {
                return;
            }

            if (!TryGetRectSize(out var size))
            {
                return;
            }

            if (ApproximatelyEqual(_lastAppliedRectSize, size))
            {
                return;
            }

            ApplyNow();
        }

        public void ApplyNow()
        {
            if (!_hasConfiguredState)
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

            if (_isApplying)
            {
                return;
            }

            if (!HasValidRectTransform())
            {
                return;
            }

            _isApplying = true;
            try
            {
                if (GetComponent<CanvasRenderer>() == null)
                {
                    gameObject.AddComponent<CanvasRenderer>();
                }

                EnsureCanvasChannels();
                if (!EnsureGraphic())
                {
                    return;
                }

                ConfigureGraphic();
                ConfigureBorder();
                ConfigureGradient();
                ConfigureShadowEffect();
                if (_graphic != null)
                {
                    _graphic.SetVerticesDirty();
                    _graphic.SetMaterialDirty();
                }

                if (TryGetRectSize(out var size))
                {
                    _lastAppliedRectSize = size;
                }
            }
            catch (MissingReferenceException)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    _applyPending = false;
                    _graphic = null;
                    _border = null;
                    _gradientEffect = null;
                    _shadowEffect = null;
                    _rectTransform = null;
                    return;
                }
#endif
                throw;
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
            if (this == null || gameObject == null || !enabled)
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
            if (canvas == null)
            {
                return;
            }

            canvas.rootCanvas.additionalShaderChannels |= RequiredChannels;
        }

        private bool EnsureGraphic()
        {
            _graphic = GetComponent<RoundedImage>();
            if (_graphic == null)
            {
                _graphic = UpgradeLegacyImageToRoundedImage();
            }

            if (_graphic == null)
            {
                _graphic = TryCreateRoundedImage();
            }

            if (_graphic == null)
            {
                WarnAboutUnsupportedGraphicTarget();
                return false;
            }

            _graphicCompatibilityWarningIssued = false;
            _graphic.raycastTarget = AIToUGUIInteractionUtility.ShouldGraphicReceiveRaycasts(this);
            _graphic.maskable = true;
            _graphic.type = Image.Type.Simple;
            _graphic.Mode = RoundingMode.FILL;
            _graphic.DistanceFalloff = 1f;
            _graphic.RoundingUnit = _useMaxRoundness ? RoundingUnit.PERCENTAGE : RoundingUnit.WORLD;
            if (_useMaxRoundness)
            {
                _graphic.SetCornerRounding(1f);
            }
            else
            {
                _graphic.SetCornerRounding(ConvertPixelsToDttAmount(_cornerRadius));
            }

            _graphic.color = ResolveProceduralGraphicColor();
            _graphic.DistanceFalloff = ResolveDistanceFalloff();
            return true;
        }

        private RoundedImage UpgradeLegacyImageToRoundedImage()
        {
            var graphics = GetComponents<Graphic>();
            if (graphics == null || graphics.Length == 0)
            {
                return null;
            }

            Image legacyImage = null;
            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                if (graphic is RoundedImage roundedImage)
                {
                    return roundedImage;
                }

                if (graphic is Image image)
                {
                    legacyImage = image;
                    continue;
                }

                return null;
            }

            if (legacyImage == null)
            {
                return null;
            }

            var legacyState = LegacyImageState.Capture(legacyImage);
            var selectables = GetComponents<Selectable>();

            if (Application.isPlaying)
            {
                DestroyImmediate(legacyImage);
            }
            else
            {
                DestroyImmediate(legacyImage);
            }

            var upgradedImage = gameObject.AddComponent<RoundedImage>();
            if (upgradedImage == null)
            {
                return null;
            }

            legacyState.ApplyTo(upgradedImage);
            RebindSelectableTargets(selectables, legacyImage, upgradedImage);
            return upgradedImage;
        }

        private RoundedImage TryCreateRoundedImage()
        {
            var graphics = GetComponents<Graphic>();
            if (graphics != null)
            {
                for (var i = 0; i < graphics.Length; i++)
                {
                    var graphic = graphics[i];
                    if (graphic is RoundedImage roundedImage)
                    {
                        return roundedImage;
                    }

                    if (graphic != null)
                    {
                        return null;
                    }
                }
            }

            return gameObject.AddComponent<RoundedImage>();
        }

        private void WarnAboutUnsupportedGraphicTarget()
        {
            if (_graphicCompatibilityWarningIssued)
            {
                return;
            }

            _graphicCompatibilityWarningIssued = true;
            var conflictingGraphic = GetComponent<Graphic>();
            var conflictingType = conflictingGraphic != null ? conflictingGraphic.GetType().Name : "unknown";
            Debug.LogWarning(
                $"[AIToUGUI] Skipping procedural shape on '{name}' because the target already contains an unsupported graphic component ({conflictingType}).",
                this);
        }

        private void ConfigureBorder()
        {
            if (_outlineWidth <= 0.001f || _outlineColor.a <= 0.001f)
            {
                RemoveComponent(ref _border);
                return;
            }

            _border = GetComponent<Border>();
            if (_border == null)
            {
                _border = gameObject.AddComponent<Border>();
            }

            if (_border == null || _border.ParentRoundedImage == null || _border.BorderRoundedImage == null)
            {
                _applyPending = true;
                return;
            }

            _border.RoundingUnit = RoundingUnit.PERCENTAGE;
            try
            {
                _border.BorderThickness = ConvertPixelsToDttAmount(_outlineWidth);
                _border.Color = _outlineColor;
                _border.RenderOutside = false;
            }
            catch (NullReferenceException)
            {
                _applyPending = true;
                return;
            }
        }

        private void ConfigureGradient()
        {
            if (!_useGradient || _gradientColor.a <= 0.001f)
            {
                RemoveComponent(ref _gradientEffect);
                if (_graphic != null)
                {
                    _graphic.material = null;
                }
                return;
            }

            _gradientEffect = GetComponent<GradientEffect>();
            if (_gradientEffect == null)
            {
                _gradientEffect = gameObject.AddComponent<GradientEffect>();
            }

            PrimeGradientEffect(
                _gradientEffect,
                BuildGradient(_enableFill ? _fillColor : Color.clear, _gradientColor),
                GradientEffect.GradientType.LINEAR,
                Vector2.zero,
                1f,
                ResolveGradientRotation(_gradientDirection));
            _gradientEffect.UpdateGradient();
        }

        private void ConfigureGraphic()
        {
            if (_graphic == null)
            {
                return;
            }

            _graphic.color = ResolveProceduralGraphicColor();
        }

        private void ConfigureShadowEffect()
        {
            _shadowEffect = GetComponent<AIToUGUIShadowEffect>();
            if (_shadowEffect == null)
            {
                _shadowEffect = gameObject.AddComponent<AIToUGUIShadowEffect>();
            }

            var shadowSize = _shadowSize;
            var shadowBlur = _shadowBlur;
            var shadowColor = _shadowColor;
            if (ShouldToneDownShadow())
            {
                var diagonal = IsDiagonalRotation();
                shadowSize *= diagonal ? 0.25f : 0.4f;
                shadowBlur *= diagonal ? 0.6f : 0.8f;
                shadowColor.a *= diagonal ? 0.2f : 0.35f;
            }

            _shadowEffect.Configure(
                shadowSize,
                shadowBlur,
                shadowColor,
                _enableGlow,
                _glowColor,
                _glowBlur,
                _glowIntensity);
        }

        private static Gradient BuildGradient(Color primary, Color secondary)
        {
            return new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(primary, 0f),
                    new GradientColorKey(secondary, 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(primary.a, 0f),
                    new GradientAlphaKey(secondary.a, 1f)
                }
            };
        }

        private static float ResolveGradientRotation(AIToUGUIGradientDirection direction)
        {
            switch (direction)
            {
                case AIToUGUIGradientDirection.Horizontal:
                    return 90f;
                case AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight:
                    return 315f;
                case AIToUGUIGradientDirection.DiagonalBottomLeftToTopRight:
                    return 45f;
                default:
                    return 180f;
            }
        }

        private Color ResolveProceduralGraphicColor()
        {
            if (!_enableFill)
            {
                return new Color(1f, 1f, 1f, 0f);
            }

            if (_useGradient && _gradientColor.a > 0.001f)
            {
                // DTT's gradient shaders multiply texture color by Image.color.
                // Use white here so the authored gradient colors are not darkened twice.
                return Color.white;
            }

            return _fillColor;
        }

        private float ResolveDistanceFalloff()
        {
            if (!TryGetRectSize(out var size))
            {
                return 0.5f;
            }

            var shortSide = Mathf.Max(1f, Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y)));
            var longSide = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y));
            var aspectRatio = longSide / shortSide;
            var falloff = 0.5f;

            if (shortSide <= 18f || aspectRatio >= 4f)
            {
                falloff = 0.25f;
            }
            else if (shortSide <= 28f || aspectRatio >= 2.5f)
            {
                falloff = 0.35f;
            }

            if (IsDiagonalRotation())
            {
                falloff *= 0.8f;
            }

            return Mathf.Clamp(falloff, 0.15f, 0.45f);
        }

        private bool ShouldToneDownShadow()
        {
            return IsSlenderShape() || IsDiagonalRotation();
        }

        private bool IsSlenderShape()
        {
            if (!TryGetRectSize(out var size))
            {
                return false;
            }

            var shortSide = Mathf.Max(0.01f, Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y)));
            var longSide = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y));
            return shortSide <= 12f || longSide / shortSide >= 5f;
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

        private static void PrimeGradientEffect(
            GradientEffect effect,
            Gradient gradient,
            GradientEffect.GradientType type,
            Vector2 offset,
            float scale,
            float rotation)
        {
            if (effect == null)
            {
                return;
            }

            GradientEffectGradientField?.SetValue(effect, gradient ?? new Gradient());
            GradientEffectTypeField?.SetValue(effect, type);
            GradientEffectOffsetField?.SetValue(effect, offset);
            GradientEffectScaleField?.SetValue(effect, scale);
            GradientEffectRotationField?.SetValue(effect, rotation);
            GradientEffectBatchingField?.SetValue(effect, true);
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

        private static void RebindSelectableTargets(Selectable[] selectables, Graphic previousGraphic, Graphic nextGraphic)
        {
            if (selectables == null || previousGraphic == null || nextGraphic == null)
            {
                return;
            }

            for (var i = 0; i < selectables.Length; i++)
            {
                var selectable = selectables[i];
                if (selectable != null && selectable.targetGraphic == previousGraphic)
                {
                    selectable.targetGraphic = nextGraphic;
                }
            }
        }

        [Serializable]
        private struct LegacyImageState
        {
            public Sprite sprite;
            public Sprite overrideSprite;
            public Color color;
            public Material material;
            public bool raycastTarget;
            public bool maskable;
            public bool preserveAspect;
            public bool fillCenter;
            public Image.Type type;
            public Image.FillMethod fillMethod;
            public int fillOrigin;
            public float fillAmount;
            public bool fillClockwise;
            public bool enabled;
            public bool useSpriteMesh;
            public float pixelsPerUnitMultiplier;
            public float alphaHitTestMinimumThreshold;
            public bool canRestoreAlphaHitTestMinimumThreshold;

            public static LegacyImageState Capture(Image image)
            {
                return new LegacyImageState
                {
                    sprite = image != null ? image.sprite : null,
                    overrideSprite = image != null ? image.overrideSprite : null,
                    color = image != null ? image.color : Color.white,
                    material = image != null ? image.material : null,
                    raycastTarget = image != null && image.raycastTarget,
                    maskable = image == null || image.maskable,
                    preserveAspect = image != null && image.preserveAspect,
                    fillCenter = image == null || image.fillCenter,
                    type = image != null ? image.type : Image.Type.Simple,
                    fillMethod = image != null ? image.fillMethod : Image.FillMethod.Radial360,
                    fillOrigin = image != null ? image.fillOrigin : 0,
                    fillAmount = image != null ? image.fillAmount : 1f,
                    fillClockwise = image == null || image.fillClockwise,
                    enabled = image == null || image.enabled,
                    useSpriteMesh = image != null && image.useSpriteMesh,
                    pixelsPerUnitMultiplier = image != null ? image.pixelsPerUnitMultiplier : 1f,
                    alphaHitTestMinimumThreshold = image != null ? image.alphaHitTestMinimumThreshold : 0f,
                    canRestoreAlphaHitTestMinimumThreshold = image != null && CanAssignAlphaHitTestMinimumThreshold(image.sprite, image.overrideSprite)
                };
            }

            public void ApplyTo(Image image)
            {
                if (image == null)
                {
                    return;
                }

                image.sprite = sprite;
                image.overrideSprite = overrideSprite;
                image.color = color;
                image.material = material;
                image.raycastTarget = raycastTarget;
                image.maskable = maskable;
                image.preserveAspect = preserveAspect;
                image.fillCenter = fillCenter;
                image.type = type;
                image.fillMethod = fillMethod;
                image.fillOrigin = fillOrigin;
                image.fillAmount = fillAmount;
                image.fillClockwise = fillClockwise;
                image.useSpriteMesh = useSpriteMesh;
                image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
                if (canRestoreAlphaHitTestMinimumThreshold &&
                    CanAssignAlphaHitTestMinimumThreshold(image.sprite, image.overrideSprite))
                {
                    image.alphaHitTestMinimumThreshold = alphaHitTestMinimumThreshold;
                }

                image.enabled = enabled;
            }

            private static bool CanAssignAlphaHitTestMinimumThreshold(Sprite sprite, Sprite overrideSprite)
            {
                return IsReadableNonCrunchTexture((overrideSprite != null ? overrideSprite.texture : null) ??
                                                  (sprite != null ? sprite.texture : null));
            }

            private static bool IsReadableNonCrunchTexture(Texture2D texture)
            {
                if (texture == null)
                {
                    return false;
                }

                try
                {
                    return texture.isReadable && !texture.format.ToString().Contains("Crunch", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
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

        private float ConvertPixelsToDttAmount(float pixels)
        {
            pixels = Mathf.Max(0f, pixels);
            var shortSide = ResolveShortSide();
            if (shortSide <= 0.001f)
            {
                return 0f;
            }

            return Mathf.Clamp01(pixels / (shortSide * 0.5f));
        }

        private float ResolveShortSide()
        {
            var rectTransform = CachedRectTransform;
            if (rectTransform == null)
            {
                return 0f;
            }

            Rect rect;
            try
            {
                rect = rectTransform.rect;
            }
            catch (MissingReferenceException)
            {
                _rectTransform = null;
                return 0f;
            }

            var shortSide = Mathf.Min(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
            if (shortSide > 0.001f)
            {
                return shortSide;
            }

            var sizeDelta = rectTransform.sizeDelta;
            return Mathf.Min(Mathf.Abs(sizeDelta.x), Mathf.Abs(sizeDelta.y));
        }

        private bool TryGetRectSize(out Vector2 size)
        {
            var rectTransform = CachedRectTransform;
            if (rectTransform == null)
            {
                size = Vector2.zero;
                return false;
            }

            try
            {
                size = rectTransform.rect.size;
            }
            catch (MissingReferenceException)
            {
                _rectTransform = null;
                size = Vector2.zero;
                return false;
            }

            if (size.sqrMagnitude > 0.0001f)
            {
                return true;
            }

            size = rectTransform.sizeDelta;
            return size.sqrMagnitude > 0.0001f;
        }

        private bool HasValidRectTransform()
        {
            var rectTransform = CachedRectTransform;
            if (rectTransform == null)
            {
                return false;
            }

            try
            {
                _ = rectTransform.rect;
                return true;
            }
            catch (MissingReferenceException)
            {
                _rectTransform = null;
                return false;
            }
        }

        private static bool ApproximatelyEqual(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.01f && Mathf.Abs(a.y - b.y) <= 0.01f;
        }

        protected virtual void OnDisable()
        {
            _applyPending = false;
            CancelQueuedEditorApply();
        }

        protected virtual void OnDestroy()
        {
            CancelQueuedEditorApply();
        }

        private void CancelQueuedEditorApply()
        {
#if UNITY_EDITOR
            if (_editorApplyQueued)
            {
                EditorApplication.delayCall -= ApplyQueuedInEditor;
                _editorApplyQueued = false;
            }
#endif
        }
    }

    public sealed class AIToUGUIShapeAdapter : AIToUGUIProceduralShapeAdapter
    {
    }
}
