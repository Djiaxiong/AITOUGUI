using System;
using System.Collections.Generic;
using UnityEngine;

public enum AssetLoadMode
{
    Resources = 0,
    EditorAsset = 1
}

[Serializable]
public sealed class ResourceConfigEntry
{
    public string logicalPath;
    public string editorAssetPath;
    public AssetLoadMode loadMode = AssetLoadMode.EditorAsset;
}

[CreateAssetMenu(fileName = "ResourceConfig", menuName = "AIToUGUI/Resource Config")]
public sealed class ResourceConfig : ScriptableObject
{
    [SerializeField] private List<ResourceConfigEntry> entries = new List<ResourceConfigEntry>();

    public IReadOnlyList<ResourceConfigEntry> Entries => entries;

    public bool TryGetEditorAssetPath(string logicalPath, out string editorAssetPath)
    {
        var normalized = AIToUGUIResourceService.ToResourcesPath(logicalPath);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            if (!string.Equals(entry.logicalPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            editorAssetPath = entry.editorAssetPath;
            return !string.IsNullOrWhiteSpace(editorAssetPath);
        }

        editorAssetPath = null;
        return false;
    }

    public bool UpsertAssetByEditorPath(string editorAssetPath, string logicalPath, AssetLoadMode loadMode)
    {
        if (string.IsNullOrWhiteSpace(editorAssetPath) || string.IsNullOrWhiteSpace(logicalPath))
        {
            return false;
        }

        var normalizedPath = AIToUGUIResourceService.ToResourcesPath(logicalPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            if (!string.Equals(entry.logicalPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.editorAssetPath = editorAssetPath.Replace('\\', '/');
            entry.loadMode = loadMode;
            return true;
        }

        entries.Add(new ResourceConfigEntry
        {
            logicalPath = normalizedPath,
            editorAssetPath = editorAssetPath.Replace('\\', '/'),
            loadMode = loadMode
        });
        return true;
    }
}
