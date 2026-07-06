using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [Serializable]
    public sealed class AIToUGUICompiledBundlePageSummary
    {
        [LabelText("Page ID")]
        public string pageId;

        [LabelText("Display Name")]
        public string displayName;

        [LabelText("Prefab Name")]
        public string prefabName;

        [LabelText("Target Layer")]
        public UILayer targetLayer = UILayer.Normal;

        [LabelText("Logical Path")]
        public string logicalPath;

        [LabelText("Runtime Page ID")]
        public string runtimePageId;

        [LabelText("Attach Panel Component")]
        public bool attachPanelComponent;

        [LabelText("Panel Component Type")]
        public string panelComponentTypeName;
    }

    [CreateAssetMenu(fileName = "AIToUGUICompiledBundle", menuName = "AIToUGUI/Compiled Bundle Definition")]
    public sealed class AIToUGUICompiledBundleDefinition : ScriptableObject
    {
        [Title("Source")]
        [LabelText("Bundle JSON")]
        public TextAsset bundleJson;

        [LabelText("Bundle JSON Path")]
        public string bundleJsonAssetPath;

        [Title("Site")]
        [LabelText("Schema Version")]
        public string schemaVersion = "1.1";

        [LabelText("Site ID")]
        public string siteId = "site_id";

        [LabelText("Display Name")]
        public string displayName = "Compiled Bundle";

        [LabelText("Design Resolution")]
        public Vector2 designResolution = new Vector2(1920f, 1080f);

        [Title("Output")]
        [LabelText("Prefab Output Root")]
        public string prefabOutputRoot = AIToUGUIGeneratedAssetPaths.GetPrefabsRoot("site_id");

        [LabelText("Metadata Output Root")]
        public string metadataOutputRoot = AIToUGUIGeneratedAssetPaths.GetMetadataRoot("site_id");

        [Title("Export Options")]
        [LabelText("Keep Export Node Markers")]
        public bool keepExportNodeMarkers = true;

        [LabelText("Keep Asset Binding Manifests")]
        public bool keepAssetBindingManifests;

        [LabelText("Use Overflow Mask Hosts")]
        public bool useOverflowMaskHosts = true;

        [Title("Pages")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUICompiledBundlePageSummary> pages = new List<AIToUGUICompiledBundlePageSummary>();
    }
}
