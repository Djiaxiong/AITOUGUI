using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AIToUGUI;
using UnityEngine;

public interface IAIToUGUIRuntimeSystem
{
    BasePanel ShowPage(string runtimePageId, Action<BasePanel> cb = null);
    T ShowPage<T>(string runtimePageId, Action<T> cb = null) where T : BasePanel;
    Awaiter<BasePanel> ShowPageAsync(string runtimePageId, Action<BasePanel> cb = null);
    Awaiter<T> ShowPageAsync<T>(string runtimePageId, Action<T> cb = null) where T : BasePanel;
    void HidePage(string runtimePageId);
    bool TryGetPage(string runtimePageId, out BasePanel panel);
    bool TryGetPageEntry(string runtimePageId, out AIToUGUIRuntimePageEntry entry);
    bool TryGetMetadata(string runtimePageId, out AIToUGUIBakeMetadata metadata);
    void ClearLayer(UILayer layer);
}

public sealed class AIToUGUIRuntimeSystem : BaseMonoManager<AIToUGUIRuntimeSystem>, IAIToUGUIRuntimeSystem
{
    private AIToUGUIRuntimeRegistry _registry;
    private bool _registryLoadAttempted;
    private bool _missingRegistryLogged;
    private readonly Dictionary<string, BasePanel> _cachedPages = new Dictionary<string, BasePanel>(StringComparer.Ordinal);

    public override void Init()
    {
        _ = UISystem.Instance.Canvas;
        EnsureRegistryLoaded();
    }

    public BasePanel ShowPage(string runtimePageId, Action<BasePanel> cb = null)
    {
        return ShowPage<BasePanel>(runtimePageId, cb);
    }

    public T ShowPage<T>(string runtimePageId, Action<T> cb = null) where T : BasePanel
    {
        if (!TryGetEntry(runtimePageId, out var entry) || !ValidateRequestedType<T>(entry))
        {
            return null;
        }

        if (_cachedPages.TryGetValue(runtimePageId, out var existing) && existing != null)
        {
            if (existing is not T typedPanel)
            {
                Debug.LogError($"[AIToUGUIRuntimeSystem] Cached page '{runtimePageId}' is '{existing.GetType().Name}', but '{typeof(T).Name}' was requested.");
                return null;
            }

            MoveToLayer(existing.transform, entry.targetLayer);
            ApplyRuntimePageContext(existing, entry);
            typedPanel.InternalShow();
            cb?.Invoke(typedPanel);
            return typedPanel;
        }

        var panel = UISystem.Instance.CreatePanel<T>(entry.prefabLogicalPath, entry.targetLayer);
        if (panel == null)
        {
            Debug.LogError($"[AIToUGUIRuntimeSystem] Failed to create page '{runtimePageId}' from '{entry.prefabLogicalPath}'.");
            return null;
        }

        _cachedPages[runtimePageId] = panel;
        ApplyRuntimePageContext(panel, entry);
        panel.InternalShow();
        cb?.Invoke(panel);
        return panel;
    }

    public Awaiter<BasePanel> ShowPageAsync(string runtimePageId, Action<BasePanel> cb = null)
    {
        return ShowPageAsync<BasePanel>(runtimePageId, cb);
    }

    public Awaiter<T> ShowPageAsync<T>(string runtimePageId, Action<T> cb = null) where T : BasePanel
    {
        return MiniTask.FromCoroutine<T>(setResult => ShowPageAsyncCoroutine(runtimePageId, cb, setResult));
    }

    public void HidePage(string runtimePageId)
    {
        if (_cachedPages.TryGetValue(runtimePageId, out var panel) && panel != null)
        {
            panel.InternalHide();
        }
    }

    public bool TryGetPage(string runtimePageId, out BasePanel panel)
    {
        return _cachedPages.TryGetValue(runtimePageId, out panel) && panel != null;
    }

    public bool TryGetPageEntry(string runtimePageId, out AIToUGUIRuntimePageEntry entry)
    {
        return TryGetEntry(runtimePageId, out entry);
    }

    public bool TryGetMetadata(string runtimePageId, out AIToUGUIBakeMetadata metadata)
    {
        metadata = null;
        if (!TryGetEntry(runtimePageId, out var entry))
        {
            return false;
        }

        metadata = ResolveMetadata(entry);
        return metadata != null;
    }

    public void ClearLayer(UILayer layer)
    {
        if (_registry == null || _registry.pages == null)
        {
            return;
        }

        for (var i = 0; i < _registry.pages.Count; i++)
        {
            var entry = _registry.pages[i];
            if (entry == null || entry.targetLayer != layer)
            {
                continue;
            }

            if (_cachedPages.TryGetValue(entry.runtimePageId, out var panel) && panel != null)
            {
                panel.InternalHide();
            }
        }
    }

