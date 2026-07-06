using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AIToUGUIDashedBorderAdapter : MonoBehaviour
    {
        private const string OverlayName = "__ai_DashedBorder";

        [SerializeField] private float _thickness = 2f;
        [SerializeField] private Color _borderColor = Color.white;
        [SerializeField] private float _cornerRadius;
        [SerializeField] private bool _forceEllipse;
        [SerializeField] private float _dashLength = 14f;
        [SerializeField] private float _gapLength = 8f;
        [SerializeField] private RectTransform _overlayRect;
        [SerializeField] private AIToUGUIDashedBorderGraphic _graphic;
        private bool _applyPending;
#if UNITY_EDITOR
        private bool _editorApplyQueued;
#endif

        public void Configure(float thickness, Color borderColor, float cornerRadius, bool forceEllipse, float dashLength = 14f, float gapLength = 8f)
        {
            _thickness = thickness;
            _borderColor = borderColor;
            _cornerRadius = cornerRadius;
            _forceEllipse = forceEllipse;
            _dashLength = Mathf.Max(1f, Mathf.Round(dashLength));
            _gapLength = Mathf.Max(0f, Mathf.Round(gapLength));
            ApplyNow();
        }

        private void Awake()
        {
            ApplyNow();
        }

        private void OnEnable()
        {
            ApplyNow();
        }

        private void OnValidate()
        {
            QueueApply();
        }

        private void OnRectTransformDimensionsChange()
        {
            QueueApply();
        }

        public void ApplyNow()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                QueueApply();
                return;
            }
#endif

            if (_borderColor.a <= 0.001f || _thickness <= 0.001f)
            {
                CleanupOverlay();
                return;
            }

            EnsureOverlay();
            _graphic.Configure(_thickness, _borderColor, _cornerRadius, _forceEllipse, _dashLength, _gapLength);
        }

        private void LateUpdate()
        {
            if (_applyPending)
            {
                _applyPending = false;
                ApplyNow();
            }
        }

        private void QueueApply()
        {
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

            if (AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                AIToUGUIEditorUiMutationGuard.QueueEditorCallback(ref _editorApplyQueued, ApplyQueuedInEditor);
                return;
            }

            _applyPending = false;
            ApplyNow();
        }
#endif

        private void EnsureOverlay()
        {
            if (_overlayRect == null || _graphic == null)
            {
                var overlay = transform.Find(OverlayName) as RectTransform;
                if (overlay == null)
                {
                    var overlayGo = new GameObject(OverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(AIToUGUIDashedBorderGraphic));
                    overlay = overlayGo.GetComponent<RectTransform>();
                    overlay.SetParent(transform, false);
                }

                _overlayRect = overlay;
                _graphic = overlay.GetComponent<AIToUGUIDashedBorderGraphic>() ?? overlay.gameObject.AddComponent<AIToUGUIDashedBorderGraphic>();
            }

            _overlayRect.anchorMin = Vector2.zero;
            _overlayRect.anchorMax = Vector2.one;
            _overlayRect.pivot = new Vector2(0.5f, 0.5f);
            _overlayRect.offsetMin = Vector2.zero;
            _overlayRect.offsetMax = Vector2.zero;
            _overlayRect.SetAsLastSibling();
            _graphic.raycastTarget = false;
        }

        private void CleanupOverlay()
        {
            var overlay = transform.Find(OverlayName);
            if (overlay != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(overlay.gameObject);
                }
                else
                {
                    AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(overlay.gameObject);
                }
            }

            _overlayRect = null;
            _graphic = null;
        }
    }
}
