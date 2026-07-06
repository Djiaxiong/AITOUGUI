using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public interface IUISystem
{
    Canvas Canvas { get; }
    T ShowPanel<T>(string panelName, UILayer layer = UILayer.Normal, Action<T> cb = null) where T : BasePanel;
    Awaiter<T> ShowPanelAsync<T>(string panelName, UILayer layer = UILayer.Normal, Action<T> cb = null) where T : BasePanel;
    void HidePanel(string panelName);
    T CreatePanel<T>(string panelName, UILayer layer) where T : BasePanel;
    Awaiter<T> CreatePanelAsync<T>(string panelName, UILayer layer) where T : BasePanel;
    RectTransform GetLayerRoot(UILayer layer);
    Camera GetUICamera();
    void UseStandaloneUICamera();
    void AttachUICameraToBase(Camera baseCamera);
}

public sealed class UISystem : BaseMonoManager<UISystem>, IUISystem
{
    private UIConfig _cfg;
    private GameObject _uiRoot;
    private Camera _uiCamera;
    private Canvas _canvas;
    private EventSystem _eventSystem;

    private readonly Dictionary<UILayer, RectTransform> _layers = new Dictionary<UILayer, RectTransform>();
    private readonly Dictionary<string, BasePanel> _panels = new Dictionary<string, BasePanel>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoBootstrap()
    {
        _ = Instance;
    }

    public override void Init()
    {
        EnsureUIRoot();
    }

    public Canvas Canvas
    {
        get
        {
            EnsureUIRoot();
            return _canvas;
        }
    }

    public T ShowPanel<T>(string panelName, UILayer layer = UILayer.Normal, Action<T> cb = null) where T : BasePanel
    {
        EnsureUIRoot();

        if (!_panels.TryGetValue(panelName, out var panel) || panel == null)
        {
            panel = CreatePanel<T>(panelName, layer);
            if (panel == null)
            {
                return null;
            }

            _panels[panelName] = panel;
        }
        else
        {
            panel.transform.SetParent(GetLayerRoot(layer), false);
        }

        panel.InternalShow();
        cb?.Invoke(panel as T);
        return panel as T;
    }

    public Awaiter<T> ShowPanelAsync<T>(string panelName, UILayer layer = UILayer.Normal, Action<T> cb = null) where T : BasePanel
    {
        return MiniTask.FromCoroutine<T>(setResult => ShowPanelAsyncCoroutine(panelName, layer, cb, setResult));
    }

    public void HidePanel(string panelName)
    {
        if (_panels.TryGetValue(panelName, out var panel) && panel != null)
        {
            panel.InternalHide();
        }
    }

    public T CreatePanel<T>(string panelName, UILayer layer) where T : BasePanel
    {
        EnsureUIRoot();

        var go = AIToUGUIResourceService.InstantiatePrefab(panelName);
        if (go == null)
        {
            Debug.LogError($"[UISystem] Missing panel prefab: {panelName}");
            return null;
        }

        return BindPanel<T>(go, layer);
    }

    public Awaiter<T> CreatePanelAsync<T>(string panelName, UILayer layer) where T : BasePanel
    {
        return MiniTask.FromCoroutine<T>(setResult => CreatePanelAsyncCoroutine(panelName, layer, setResult));
    }

    public RectTransform GetLayerRoot(UILayer layer)
    {
        EnsureUIRoot();
        EnsureLayer(layer);
        return _layers.TryGetValue(layer, out var rt) ? rt : null;
    }

    public Camera GetUICamera()
    {
        EnsureUIRoot();
        return _uiCamera;
    }

    public void UseStandaloneUICamera()
    {
        EnsureUIRoot();
        if (_uiCamera == null)
        {
            return;
        }

        RemoveUICameraFromAllStacks();

        var uiData = _uiCamera.GetUniversalAdditionalCameraData();
        uiData.renderType = CameraRenderType.Base;
        _uiCamera.clearFlags = _cfg != null ? _cfg.clearFlags : CameraClearFlags.Depth;
    }

