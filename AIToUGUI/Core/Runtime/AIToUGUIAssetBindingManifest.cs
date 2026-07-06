using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIToUGUI
{
    [Serializable]
    public sealed class AIToUGUIAssetBindingEntry
    {
        public string assetId;
        public string assetType = nameof(AIToUGUIAssetType.Icon);
        public string usage;
        public string importMode = nameof(AIToUGUIAssetImportMode.Auto);
        public string source;
        public string notes;
        public string logicalAssetPath;
        public Vector4 sliceBorder;
        public float pixelsPerUnit = 100f;
        public float preferredWidth;
        public float preferredHeight;
        public string tintPolicy;
        public string atlasGroup;
    }

    [DisallowMultipleComponent]
    public sealed class AIToUGUIAssetBindingManifest : MonoBehaviour
    {
        public string componentFamily;
        public string componentVariant;
        public string renderStrategy = AIToUGUIElementContractUtility.ProceduralRenderStrategyId;
        public List<AIToUGUIAssetBindingEntry> assetRefs = new List<AIToUGUIAssetBindingEntry>();
        public List<string> fidelityNotes = new List<string>();
    }
}
