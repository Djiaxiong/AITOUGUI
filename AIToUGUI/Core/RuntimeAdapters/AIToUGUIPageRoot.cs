using UnityEngine;

namespace AIToUGUI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AIToUGUIPageRoot : MonoBehaviour
    {
        public string siteId;
        public string pageId;
        public string runtimePageId;
        public string themeId;
        public string resourceLogicalPath;
        public string generatedAt;
        public Vector2 designResolution = new Vector2(1920f, 1080f);
        public UILayer targetLayer = UILayer.Normal;
        public AIToUGUIBakeMetadata bakeMetadata;

        [SerializeField] private bool fitToParent = true;

        private RectTransform _rectTransform;
        private Vector2 _lastParentSize = new Vector2(-1f, -1f);
        private Vector2 _lastDesignResolution = new Vector2(-1f, -1f);
        private bool _isApplying;

        public AIToUGUIBakeMetadata BakeMetadata => bakeMetadata;

        public void ApplyNow()
        {
            ApplyLayout(true);
        }

        public void BindRuntimeMetadata(string resolvedRuntimePageId, AIToUGUIBakeMetadata metadata)
        {
            runtimePageId = resolvedRuntimePageId ?? string.Empty;
            bakeMetadata = metadata;
        }

        public AIToUGUIBakeExportedNodeInfo FindExportedNode(string nodeName)
        {
            if (bakeMetadata == null ||
                bakeMetadata.exportedNodes == null ||
                string.IsNullOrWhiteSpace(nodeName))
            {
                return null;
            }

            for (var i = 0; i < bakeMetadata.exportedNodes.Count; i++)
            {
                var node = bakeMetadata.exportedNodes[i];
                if (node != null && string.Equals(node.nodeName, nodeName, System.StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        public AIToUGUIBakeExportedNodeInfo FindNodeBySlotId(string slotId)
        {
            return FindNodeBySemanticId(slotId, candidate => candidate.slotId);
        }

        public AIToUGUIBakeExportedNodeInfo FindNodeByContainerId(string containerId)
        {
            return FindNodeBySemanticId(containerId, candidate => candidate.containerId);
        }

        public AIToUGUIBakeExportedNodeInfo FindNodeByTemplateId(string templateId)
        {
            return FindNodeBySemanticId(templateId, candidate => candidate.templateId);
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

            var rect = CachedRectTransform;
            if (rect == null)
            {
                return;
            }

            var parentRect = rect.parent as RectTransform;
            var parentSize = parentRect != null
                ? new Vector2(Mathf.Max(1f, parentRect.rect.width), Mathf.Max(1f, parentRect.rect.height))
                : new Vector2(Mathf.Max(1f, designResolution.x), Mathf.Max(1f, designResolution.y));

            if (!force &&
                ApproximatelyEqual(parentSize, _lastParentSize) &&
                ApproximatelyEqual(designResolution, _lastDesignResolution))
            {
                return;
            }

            _isApplying = true;
            try
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
                _lastParentSize = parentSize;
                _lastDesignResolution = designResolution;
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

        private AIToUGUIBakeExportedNodeInfo FindNodeBySemanticId(
            string semanticId,
            System.Func<AIToUGUIBakeExportedNodeInfo, string> selector)
        {
            if (bakeMetadata == null ||
                bakeMetadata.exportedNodes == null ||
                string.IsNullOrWhiteSpace(semanticId) ||
                selector == null)
            {
                return null;
            }

            for (var i = 0; i < bakeMetadata.exportedNodes.Count; i++)
            {
                var node = bakeMetadata.exportedNodes[i];
                if (node != null && string.Equals(selector(node), semanticId, System.StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }
    }
}