    public void AttachUICameraToBase(Camera baseCamera)
    {
        EnsureUIRoot();
        if (_uiCamera == null || baseCamera == null || _uiCamera == baseCamera)
        {
            return;
        }

        RemoveUICameraFromAllStacks();

        var baseData = baseCamera.GetUniversalAdditionalCameraData();
        baseData.renderType = CameraRenderType.Base;

        var uiData = _uiCamera.GetUniversalAdditionalCameraData();
        uiData.renderType = CameraRenderType.Overlay;
        if (!baseData.cameraStack.Contains(_uiCamera))
        {
            baseData.cameraStack.Add(_uiCamera);
        }
    }

    private IEnumerator ShowPanelAsyncCoroutine<T>(string panelName, UILayer layer, Action<T> cb, Action<T> setResult) where T : BasePanel
    {
        EnsureUIRoot();

        if (!_panels.TryGetValue(panelName, out var panel) || panel == null)
        {
            var createTask = CreatePanelAsync<T>(panelName, layer);
            while (!createTask.IsCompleted)
            {
                yield return null;
            }

            panel = createTask.GetResult();
            if (panel == null)
            {
                setResult(null);
                yield break;
            }

            _panels[panelName] = panel;
        }
        else
        {
            panel.transform.SetParent(GetLayerRoot(layer), false);
        }

        panel.InternalShow();
        cb?.Invoke(panel as T);
        setResult(panel as T);
    }

    private IEnumerator CreatePanelAsyncCoroutine<T>(string panelName, UILayer layer, Action<T> setResult) where T : BasePanel
    {
        var task = AIToUGUIResourceService.InstantiatePrefabAsync(panelName);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        var go = task.GetResult();
        if (go == null)
        {
            Debug.LogError($"[UISystem] Missing panel prefab: {panelName}");
            setResult(null);
            yield break;
        }

        setResult(BindPanel<T>(go, layer));
    }

    private T BindPanel<T>(GameObject go, UILayer layer) where T : BasePanel
    {
        if (!go.TryGetComponent<T>(out var panel))
        {
            panel = go.AddComponent<T>();
        }

        go.transform.SetParent(GetLayerRoot(layer), false);
        FitFullScreen(go.transform as RectTransform);

        panel.InternalInit();
        panel.InternalHide();
        return panel;
    }

    private void EnsureUIRoot()
    {
        _cfg = Resources.Load<UIConfig>("Config/UIConfig");

        if (_uiRoot != null && _canvas != null)
        {
            CacheLayers();
            EnsureAllLayers();
            return;
        }

        var existing = transform.Find("UIRoot");
        if (existing != null)
        {
            // 允许场景预先放一个 UIRoot，也允许完全运行时自举。
            // 这样生成出来的 Prefab 可以脱离旧框架直接接到这个轻量基座上。
            _uiRoot = existing.gameObject;
            _uiCamera = _uiRoot.GetComponentInChildren<Camera>(true);
            _canvas = _uiRoot.GetComponentInChildren<Canvas>(true);
            _eventSystem = _uiRoot.GetComponentInChildren<EventSystem>(true);
            CacheLayers();
            EnsureAllLayers();
            EnsureEventSystem();
            ApplyConfiguredLayer();
            return;
        }

        _uiRoot = new GameObject("UIRoot");
        _uiRoot.transform.SetParent(transform, false);

        CreateUICamera();
        CreateCanvas();
        EnsureAllLayers();
        EnsureEventSystem();
        ApplyConfiguredLayer();
    }

    private void CreateUICamera()
    {
        var go = new GameObject("UICamera");
        go.transform.SetParent(_uiRoot.transform, false);

        _uiCamera = go.AddComponent<Camera>();
        _uiCamera.clearFlags = _cfg != null ? _cfg.clearFlags : CameraClearFlags.Depth;
        _uiCamera.orthographic = _cfg == null || _cfg.orthographic;
        _uiCamera.orthographicSize = _cfg != null ? _cfg.orthoSize : 5f;
        _uiCamera.nearClipPlane = _cfg != null ? _cfg.near : -10f;
        _uiCamera.farClipPlane = _cfg != null ? _cfg.far : 10f;
        _uiCamera.depth = _cfg != null ? _cfg.cameraDepth : 100f;

        if (_cfg != null && !string.IsNullOrWhiteSpace(_cfg.uiLayerName))
        {
            var mask = LayerMask.GetMask(_cfg.uiLayerName);
            _uiCamera.cullingMask = mask == 0 ? ~0 : mask;
        }
        else
        {
            _uiCamera.cullingMask = ~0;
        }

        var uiData = _uiCamera.GetUniversalAdditionalCameraData();
        uiData.renderType = CameraRenderType.Base;
    }

