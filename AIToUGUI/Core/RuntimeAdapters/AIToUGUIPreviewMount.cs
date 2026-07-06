using UnityEngine;

namespace AIToUGUI
{
    
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AIToUGUI/Preview Mount")]
    public sealed class AIToUGUIPreviewMount : MonoBehaviour
    {
        public bool clearBeforePreview = true;
    }
 
}
