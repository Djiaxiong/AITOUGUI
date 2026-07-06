using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [CreateAssetMenu(fileName = "AIToUGUISite", menuName = "AIToUGUI/Site Definition")]
    public sealed class AIToUGUISiteDefinition : ScriptableObject
    {
        [Title("基础信息")]
        [LabelText("站点 ID")]
        public string siteId = "site_id";

        [LabelText("显示名称")]
        public string displayName = "UI Site";

        [LabelText("设计分辨率")]
        public Vector2 designResolution = new Vector2(1920f, 1080f);

        [LabelText("默认 UI 层")]
        public UILayer defaultLayer = UILayer.Normal;

        [Title("来源")]
        [InfoBox("推荐流程：先让外部 AI 生成整站 HTML 包，本地浏览器确认后，再把 site.json 导入到 Unity。", InfoMessageType.None)]
        [LabelText("站点清单文件")]
        public TextAsset manifestAsset;

        [LabelText("源根目录")]
        public string sourceRootFolder;

        [Title("共享配置")]
        [LabelText("共享主题")]
        public AIToUGUIThemeDefinition sharedTheme;

        [LabelText("基础元素库")]
        public AIToUGUIElementLibrary elementLibrary;

        [LabelText("共享样式表")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<TextAsset> sharedStyleSheets = new List<TextAsset>();

        [Title("页面列表")]
        [LabelText("页面定义")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIPageDefinition> pages = new List<AIToUGUIPageDefinition>();

        [Title("输出路径")]
        [LabelText("Prefab 输出根目录")]
        public string prefabOutputRoot = AIToUGUIGeneratedAssetPaths.GetPrefabsRoot("site_id");

        [LabelText("Metadata 输出根目录")]
        public string metadataOutputRoot = AIToUGUIGeneratedAssetPaths.GetMetadataRoot("site_id");

        [Title("Export Options")]
        [LabelText("Keep Export Node Markers")]
        public bool keepExportNodeMarkers = true;

        [LabelText("Keep Asset Binding Manifests")]
        public bool keepAssetBindingManifests;

        [LabelText("Use Overflow Mask Hosts")]
        public bool useOverflowMaskHosts = true;
    }
}
