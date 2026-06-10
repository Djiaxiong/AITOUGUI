using UnityEngine;

namespace AIToUGUI.Lite
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AIToUGUILitePageRoot : MonoBehaviour
    {
        public string siteId;
        public string pageId;
        public Vector2 designResolution = new Vector2(1920f, 1080f);

        [SerializeField] private bool fitToParent = true;

        private RectTransform _rectTransform;
        private Vector2 _lastParentSize = new Vector2(-1f, -1f);
        private bool _isApplying;

        public void ApplyNow()
        {
            ApplyLayout(true);
        }

        private void OnEnable()
        {
            ApplyLayout(true);
        }

        private void OnValidate()
        {
            ApplyLayout(true);
        }

        private void OnTransformParentChanged()
        {
            ApplyLayout(true);
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyLayout(false);
        }

        private void LateUpdate()
        {
            ApplyLayout(false);
        }

        private void ApplyLayout(bool force)
        {
            if (_isApplying || !fitToParent)
            {
                return;
            }

            var rectTransform = CachedRectTransform;
            if (rectTransform == null)
            {
                return;
            }

            var parentRect = rectTransform.parent as RectTransform;
            var parentSize = parentRect != null
                ? new Vector2(Mathf.Max(1f, parentRect.rect.width), Mathf.Max(1f, parentRect.rect.height))
                : new Vector2(Mathf.Max(1f, designResolution.x), Mathf.Max(1f, designResolution.y));

            if (!force && ApproximatelyEqual(parentSize, _lastParentSize))
            {
                return;
            }

            _isApplying = true;
            try
            {
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.localRotation = Quaternion.identity;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
                rectTransform.sizeDelta = Vector2.zero;
                rectTransform.localScale = Vector3.one;
                _lastParentSize = parentSize;
            }
            finally
            {
                _isApplying = false;
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

        private static bool ApproximatelyEqual(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.01f && Mathf.Abs(a.y - b.y) <= 0.01f;
        }
    }
}
