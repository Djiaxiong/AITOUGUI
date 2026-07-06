using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [Serializable]
    public sealed class AIToUGUIBakeExportedNodeInfo
    {
        [LabelText("Node Name")]
        public string nodeName;

        [LabelText("Control Type")]
        public AIToUGUIControlType controlType = AIToUGUIControlType.Div;

        [LabelText("Role")]
        public string role;

        [LabelText("Element ID")]
        public string elementId;

        [LabelText("Variant ID")]
        public string variantId;

        [LabelText("Shape ID")]
        public string shapeId;

        [LabelText("Frame ID")]
        public string frameId;

        [LabelText("Slot ID")]
        public string slotId;

        [LabelText("Container ID")]
        public string containerId;

        [LabelText("Template ID")]
        public string templateId;

        [LabelText("Component Family")]
        public string componentFamily;

        [LabelText("Component Variant")]
        public string componentVariant;

        [LabelText("Render Strategy")]
        public string renderStrategy = AIToUGUIElementContractUtility.ProceduralRenderStrategyId;

        [LabelText("Prefab Backed")]
        public bool isPrefabBacked;

        [LabelText("Internal Generated")]
        public bool isInternalGenerated;

        [LabelText("Asset Refs")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIAssetReference> assetRefs = new List<AIToUGUIAssetReference>();

        [LabelText("Fidelity Notes")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> fidelityNotes = new List<string>();

        [LabelText("Component Types")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> componentTypes = new List<string>();

        [LabelText("Accessible Components")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> accessibleComponentTypes = new List<string>();
    }

    [CreateAssetMenu(fileName = "AIToUGUIBakeMetadata", menuName = "AIToUGUI/Bake Metadata")]
    public sealed class AIToUGUIBakeMetadata : ScriptableObject
    {
        [Title("Identity")]
        [LabelText("Site ID")]
        public string siteId;

        [LabelText("Page ID")]
        public string pageId;

        [LabelText("Runtime Page ID")]
        public string runtimePageId;

        [LabelText("Theme ID")]
        public string themeId;

        [Title("Source")]
        [LabelText("Bundle JSON Path")]
        public string bundleJsonAssetPath;

        [LabelText("Manifest Path")]
        public string manifestAssetPath;

        [LabelText("HTML Path")]
        public string htmlAssetPath;

        [LabelText("Source Relative Path")]
        public string sourceRelativePath;

        [Title("Output")]
        [LabelText("Prefab Path")]
        public string prefabAssetPath;

        [LabelText("Resource Logical Path")]
        public string resourceLogicalPath;

        [LabelText("Attach Panel Component")]
        public bool attachPanelComponent;

        [LabelText("Panel Component Type")]
        public string panelComponentTypeName;

        [LabelText("Target Layer")]
        public UILayer targetLayer = UILayer.Normal;

        [Title("Result")]
        [LabelText("Generated At")]
        public string generatedAt;

        [LabelText("Design Resolution")]
        public Vector2 designResolution;

        [LabelText("Errors")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> errors = new List<string>();

        [LabelText("Warnings")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> warnings = new List<string>();

        [LabelText("Exported Nodes")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIBakeExportedNodeInfo> exportedNodes = new List<AIToUGUIBakeExportedNodeInfo>();
    }
}
