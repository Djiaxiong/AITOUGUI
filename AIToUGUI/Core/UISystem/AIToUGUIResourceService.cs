using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

internal static class AIToUGUIResourceService
{
    private const string ResourceConfigAssetPath = "Assets/Resources/Config/ResourceConfig.asset";

    public static GameObject InstantiatePrefab(string logicalPath)
    {
        var prefab = LoadPrefab(logicalPath);
        return prefab != null ? UnityEngine.Object.Instantiate(prefab) : null;
    }

    public static Awaiter<GameObject> InstantiatePrefabAsync(string logicalPath)
    {
        return MiniTask.FromCoroutine<GameObject>(setResult => InstantiatePrefabAsyncCoroutine(logicalPath, setResult));
    }

    public static string LoadText(string logicalPath)
    {
        var textAsset = LoadTextAsset(logicalPath);
        return textAsset != null ? textAsset.text : null;
    }

    public static Sprite LoadSprite(string logicalPath)
    {
        return LoadSpriteAsset(logicalPath);
    }

    public static Awaiter<string> LoadTextAsync(string logicalPath)
    {
        return MiniTask.FromCoroutine<string>(setResult => LoadTextAsyncCoroutine(logicalPath, setResult));
    }

    public static Awaiter<Sprite> LoadSpriteAsync(string logicalPath)
    {
        return MiniTask.FromCoroutine<Sprite>(setResult => LoadSpriteAsyncCoroutine(logicalPath, setResult));
    }