    private IEnumerator ShowPageAsyncCoroutine<T>(string runtimePageId, Action<T> cb, Action<T> setResult) where T : BasePanel
    {
        if (!TryGetEntry(runtimePageId, out var entry) || !ValidateRequestedType<T>(entry))
        {
            setResult(null);
            yield break;
        }

        if (_cachedPages.TryGetValue(runtimePageId, out var existing) && existing != null)
        {
            if (existing is not T typedPanel)
            {
                Debug.LogError($"[AIToUGUIRuntimeSystem] Cached page '{runtimePageId}' is '{existing.GetType().Name}', but '{typeof(T).Name}' was requested.");
                setResult(null);
                yield break;
            }

            MoveToLayer(existing.transform, entry.targetLayer);
            ApplyRuntimePageContext(existing, entry);
            typedPanel.InternalShow();
            cb?.Invoke(typedPanel);
            setResult(typedPanel);
            yield break;
        }

        var createTask = UISystem.Instance.CreatePanelAsync<T>(entry.prefabLogicalPath, entry.targetLayer);
        while (!createTask.IsCompleted)
        {
            yield return null;
        }

        var panel = createTask.GetResult();
        if (panel == null)
        {
            Debug.LogError($"[AIToUGUIRuntimeSystem] Failed to create page '{runtimePageId}' from '{entry.prefabLogicalPath}'.");
            setResult(null);
            yield break;
        }

        _cachedPages[runtimePageId] = panel;
        ApplyRuntimePageContext(panel, entry);
        panel.InternalShow();
        cb?.Invoke(panel);
        setResult(panel);
    }

    private bool TryGetEntry(string runtimePageId, out AIToUGUIRuntimePageEntry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(runtimePageId))
        {
            return false;
        }

        if (!EnsureRegistryLoaded() || _registry == null)
        {
            return false;
        }

        entry = _registry.FindPage(runtimePageId);
        if (entry == null)
        {
            Debug.LogError($"[AIToUGUIRuntimeSystem] Runtime page '{runtimePageId}' is not registered.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.prefabLogicalPath))
        {
            Debug.LogError($"[AIToUGUIRuntimeSystem] Runtime page '{runtimePageId}' has no prefab logical path.");
            return false;
        }

        return true;
    }

    private bool EnsureRegistryLoaded()
    {
        if (_registry != null)
        {
            return true;
        }

        if (!_registryLoadAttempted)
        {
            _registryLoadAttempted = true;
            _registry = Resources.Load<AIToUGUIRuntimeRegistry>("Config/AIToUGUIRuntimeRegistry");
        }

        if (_registry == null && !_missingRegistryLogged)
        {
            _missingRegistryLogged = true;
            Debug.LogWarning("[AIToUGUIRuntimeSystem] AIToUGUIRuntimeRegistry is missing at Resources/Config/AIToUGUIRuntimeRegistry.");
        }

        return _registry != null;
    }

    private bool ValidateRequestedType<T>(AIToUGUIRuntimePageEntry entry) where T : BasePanel
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.panelComponentTypeName))
        {
            return true;
        }

        var configuredType = FindPanelType(entry.panelComponentTypeName);
        if (configuredType == null)
        {
            Debug.LogError($"[AIToUGUIRuntimeSystem] Panel type '{entry.panelComponentTypeName}' is not available for page '{entry.runtimePageId}'.");
            return false;
        }

        var requestedType = typeof(T);
        return requestedType.IsAssignableFrom(configuredType) || configuredType.IsAssignableFrom(requestedType);
    }

    private void MoveToLayer(Transform panelTransform, UILayer layer)
    {
        if (panelTransform == null)
        {
            return;
        }

        var layerRoot = UISystem.Instance.GetLayerRoot(layer);
        if (layerRoot != null)
        {
            panelTransform.SetParent(layerRoot, false);
        }
    }

    private void ApplyRuntimePageContext(BasePanel panel, AIToUGUIRuntimePageEntry entry)
    {
        if (panel == null || entry == null)
        {
            return;
        }

        var pageRoot = panel.GetComponent<AIToUGUIPageRoot>();
        if (pageRoot == null)
        {
            return;
        }

        pageRoot.siteId = string.IsNullOrWhiteSpace(pageRoot.siteId) ? entry.siteId : pageRoot.siteId;
        pageRoot.pageId = string.IsNullOrWhiteSpace(pageRoot.pageId) ? entry.pageId : pageRoot.pageId;
        pageRoot.resourceLogicalPath = string.IsNullOrWhiteSpace(pageRoot.resourceLogicalPath) ? entry.prefabLogicalPath : pageRoot.resourceLogicalPath;
        pageRoot.targetLayer = entry.targetLayer;
        pageRoot.BindRuntimeMetadata(entry.runtimePageId, ResolveMetadata(entry));
    }

    private static AIToUGUIBakeMetadata ResolveMetadata(AIToUGUIRuntimePageEntry entry)
    {
        if (entry == null)
        {
            return null;
        }

        if (entry.metadataAsset != null)
        {
            return entry.metadataAsset;
        }

        return Resources.Load<AIToUGUIBakeMetadata>(ToResourcesPath(entry.metadataAssetPath));
    }

    private static string ToResourcesPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        const string resourcesSegment = "/Resources/";
        var normalized = assetPath.Replace("\\", "/");
        var index = normalized.IndexOf(resourcesSegment, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var relative = normalized.Substring(index + resourcesSegment.Length);
        if (relative.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            relative = relative.Substring(0, relative.Length - ".asset".Length);
        }

        return relative;
    }

    private static Type FindPanelType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = Array.FindAll(exception.Types, candidate => candidate != null);
            }

            for (var j = 0; j < types.Length; j++)
            {
                var type = types[j];
                if (string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, typeName, StringComparison.Ordinal))
                {
                    return type;
                }
            }
        }

        return null;
    }
}
