using System;
using System.Collections.Generic;
using System.Reflection;
using AIToUGUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class UIAutoBinder
{
    private static readonly Dictionary<Transform, Dictionary<string, Transform>> s_indexCache = new(256);

    public static void WarmIndex(Transform root)
    {
        if (root == null)
        {
            return;
        }

        _ = GetOrBuildIndex(root);
    }

    public static void RebuildIndex(Transform root)
    {
        if (root == null)
        {
            return;
        }

        s_indexCache[root] = BuildIndex(root);
    }

    public static void BindFields(object target, Transform root)
    {
        if (target == null || root == null)
        {
            return;
        }

        WarmIndex(root);

        foreach (var field in EnumerateBindableFields(target.GetType()))
        {
            var attr = field.GetCustomAttribute<BindFieldAttribute>(true);
            if (attr == null || !typeof(Component).IsAssignableFrom(field.FieldType))
            {
                continue;
            }

            if (!TryResolveComponent(root, attr.BindName, field.FieldType, out var component))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[UIBind] Bind failed: {target.GetType().Name}.{field.Name} <- '{attr.BindName}' ({field.FieldType.Name}) under '{root.name}'");
#endif
                continue;
            }

            field.SetValue(target, component);
        }
    }

    public static bool TryResolveComponent(Transform root, string bindName, Type fieldType, out Component component)
    {
        component = null;
        if (root == null || fieldType == null || !typeof(Component).IsAssignableFrom(fieldType))
        {
            return false;
        }

        if (string.IsNullOrEmpty(bindName))
        {
            component = root.GetComponent(fieldType);
            return component != null;
        }

        var index = GetOrBuildIndex(root);
        if (!index.TryGetValue(bindName, out var targetTransform) || targetTransform == null)
        {
            index = BuildIndex(root);
            s_indexCache[root] = index;
            if (!index.TryGetValue(bindName, out targetTransform) || targetTransform == null)
            {
                return false;
            }
        }

        component = ResolveComponentOnTransform(targetTransform, fieldType);
        return component != null;
    }

    public static T ResolveComponent<T>(Transform root, string bindName) where T : Component
    {
        return TryResolveComponent(root, bindName, typeof(T), out var component) ? component as T : null;
    }

    public static IReadOnlyList<Type> GetBehaviourComponentTypes(EUIBehaviourType behaviourType)
    {
        return behaviourType switch
        {
            EUIBehaviourType.Button => s_buttonTypes,
            EUIBehaviourType.Toggle => s_toggleTypes,
            EUIBehaviourType.Slider => s_sliderTypes,
            EUIBehaviourType.Scrollbar => s_scrollbarTypes,
            EUIBehaviourType.Dropdown => s_dropdownTypes,
            EUIBehaviourType.InputField => s_inputFieldTypes,
            _ => Array.Empty<Type>()
        };
    }

    public static void ReleaseIndex(Transform root)
    {
        if (root == null)
        {
            return;
        }

        s_indexCache.Remove(root);
    }

    private static readonly Type[] s_buttonTypes = { typeof(UIButton), typeof(Button) };
    private static readonly Type[] s_toggleTypes = { typeof(UIToggle), typeof(Toggle) };
    private static readonly Type[] s_sliderTypes = { typeof(UISlider), typeof(Slider) };
    private static readonly Type[] s_scrollbarTypes = { typeof(UIScrollbar), typeof(Scrollbar) };
    private static readonly Type[] s_dropdownTypes = { typeof(UIDropdown), typeof(TMP_Dropdown) };
    private static readonly Type[] s_inputFieldTypes = { typeof(UIInputField), typeof(TMP_InputField) };

    private static Component ResolveComponentOnTransform(Transform targetTransform, Type fieldType)
    {
        if (targetTransform == null || fieldType == null)
        {
            return null;
        }

        var directComponent = targetTransform.GetComponent(fieldType);
        if (directComponent != null)
        {
            return directComponent;
        }

        var nestedComponents = targetTransform.GetComponentsInChildren(fieldType, true);
        return nestedComponents != null && nestedComponents.Length > 0 ? nestedComponents[0] : null;
    }

    private static Dictionary<string, Transform> GetOrBuildIndex(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        if (s_indexCache.TryGetValue(root, out var index) && index != null)
        {
            return index;
        }

        index = BuildIndex(root);
        s_indexCache[root] = index;
        return index;
    }

    private static Dictionary<string, Transform> BuildIndex(Transform root)
    {
        var dict = new Dictionary<string, Transform>(64, StringComparer.Ordinal);
        if (root == null)
        {
            return dict;
        }

        if (!dict.ContainsKey(root.name))
        {
            dict.Add(root.name, root);
        }

        TryRegisterAlias(dict, ResolveLogicalNodeName(root), root);

        var stack = new Stack<Transform>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var transform = stack.Pop();
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child != root && child.GetComponent<AIToUGUIDynamicTemplateInstance>() != null)
                {
                    continue;
                }

                TryRegisterAlias(dict, child.name, child);
                TryRegisterAlias(dict, ResolveLogicalNodeName(child), child);
                stack.Push(child);
            }
        }

        return dict;
    }

    private static IEnumerable<FieldInfo> EnumerateBindableFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        while (type != null && type != typeof(MonoBehaviour) && type != typeof(object))
        {
            var fields = type.GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
            {
                yield return fields[i];
            }

            type = type.BaseType;
        }
    }

    private static string ResolveLogicalNodeName(Transform target)
    {
        if (target == null)
        {
            return null;
        }

        if (target.TryGetComponent<AIToUGUIExportNodeMarker>(out var marker) &&
            !string.IsNullOrWhiteSpace(marker.nodeName))
        {
            return marker.nodeName;
        }

        if (target.TryGetComponent<AIToUGUIPageRoot>(out var pageRoot) &&
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

    private static void TryRegisterAlias(Dictionary<string, Transform> index, string key, Transform target)
    {
        if (index == null || target == null || string.IsNullOrWhiteSpace(key) || index.ContainsKey(key))
        {
            return;
        }

        index.Add(key, target);
    }
}