    public static string ToResourcesPath(string logicalPath)
    {
        var normalized = NormalizePath(logicalPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        const string resourcesSegment = "/Resources/";
        var resourcesIndex = normalized.IndexOf(resourcesSegment, StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
        {
            normalized = normalized.Substring(resourcesIndex + resourcesSegment.Length);
        }
        else if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Resources/".Length);
        }

        normalized = StripKnownExtension(normalized);
        return normalized.Trim('/');
    }

    private static IEnumerator InstantiatePrefabAsyncCoroutine(string logicalPath, Action<GameObject> setResult)
    {
        var resourcesPath = ToResourcesPath(logicalPath);
        if (!string.IsNullOrWhiteSpace(resourcesPath))
        {
            var request = Resources.LoadAsync<GameObject>(resourcesPath);
            yield return request;

            if (request.asset is GameObject prefab)
            {
                setResult?.Invoke(UnityEngine.Object.Instantiate(prefab));
                yield break;
            }
        }

        setResult?.Invoke(InstantiatePrefab(logicalPath));
    }

    private static IEnumerator LoadTextAsyncCoroutine(string logicalPath, Action<string> setResult)
    {
        var resourcesPath = ToResourcesPath(logicalPath);
        if (!string.IsNullOrWhiteSpace(resourcesPath))
        {
            var request = Resources.LoadAsync<TextAsset>(resourcesPath);
            yield return request;

            if (request.asset is TextAsset textAsset)
            {
                setResult?.Invoke(textAsset.text);
                yield break;
            }
        }

        setResult?.Invoke(LoadText(logicalPath));
    }

    private static IEnumerator LoadSpriteAsyncCoroutine(string logicalPath, Action<Sprite> setResult)
    {
        var resourcesPath = ToResourcesPath(logicalPath);
        if (!string.IsNullOrWhiteSpace(resourcesPath))
        {
            var request = Resources.LoadAsync<Sprite>(resourcesPath);
            yield return request;

            if (request.asset is Sprite sprite)
            {
                setResult?.Invoke(sprite);
                yield break;
            }
        }

        setResult?.Invoke(LoadSprite(logicalPath));
    }

    private static GameObject LoadPrefab(string logicalPath)
    {
        var resourcesPath = ToResourcesPath(logicalPath);
        var prefab = !string.IsNullOrWhiteSpace(resourcesPath)
            ? Resources.Load<GameObject>(resourcesPath)
            : null;

#if UNITY_EDITOR
        if (prefab == null)
        {
            prefab = LoadEditorAsset<GameObject>(logicalPath, ".prefab");
        }
#endif

        return prefab;
    }

    private static TextAsset LoadTextAsset(string logicalPath)
    {
        var resourcesPath = ToResourcesPath(logicalPath);
        var textAsset = !string.IsNullOrWhiteSpace(resourcesPath)
            ? Resources.Load<TextAsset>(resourcesPath)
            : null;

#if UNITY_EDITOR
        if (textAsset == null)
        {
            textAsset = LoadEditorAsset<TextAsset>(logicalPath, ".json", ".txt");
        }
#endif

        return textAsset;
    }

    private static Sprite LoadSpriteAsset(string logicalPath)
    {
        var resourcesPath = ToResourcesPath(logicalPath);
        var sprite = !string.IsNullOrWhiteSpace(resourcesPath)
            ? Resources.Load<Sprite>(resourcesPath)
            : null;

#if UNITY_EDITOR
        if (sprite == null)
        {
            sprite = LoadEditorAsset<Sprite>(logicalPath, ".png", ".jpg", ".jpeg", ".tga", ".psd", ".spriteatlasv2");
        }
#endif

        return sprite;
    }

    private static string NormalizePath(string logicalPath)
    {
        return string.IsNullOrWhiteSpace(logicalPath)
            ? string.Empty
            : logicalPath.Replace('\\', '/').Trim();
    }

    private static string StripKnownExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        string[] extensions = { ".prefab", ".json", ".txt", ".asset" };
        for (var i = 0; i < extensions.Length; i++)
        {
            if (normalized.EndsWith(extensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(0, normalized.Length - extensions[i].Length);
            }
        }

        return normalized;
    }

#if UNITY_EDITOR
    private static T LoadEditorAsset<T>(string logicalPath, params string[] extensions) where T : UnityEngine.Object
    {
        var normalized = NormalizePath(logicalPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (TryLoadFromResourceConfig<T>(normalized, out var configuredAsset))
        {
            return configuredAsset;
        }

        var candidates = BuildEditorCandidates(normalized, extensions);
        for (var i = 0; i < candidates.Count; i++)
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(candidates[i]);
            if (asset != null)
            {
                return asset;
            }
        }

        var fileName = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var searchType = typeof(T) == typeof(GameObject) ? "Prefab" : typeof(T).Name;
        var guids = AssetDatabase.FindAssets($"{fileName} t:{searchType}");
        for (var i = 0; i < guids.Length; i++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            if (!MatchesRequestedFile(assetPath, normalized, extensions))
            {
                continue;
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }
        }

        return null;
    }

    private static bool TryLoadFromResourceConfig<T>(string logicalPath, out T asset) where T : UnityEngine.Object
    {
        var config = AssetDatabase.LoadAssetAtPath<ResourceConfig>(ResourceConfigAssetPath);
        if (config != null && config.TryGetEditorAssetPath(logicalPath, out var assetPath))
        {
            asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return true;
            }
        }

        asset = null;
        return false;
    }

    private static List<string> BuildEditorCandidates(string normalized, string[] extensions)
    {
        var candidates = new List<string>();
        AddCandidate(candidates, normalized);

        var resourcesPath = ToResourcesPath(normalized);
        if (!string.IsNullOrWhiteSpace(resourcesPath))
        {
            AddCandidate(candidates, $"Assets/Resources/{resourcesPath}");
        }

        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, $"Assets/{normalized}");
        }

        if (extensions == null || extensions.Length == 0)
        {
            return candidates;
        }

        var snapshot = candidates.ToArray();
        for (var i = 0; i < snapshot.Length; i++)
        {
            for (var j = 0; j < extensions.Length; j++)
            {
                AddCandidate(candidates, snapshot[i] + extensions[j]);
            }
        }

        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = path.Replace('\\', '/');
        if (!candidates.Contains(normalized))
        {
            candidates.Add(normalized);
        }
    }

    private static bool MatchesRequestedFile(string assetPath, string normalized, string[] extensions)
    {
        var requested = Path.GetFileNameWithoutExtension(normalized);
        var candidate = Path.GetFileNameWithoutExtension(assetPath);
        if (!string.Equals(requested, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (extensions == null || extensions.Length == 0)
        {
            return true;
        }

        for (var i = 0; i < extensions.Length; i++)
        {
            if (assetPath.EndsWith(extensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
#endif
}
