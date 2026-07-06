using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [CreateAssetMenu(fileName = "AIToUGUIPage", menuName = "AIToUGUI/Page Definition")]
    public sealed class AIToUGUIPageDefinition : ScriptableObject
    {
        [Title("基础信息")]
        [LabelText("页面 ID")]
        public string pageId = "page_id";

        [LabelText("显示名称")]
        public string displayName = "Page";

        [Title("来源")]
        [LabelText("HTML 文件")]
        public TextAsset htmlAsset;

        [LabelText("局部样式表")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<TextAsset> localStyleSheets = new List<TextAsset>();

        [LabelText("页面覆盖主题")]
        public AIToUGUIThemeDefinition overrideTheme;

        [LabelText("源相对路径")]
        public string sourceRelativePath;

        [Title("输出")]
        [LabelText("Prefab 名称")]
        public string prefabName = "PagePanel";

        [LabelText("Runtime Page ID")]
        public string runtimePageId = "page_id";

        [LabelText("目标 UI 层")]
        public UILayer targetLayer = UILayer.Normal;

        [LabelText("挂载业务脚本")]
        [InfoBox("Panel binding is now opt-in. Leave this disabled unless the page explicitly needs a panel script.", InfoMessageType.None)]
        public bool attachPanelComponent = false;

        [ShowIf(nameof(attachPanelComponent))]
        [LabelText("面板组件类型")]
        public string panelComponentTypeName;
    }
}
