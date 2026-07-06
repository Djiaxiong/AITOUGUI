using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public interface IUGUIPrototypeSystem
{
    T ShowPanel<T>(string panelId, string jsonResourcePath, UILayer layer = UILayer.Normal, bool cache = true)
        where T : BasePanel;
    Awaiter<T> ShowPanelAsync<T>(string panelId, string jsonResourcePath, UILayer layer = UILayer.Normal, bool cache = true)
        where T : BasePanel;
    bool TryGetPanel(string panelId, out RectTransform panel);
    void HidePanel(string panelId);
    void ClearLayer(UILayer layer);
    RectTransform GetLayer(UILayer layer);
}

public sealed class UGUIPrototypeSystem : BaseMonoManager<UGUIPrototypeSystem>, IUGUIPrototypeSystem
{
    private readonly Dictionary<string, BasePanel> _panels = new Dictionary<string, BasePanel>(StringComparer.Ordinal);

    public override void Init()
    {
        _ = UISystem.Instance.Canvas;
    }

    public T ShowPanel<T>(string panelId, string jsonResourcePath, UILayer layer = UILayer.Normal, bool cache = true)
        where T : BasePanel
    {
        var layerRoot = GetLayer(layer);
        if (layerRoot == null)
        {
            Debug.LogError($"[UGUIPrototypeSystem] layer '{layer}' is missing.");
            return null;
        }

        if (TryReuseCachedPanel<T>(panelId, layerRoot, cache, out var cachedPanel))
        {
            return cachedPanel;
        }

        var jsonText = AIToUGUIResourceService.LoadText(jsonResourcePath);
#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            jsonText = TryLoadEditorJson(jsonResourcePath);
        }
#endif
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            Debug.LogError($"[UGUIPrototypeSystem] missing json asset: {jsonResourcePath}");
            return null;
        }

        return BuildPanelFromJson<T>(panelId, jsonText, layerRoot, cache, jsonResourcePath);
    }

    public Awaiter<T> ShowPanelAsync<T>(string panelId, string jsonResourcePath, UILayer layer = UILayer.Normal, bool cache = true)
        where T : BasePanel
    {
        return MiniTask.FromCoroutine<T>(setResult => ShowPanelAsyncCoroutine(panelId, jsonResourcePath, layer, cache, setResult));
    }

    public bool TryGetPanel(string panelId, out RectTransform panel)
    {
        panel = null;
        if (!_panels.TryGetValue(panelId, out var existing) || existing == null)
        {
            return false;
        }

        panel = existing.transform as RectTransform;
        return panel != null;
    }

    public void HidePanel(string panelId)
    {
        if (_panels.TryGetValue(panelId, out var panel) && panel != null)
        {
            panel.InternalHide();
        }
    }

    public void ClearLayer(UILayer layer)
    {
        var layerRoot = GetLayer(layer);
        if (layerRoot == null)
        {
            return;
        }

        for (var i = 0; i < layerRoot.childCount; i++)
        {
            var child = layerRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.TryGetComponent<BasePanel>(out var panel))
            {
                panel.InternalHide();
            }
        }
    }

    public RectTransform GetLayer(UILayer layer)
    {
        return UISystem.Instance.GetLayerRoot(layer);
    }

    private IEnumerator ShowPanelAsyncCoroutine<T>(string panelId, string jsonResourcePath, UILayer layer, bool cache, Action<T> setResult)
        where T : BasePanel
    {
        var layerRoot = GetLayer(layer);
        if (layerRoot == null)
        {
            Debug.LogError($"[UGUIPrototypeSystem] layer '{layer}' is missing.");
            setResult(null);
            yield break;
        }

        if (TryReuseCachedPanel<T>(panelId, layerRoot, cache, out var cachedPanel))
        {
            setResult(cachedPanel);
            yield break;
        }

        var textTask = AIToUGUIResourceService.LoadTextAsync(jsonResourcePath);
        while (!textTask.IsCompleted)
        {
            yield return null;
        }

        var jsonText = textTask.GetResult();
#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            jsonText = TryLoadEditorJson(jsonResourcePath);
        }
#endif
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            Debug.LogError($"[UGUIPrototypeSystem] missing json asset: {jsonResourcePath}");
            setResult(null);
            yield break;
        }

        setResult(BuildPanelFromJson<T>(panelId, jsonText, layerRoot, cache, jsonResourcePath));
    }

    private bool TryReuseCachedPanel<T>(string panelId, RectTransform layerRoot, bool cache, out T panel)
        where T : BasePanel
    {
        panel = null;
        if (!cache || !_panels.TryGetValue(panelId, out var existing) || existing == null)
        {
            return false;
        }

        panel = existing as T;
        if (panel == null)
        {
            Debug.LogError(
                $"[UGUIPrototypeSystem] panel '{panelId}' was cached as '{existing.GetType().Name}', but '{typeof(T).Name}' was requested.");
            return true;
        }

        panel.transform.SetParent(layerRoot, false);
        panel.InternalShow();
        return true;
    }

    private T BuildPanelFromJson<T>(string panelId, string jsonText, RectTransform layerRoot, bool cache, string jsonResourcePath)
        where T : BasePanel
    {
        var rootGo = UGUIPrototypeBuilder.BuildFromJson(jsonText, layerRoot, panelId);
        if (rootGo == null)
        {
            Debug.LogError($"[UGUIPrototypeSystem] failed to build panel '{panelId}' from '{jsonResourcePath}'.");
            return null;
        }

        var panel = rootGo.GetComponent<T>();
        if (panel == null)
        {
            panel = rootGo.AddComponent<T>();
        }

        panel.InternalInit();
        panel.InternalShow();

        if (cache)
        {
            _panels[panelId] = panel;
        }

        return panel;
    }

#if UNITY_EDITOR
    private static string TryLoadEditorJson(string jsonResourcePath)
    {
        if (string.IsNullOrWhiteSpace(jsonResourcePath))
        {
            return null;
        }

        var normalized = jsonResourcePath.Replace('\\', '/').Trim('/');
        var candidates = new[]
        {
            $"Assets/DataConfig/{normalized}.json",
            $"Assets/{normalized}.json"
        };

        for (var i = 0; i < candidates.Length; i++)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(candidates[i]);
            if (asset != null)
            {
                return asset.text;
            }
        }

        var assetName = System.IO.Path.GetFileNameWithoutExtension(normalized);
        var guids = AssetDatabase.FindAssets($"{assetName} t:TextAsset");
        for (var i = 0; i < guids.Length; i++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            if (!assetPath.EndsWith($"/{assetName}.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset != null)
            {
                return asset.text;
            }
        }

        return null;
    }
#endif
}
