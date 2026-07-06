using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [Serializable]
    public sealed class AIToUGUIRuntimePageEntry
    {
        [LabelText("Runtime Page ID")]
        public string runtimePageId;

        [LabelText("Site ID")]
        public string siteId;

        [LabelText("Page ID")]
        public string pageId;

        [LabelText("Prefab Logical Path")]
        public string prefabLogicalPath;

        [LabelText("Prefab Name")]
        public string prefabName;

        [LabelText("Target Layer")]
        public UILayer targetLayer = UILayer.Normal;

        [LabelText("Panel Component Type")]
        public string panelComponentTypeName;

        [LabelText("Metadata Asset Path")]
        public string metadataAssetPath;

        [AssetsOnly]
        [LabelText("Metadata Asset")]
        public AIToUGUIBakeMetadata metadataAsset;
    }

    [CreateAssetMenu(fileName = "AIToUGUIRuntimeRegistry", menuName = "AIToUGUI/Runtime Registry")]
    public sealed class AIToUGUIRuntimeRegistry : ScriptableObject
    {
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIRuntimePageEntry> pages = new List<AIToUGUIRuntimePageEntry>();

        public AIToUGUIRuntimePageEntry FindPage(string runtimePageId)
        {
            if (string.IsNullOrWhiteSpace(runtimePageId) || pages == null)
            {
                return null;
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                if (page != null && string.Equals(page.runtimePageId, runtimePageId, StringComparison.Ordinal))
                {
                    return page;
                }
            }

            return null;
        }
    }
}
