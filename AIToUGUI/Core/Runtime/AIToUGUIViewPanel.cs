using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class AIToUGUIViewPanel : BasePanel
{
    private AIToUGUINodeMap _nodeMap;
    private AIToUGUI.AIToUGUIPageRoot _pageRoot;

    protected AIToUGUINodeMap NodeMap => _nodeMap ??= AIToUGUINodeMap.Capture(transform);
    protected AIToUGUI.AIToUGUIPageRoot PageRoot => _pageRoot != null ? _pageRoot : (_pageRoot = GetComponent<AIToUGUI.AIToUGUIPageRoot>());
    protected AIToUGUI.AIToUGUIBakeMetadata BakeMetadata => PageRoot != null ? PageRoot.BakeMetadata : null;

    protected sealed override void OnInit()
    {
        RebindViewNow();
        OnViewInit();
    }

    protected sealed override void OnBindingsRefreshed()
    {
        RebindViewNow();
        OnViewBindingsRefreshed();
    }

    protected virtual void OnViewInit()
    {
    }

    protected virtual void OnViewBindingsRefreshed()
    {
    }

    protected virtual void BindViewComponents(AIToUGUINodeMap nodeMap)
    {
    }

    protected RectTransform GetSlotRoot(string slotId)
    {
        return NodeMap.GetSlotTransform(slotId);
    }

    protected RectTransform GetContainerRoot(string containerId)
    {
        return NodeMap.GetContainerTransform(containerId);
    }

    protected RectTransform GetTemplateRoot(string templateId)
    {
        return NodeMap.GetTemplateTransform(templateId);
    }

    protected AIToUGUINodeMap CaptureItemNodeMap(Transform root)
    {
        return AIToUGUINodeMap.Capture(root);
    }

    protected AIToUGUI.AIToUGUIBakeExportedNodeInfo GetExportedNodeInfo(string nodeName)
    {
        return PageRoot != null ? PageRoot.FindExportedNode(nodeName) : null;
    }

    protected AIToUGUI.AIToUGUIBakeExportedNodeInfo GetSlotInfo(string slotId)
    {
        return PageRoot != null ? PageRoot.FindNodeBySlotId(slotId) : null;
    }

    protected AIToUGUI.AIToUGUIBakeExportedNodeInfo GetContainerInfo(string containerId)
    {
        return PageRoot != null ? PageRoot.FindNodeByContainerId(containerId) : null;
    }

    protected AIToUGUI.AIToUGUIBakeExportedNodeInfo GetTemplateInfo(string templateId)
    {
        return PageRoot != null ? PageRoot.FindNodeByTemplateId(templateId) : null;
    }

    protected RectTransform CreateTemplateItem(string templateId, RectTransform parent = null, bool activate = true)
    {
        var templateRoot = GetTemplateRoot(templateId);
        if (templateRoot == null)
        {
            return null;
        }

        var targetParent = parent != null ? parent : templateRoot.parent as RectTransform;
        if (targetParent == null)
        {
            return null;
        }

        var clone = Instantiate(templateRoot.gameObject, targetParent, false);
        clone.name = templateRoot.name;
        clone.SetActive(activate);

        var dynamicInstance = clone.GetComponent<AIToUGUI.AIToUGUIDynamicTemplateInstance>() ??
                              clone.AddComponent<AIToUGUI.AIToUGUIDynamicTemplateInstance>();
        dynamicInstance.templateId = templateId ?? string.Empty;

        if (targetParent.TryGetComponent<AIToUGUI.AIToUGUIExportNodeMarker>(out var parentMarker))
        {
            dynamicInstance.slotId = parentMarker.slotId ?? string.Empty;
            dynamicInstance.containerId = parentMarker.containerId ?? string.Empty;
        }

        return clone.GetComponent<RectTransform>();
    }

    protected RectTransform CreateTemplateItemInSlot(string slotId, string templateId, bool activate = true)
    {
        return CreateTemplateItem(templateId, GetSlotRoot(slotId), activate);
    }

    protected RectTransform CreateTemplateItemInContainer(string containerId, string templateId, bool activate = true)
    {
        return CreateTemplateItem(templateId, GetContainerRoot(containerId), activate);
    }

    protected void ClearSlot(string slotId, bool keepTemplateRoots = true)
    {
        ClearDynamicChildren(GetSlotRoot(slotId), keepTemplateRoots);
    }

    protected void ClearContainer(string containerId, bool keepTemplateRoots = true)
    {
        ClearDynamicChildren(GetContainerRoot(containerId), keepTemplateRoots);
    }

    protected List<RectTransform> RebuildSlot<T>(
        string slotId,
        string templateId,
        IReadOnlyList<T> items,
        Action<RectTransform, T, int> bindItem,
        bool keepTemplateRoots = true)
    {
        return RebuildDynamicCollection(GetSlotRoot(slotId), templateId, items, bindItem, keepTemplateRoots);
    }

    protected List<RectTransform> RebuildContainer<T>(
        string containerId,
        string templateId,
        IReadOnlyList<T> items,
        Action<RectTransform, T, int> bindItem,
        bool keepTemplateRoots = true)
    {
        return RebuildDynamicCollection(GetContainerRoot(containerId), templateId, items, bindItem, keepTemplateRoots);
    }

    [ContextMenu("Rebind Generated View")]
    public void RebindViewNow()
    {
        _pageRoot = GetComponent<AIToUGUI.AIToUGUIPageRoot>();
        _nodeMap = AIToUGUINodeMap.Capture(transform);
        BindViewComponents(_nodeMap);
    }

    private List<RectTransform> RebuildDynamicCollection<T>(
        RectTransform parent,
        string templateId,
        IReadOnlyList<T> items,
        Action<RectTransform, T, int> bindItem,
        bool keepTemplateRoots)
    {
        var created = new List<RectTransform>();
        if (parent == null)
        {
            return created;
        }

        ClearDynamicChildren(parent, keepTemplateRoots);
        if (items == null)
        {
            return created;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var instance = CreateTemplateItem(templateId, parent, true);
            if (instance == null)
            {
                continue;
            }

            bindItem?.Invoke(instance, items[i], i);
            created.Add(instance);
        }

        return created;
    }

    private static void ClearDynamicChildren(RectTransform parent, bool keepTemplateRoots)
    {
        if (parent == null)
        {
            return;
        }

        var toDestroy = new List<GameObject>();
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.GetComponent<AIToUGUI.AIToUGUIDynamicTemplateInstance>() != null)
            {
                toDestroy.Add(child.gameObject);
                continue;
            }

            if (!keepTemplateRoots &&
                child.TryGetComponent<AIToUGUI.AIToUGUIExportNodeMarker>(out var marker) &&
                !string.IsNullOrWhiteSpace(marker.templateId))
            {
                toDestroy.Add(child.gameObject);
            }
        }

        for (var i = 0; i < toDestroy.Count; i++)
        {
            if (Application.isPlaying)
            {
                Destroy(toDestroy[i]);
            }
            else
            {
                DestroyImmediate(toDestroy[i]);
            }
        }
    }
}
