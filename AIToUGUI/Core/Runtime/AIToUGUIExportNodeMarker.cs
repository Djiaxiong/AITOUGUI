using UnityEngine;

namespace AIToUGUI
{
    [DisallowMultipleComponent]
    public sealed class AIToUGUIExportNodeMarker : MonoBehaviour
    {
        public string nodeName;
        public AIToUGUIControlType controlType = AIToUGUIControlType.Div;
        public string role;
        public string elementId;
        public string variantId;
        public string shapeId;
        public string frameId;
        public string slotId;
        public string containerId;
        public string templateId;
        public string componentFamily;
        public string componentVariant;
        public string renderStrategy = AIToUGUIElementContractUtility.ProceduralRenderStrategyId;
        public bool isPrefabBacked;
    }

    [DisallowMultipleComponent]
    public sealed class AIToUGUIDynamicTemplateInstance : MonoBehaviour
    {
        public string slotId;
        public string containerId;
        public string templateId;
    }
}
