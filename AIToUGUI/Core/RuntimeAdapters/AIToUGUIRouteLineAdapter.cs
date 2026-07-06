using DTT.UI.ProceduralUI;
using UnityEngine;
using UnityEngine.UI;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AIToUGUIRouteLineAdapter : MonoBehaviour
    {
        private static readonly AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent |
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;

        [SerializeField] private Color _lineColor = Color.white;
        [SerializeField] private bool _hasConfiguredState;
        [SerializeField] private LineGraphic _graphic;
        [SerializeField] private RectTransform _rectTransform;
        private bool _isApplying;
        private Vector2 _lastRectSize = new Vector2(-1f, -1f);
        private float _lastRotationZ = float.NaN;

        public void Configure(Color lineColor)
        {
            _lineColor = lineColor;
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

        private void OnValidate()
        {
            if (_hasConfiguredState)
            {
                ApplyNow();
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_hasConfiguredState)
            {
                ApplyNow();
            }
        }

        private void OnTransformParentChanged()
        {
            if (_hasConfiguredState)
            {
                EnsureCanvasChannels();
            }
        }

        private void OnDestroy()
        {
            _graphic = null;
            _rectTransform = null;
        }

        public void ApplyNow()
        {
            if (!_hasConfiguredState || _isApplying)
            {
                return;
            }

            _isApplying = true;
            try
            {
                EnsureCanvasChannels();
                CleanupLegacyComponents();
                EnsureGraphic();
                ConfigureGraphic();
                CacheAppliedState();
            }
            finally
            {
                _isApplying = false;
            }
        }

        private void LateUpdate()
        {
            if (_hasConfiguredState && HasTransformStateChanged())
            {
                ApplyNow();
            }
        }

        private void CleanupLegacyComponents()
        {
            DestroyAllChildren();

            RemoveComponent(GetComponent<CanvasGroup>());
            RemoveComponent(GetComponent<AIToUGUIShapeAdapter>());
            RemoveComponent(GetComponent<AIToUGUIWindinatorShapeAdapter>());
            RemoveComponent(GetComponent<AIToUGUIShadowEffect>());
            RemoveComponent(GetComponent<Image>());
            RemoveComponent(GetComponent<RawImage>());
            RemoveComponent(GetComponent<RoundedImage>());
            RemoveComponent(GetComponent<Border>());
            RemoveComponent(GetComponent<GradientEffect>());
            RemoveComponent(GetComponent<RectangleGraphic>());
            RemoveComponent(GetComponent<PolygonGraphic>());
        }

        private void DestroyAllChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null)
                {
                    RemoveGameObject(child.gameObject);
                }
            }
        }

        private void EnsureGraphic()
        {
            _rectTransform ??= GetComponent<RectTransform>();
            _graphic = GetComponent<LineGraphic>();
            if (_graphic == null)
            {
                _graphic = gameObject.AddComponent<LineGraphic>();
            }
        }

        private void EnsureCanvasChannels()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.rootCanvas.additionalShaderChannels |= RequiredChannels;
            }
        }

        private void ConfigureGraphic()
        {
            if (_graphic == null)
            {
                return;
            }

            _graphic.color = _lineColor;
            _graphic.Alpha = 1f;
            _graphic.GraphicBlur = 0f;
            _graphic.Texture = null;
            _graphic.raycastTarget = false;
            _graphic.maskable = true;
            _graphic.SetOutline(Color.clear, 0f);
            _graphic.SetShadow(Color.clear, 0f, 0f);
            _graphic.Roundness = ResolveLineRoundness();
            _graphic.GraphicBlur = ResolveLineGraphicBlur();
            _graphic.Size = ResolveLineThickness();
            _graphic.Points = BuildLinePoints();
            _graphic.SetVerticesDirty();
            _graphic.SetMaterialDirty();
            _graphic.SetLayoutDirty();
        }

        private StaticArray<Vector4> BuildLinePoints()
        {
            var points = new StaticArray<Vector4>(1);
            var size = GetRectSize();

            if (size.x >= size.y)
            {
                // LineRenderer.shader expects normalized endpoints in [0, 1], not local pixel coordinates.
                points.Add(new Vector4(0f, 0.5f, 1f, 0.5f));
            }
            else
            {
                points.Add(new Vector4(0.5f, 0f, 0.5f, 1f));
            }

            return points;
        }

        private float ResolveLineThickness()
        {
            var size = GetRectSize();
            var shortSide = Mathf.Max(0.5f, Mathf.Min(size.x, size.y));
            var thickness = Mathf.Max(0.5f, shortSide * 0.48f);

            if (IsTiltedLine())
            {
                if (shortSide <= 2f)
                {
                    thickness = Mathf.Max(thickness, 0.72f);
                }
                else if (shortSide <= 4f)
                {
                    thickness = Mathf.Max(thickness, shortSide * 0.54f);
                }
            }

            return thickness;
        }

        private float ResolveLineGraphicBlur()
        {
            var size = GetRectSize();
            var shortSide = Mathf.Max(1f, Mathf.Min(size.x, size.y));
            var blur = 0.15f;

            if (shortSide <= 6f)
            {
                blur = 0.35f;
            }
            else if (shortSide <= 12f)
            {
                blur = 0.25f;
            }

            if (!IsTiltedLine())
            {
                return blur;
            }

            if (shortSide <= 2f)
            {
                return Mathf.Max(blur, 0.62f);
            }

            if (shortSide <= 4f)
            {
                return Mathf.Max(blur, 0.48f);
            }

            if (shortSide <= 8f)
            {
                return Mathf.Max(blur, 0.34f);
            }

            return Mathf.Max(blur, 0.22f);
        }

        private float ResolveLineRoundness()
        {
            if (!IsTiltedLine())
            {
                return 0f;
            }

            var shortSide = Mathf.Max(1f, Mathf.Min(GetRectSize().x, GetRectSize().y));
            if (shortSide <= 2f)
            {
                return 0.22f;
            }

            if (shortSide <= 4f)
            {
                return 0.12f;
            }

            return 0.06f;
        }

        private Vector2 GetRectSize()
        {
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }

            if (_rectTransform == null)
            {
                return Vector2.one;
            }

            return new Vector2(
                Mathf.Abs(_rectTransform.rect.width),
                Mathf.Abs(_rectTransform.rect.height));
        }

        private bool IsTiltedLine()
        {
            var normalized = Mathf.Abs(Mathf.Repeat(GetLocalRotationZ(), 180f));
            var axisDistance = Mathf.Min(
                normalized,
                Mathf.Abs(normalized - 90f),
                Mathf.Abs(normalized - 180f));
            return axisDistance >= 1f;
        }

        private float GetLocalRotationZ()
        {
            return _rectTransform != null ? _rectTransform.localEulerAngles.z : transform.localEulerAngles.z;
        }

        private bool HasTransformStateChanged()
        {
            var size = GetRectSize();
            var rotationZ = GetLocalRotationZ();
            return Vector2.SqrMagnitude(size - _lastRectSize) > 0.0001f ||
                   float.IsNaN(_lastRotationZ) ||
                   Mathf.Abs(Mathf.DeltaAngle(rotationZ, _lastRotationZ)) > 0.01f;
        }

        private void CacheAppliedState()
        {
            _lastRectSize = GetRectSize();
            _lastRotationZ = GetLocalRotationZ();
        }

        private static void RemoveComponent(Component component)
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

        private static void RemoveGameObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(gameObject);
            }
        }
    }
}
