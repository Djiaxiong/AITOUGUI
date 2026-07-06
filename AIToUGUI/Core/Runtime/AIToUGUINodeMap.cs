using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class AIToUGUINodeMap
{
    private readonly Transform _root;
    private readonly Dictionary<string, Transform> _nodes;
    private readonly Dictionary<string, RectTransform> _slots;
    private readonly Dictionary<string, RectTransform> _containers;
    private readonly Dictionary<string, RectTransform> _templates;

    private AIToUGUINodeMap(
        Transform root,
        Dictionary<string, Transform> nodes,
        Dictionary<string, RectTransform> slots,
        Dictionary<string, RectTransform> containers,
        Dictionary<string, RectTransform> templates)
    {
        _root = root;
        _nodes = nodes ?? new Dictionary<string, Transform>(StringComparer.Ordinal);
        _slots = slots ?? new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        _containers = containers ?? new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        _templates = templates ?? new Dictionary<string, RectTransform>(StringComparer.Ordinal);
    }

    public static AIToUGUINodeMap Capture(Transform root)
    {
        var nodes = new Dictionary<string, Transform>(64, StringComparer.Ordinal);
        var slots = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        var containers = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        var templates = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        if (root == null)
        {
            return new AIToUGUINodeMap(null, nodes, slots, containers, templates);
        }

        RegisterNodeAliases(nodes, root);
        RegisterSemanticIds(root, slots, containers, templates);

        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            for (var i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                if (ShouldSkipDynamicSubtree(root, child))
                {
                    continue;
                }

                RegisterNodeAliases(nodes, child);
                RegisterSemanticIds(child, slots, containers, templates);
                stack.Push(child);
            }
        }

        return new AIToUGUINodeMap(root, nodes, slots, containers, templates);
    }

    public Transform GetTransform(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return _root;
        }

        return _nodes.TryGetValue(nodeName, out var target) ? target : null;
    }

    public RectTransform GetSlotTransform(string slotId)
    {
        return TryGetSemanticTransform(_slots, slotId, out var target) ? target : null;
    }

    public RectTransform GetContainerTransform(string containerId)
    {
        return TryGetSemanticTransform(_containers, containerId, out var target) ? target : null;
    }

    public RectTransform GetTemplateTransform(string templateId)
    {
        return TryGetSemanticTransform(_templates, templateId, out var target) ? target : null;
    }

    public bool TryGetComponent<T>(string nodeName, out T component) where T : Component
    {
        component = Get<T>(nodeName);
        return component != null;
    }

    public T Get<T>(string nodeName) where T : Component
    {
        var target = GetTransform(nodeName);
        if (target == null)
        {
            return null;
        }

        var direct = target.GetComponent<T>();
        if (direct != null)
        {
            return direct;
        }

        var nested = target.GetComponentsInChildren<T>(true);
        return nested != null && nested.Length > 0 ? nested[0] : null;
    }

    private static bool TryGetSemanticTransform(
        Dictionary<string, RectTransform> map,
        string key,
        out RectTransform target)
    {
        target = null;
        if (map == null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return map.TryGetValue(key, out target) && target != null;
    }

    private static void RegisterSemanticIds(
        Transform target,
        Dictionary<string, RectTransform> slots,
        Dictionary<string, RectTransform> containers,
        Dictionary<string, RectTransform> templates)
    {
        if (target == null)
        {
            return;
        }

        var rect = target as RectTransform ?? target.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        var marker = target.GetComponent<AIToUGUI.AIToUGUIExportNodeMarker>();
        if (marker == null)
        {
            return;
        }

        RegisterSemanticId(slots, marker.slotId, rect);
        RegisterSemanticId(containers, marker.containerId, rect);
        RegisterSemanticId(templates, marker.templateId, rect);
    }

    private static void RegisterSemanticId(Dictionary<string, RectTransform> map, string key, RectTransform target)
    {
        if (map == null || target == null || string.IsNullOrWhiteSpace(key) || map.ContainsKey(key))
        {
            return;
        }

        map.Add(key, target);
    }

    private static void RegisterNodeAliases(Dictionary<string, Transform> map, Transform target)
    {
        if (map == null || target == null)
        {
            return;
        }

        TryRegisterNodeAlias(map, target.name, target);
        TryRegisterNodeAlias(map, ResolveLogicalNodeName(target), target);
    }

    private static void TryRegisterNodeAlias(Dictionary<string, Transform> map, string key, Transform target)
    {
        if (map == null || target == null || string.IsNullOrWhiteSpace(key) || map.ContainsKey(key))
        {
            return;
        }

        map.Add(key, target);
    }

    private static string ResolveLogicalNodeName(Transform target)
    {
        if (target == null)
        {
            return null;
        }

        if (target.TryGetComponent<AIToUGUI.AIToUGUIExportNodeMarker>(out var marker) &&
            !string.IsNullOrWhiteSpace(marker.nodeName))
        {
            return marker.nodeName;
        }

        if (target.TryGetComponent<AIToUGUI.AIToUGUIPageRoot>(out var pageRoot) &&
            pageRoot.BakeMetadata != null &&
            pageRoot.BakeMetadata.exportedNodes != null &&
            pageRoot.BakeMetadata.exportedNodes.Count > 0)
        {
            var exportedRoot = pageRoot.BakeMetadata.exportedNodes[0];
            if (exportedRoot != null && !string.IsNullOrWhiteSpace(exportedRoot.nodeName))
            {
                return exportedRoot.nodeName;
            }
        }

        return null;
    }

    private static bool ShouldSkipDynamicSubtree(Transform captureRoot, Transform candidate)
    {
        return candidate != null &&
               captureRoot != candidate &&
               candidate.GetComponent<AIToUGUI.AIToUGUIDynamicTemplateInstance>() != null;
    }
}