    private void CreateCanvas()
    {
        var go = new GameObject("Canvas");
        go.transform.SetParent(_uiRoot.transform, false);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = _cfg != null ? _cfg.renderMode : RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _canvas.renderMode == RenderMode.ScreenSpaceCamera ? _uiCamera : null;
        _canvas.planeDistance = _cfg != null ? _cfg.planeDistance : 1f;
        _canvas.overrideSorting = true;
        _canvas.sortingLayerName = _cfg != null ? _cfg.sortingLayerName : "UI";
        _canvas.sortingOrder = _cfg != null ? _cfg.sortingOrder : 0;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = _cfg != null ? _cfg.scaleMode : CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = _cfg != null ? _cfg.referenceResolution : new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = _cfg != null ? _cfg.matchWidthOrHeight : 0.5f;
        scaler.referencePixelsPerUnit = _cfg != null ? _cfg.referencePixelsPerUnit : 100f;

        go.AddComponent<GraphicRaycaster>();

        FitFullScreen(go.GetComponent<RectTransform>());
    }

    private void EnsureEventSystem()
    {
        if (_eventSystem != null)
        {
            return;
        }

        if (_cfg != null && !_cfg.createEventSystem)
        {
            return;
        }

        _eventSystem = EventSystem.current;
        if (_eventSystem != null)
        {
            return;
        }

        var existing = FindObjectOfType<EventSystem>(true);
        if (existing != null)
        {
            _eventSystem = existing;
            return;
        }

        var go = new GameObject("EventSystem");
        go.transform.SetParent(_uiRoot.transform, false);
        _eventSystem = go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void EnsureAllLayers()
    {
        // 固定分层是为了让导出的面板与代码绑定保持简单，不再依赖旧框架的复杂窗口栈。
        EnsureLayer(UILayer.Background);
        EnsureLayer(UILayer.Normal);
        EnsureLayer(UILayer.Popup);
        EnsureLayer(UILayer.Tips);
        EnsureLayer(UILayer.Top);
    }

    private void EnsureLayer(UILayer layer)
    {
        if (_canvas == null)
        {
            return;
        }

        if (_layers.TryGetValue(layer, out var existing) && existing != null)
        {
            return;
        }

        var layerTransform = _canvas.transform.Find(layer.ToString()) as RectTransform;
        if (layerTransform == null)
        {
            var go = new GameObject(layer.ToString());
            go.transform.SetParent(_canvas.transform, false);
            layerTransform = go.AddComponent<RectTransform>();
            FitFullScreen(layerTransform);
        }

        _layers[layer] = layerTransform;
    }

    private void ApplyConfiguredLayer()
    {
        if (_uiRoot == null || _cfg == null || string.IsNullOrWhiteSpace(_cfg.uiLayerName))
        {
            return;
        }

        var uiLayer = LayerMask.NameToLayer(_cfg.uiLayerName);
        if (uiLayer < 0)
        {
            Debug.LogWarning($"[UISystem] UI layer not found: {_cfg.uiLayerName}");
            return;
        }

        SetLayerRecursively(_uiRoot.transform, uiLayer);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }

    private void CacheLayers()
    {
        _layers.Clear();
        if (_canvas == null)
        {
            return;
        }

        foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
        {
            var layerTransform = _canvas.transform.Find(layer.ToString()) as RectTransform;
            if (layerTransform != null)
            {
                _layers[layer] = layerTransform;
            }
        }
    }

    private static void FitFullScreen(RectTransform rt)
    {
        if (rt == null)
        {
            return;
        }

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero;
        rt.localScale = Vector3.one;
    }

    private void RemoveUICameraFromAllStacks()
    {
        if (_uiCamera == null)
        {
            return;
        }

        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
        for (var i = 0; i < cameras.Length; i++)
        {
            var camera = cameras[i];
            if (camera == null || camera == _uiCamera)
            {
                continue;
            }

            var data = camera.GetUniversalAdditionalCameraData();
            if (data.renderType != CameraRenderType.Base)
            {
                continue;
            }

            if (data.cameraStack.Contains(_uiCamera))
            {
                data.cameraStack.Remove(_uiCamera);
            }
        }
    }
}
