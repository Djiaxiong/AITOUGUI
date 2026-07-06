using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName="UIConfig", menuName="GameConfig/UIConfig", order=2)]
public class UIConfig : ScriptableObject
{
    [Header("Canvas")]
    public RenderMode renderMode = RenderMode.ScreenSpaceCamera;
    public string sortingLayerName = "UI";
    public int sortingOrder = 0;

    [Header("CanvasScaler")]
    public CanvasScaler.ScaleMode scaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    public Vector2 referenceResolution = new Vector2(1920, 1080);
    [Range(0,1)] public float matchWidthOrHeight = 0.5f;
    public float referencePixelsPerUnit = 100f;

    [Header("Camera")]
    public string uiLayerName = "UI";
    public float cameraDepth = 100f;
    public CameraClearFlags clearFlags = CameraClearFlags.Depth;
    public bool orthographic = true;
    public float orthoSize = 5f;
    public float near = -10f;
    public float far = 10f;
    public float planeDistance = 1f;

    [Header("EventSystem")]
    public bool createEventSystem = true;
}