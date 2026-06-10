using UnityEngine;

namespace AIToUGUI.Lite
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AIToUGUI Lite/Preview Mount")]
    public sealed class AIToUGUILitePreviewMount : MonoBehaviour
    {
        public bool clearBeforePreview = true;
    }
}
