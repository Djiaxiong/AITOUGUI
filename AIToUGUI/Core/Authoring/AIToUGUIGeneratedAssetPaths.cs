using System;
using System.IO;

namespace AIToUGUI
{
    public static class AIToUGUIGeneratedAssetPaths
    {
        public const string GeneratedRoot = "Assets/AIToUGUI_Generated";
        public const string LegacySiteAssetRoot = "Assets/DataConfig/UI/AIToUGUI";
        public const string LegacyBundleAssetRoot = "Assets/DataConfig/UI/AIToUGUICompiled";
        public const string LegacyPrefabOutputRoot = "Assets/Prefabs/UI/Generated";
        public const string LegacyMetadataOutputRoot = "Assets/DataConfig/UI/Generated";
        public const string LegacyGeneratedPanelRoot = "Assets/AI_UGUI_Creator/AIToUGUI/GeneratedPanels";

        public static string GetPackageRoot(string siteId)
        {
            return $"{GeneratedRoot}/{SanitizePathSegment(siteId)}";
        }

        public static string GetConfigRoot(string siteId)
        {
            return $"{GetPackageRoot(siteId)}/Config";
        }

        public static string GetPagesRoot(string siteId)
        {
            return $"{GetConfigRoot(siteId)}/Pages";
        }

        public static string GetMetadataRoot(string siteId)
        {
            return $"{GetConfigRoot(siteId)}/Metadata";
        }

        public static string GetPrefabsRoot(string siteId)
        {
            return $"{GetPackageRoot(siteId)}/Prefabs";
        }

        public static string GetSpritesRoot(string siteId)
        {
            return $"{GetPackageRoot(siteId)}/Sprites";
        }

        public static string GetScriptsRoot(string siteId)
        {
            return $"{GetPackageRoot(siteId)}/Scripts";
        }

        public static string GetSiteAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetConfigRoot(siteId)}/{sanitizedSiteId}_Site.asset";
        }

        public static string GetBundleAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetConfigRoot(siteId)}/{sanitizedSiteId}_CompiledBundle.asset";
        }

        public static string GetThemeAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetConfigRoot(siteId)}/{sanitizedSiteId}_Theme.asset";
        }

        public static string GetElementLibraryAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetConfigRoot(siteId)}/{sanitizedSiteId}_ElementLibrary.asset";
        }

        public static string GetPageAssetPath(string siteId, string pageId)
        {
            return $"{GetPagesRoot(siteId)}/{SanitizePathSegment(pageId)}_Page.asset";
        }

        public static string GetGeneratedPanelScriptPath(string siteId, string panelTypeName)
        {
            return $"{GetScriptsRoot(siteId)}/{SanitizePathSegment(panelTypeName)}.cs";
        }

        public static string GetLegacySiteFolder(string siteId)
        {
            return $"{LegacySiteAssetRoot}/{SanitizePathSegment(siteId)}";
        }

        public static string GetLegacyBundleFolder(string siteId)
        {
            return $"{LegacyBundleAssetRoot}/{SanitizePathSegment(siteId)}";
        }

        public static string GetLegacySiteAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetLegacySiteFolder(siteId)}/{sanitizedSiteId}_Site.asset";
        }

        public static string GetLegacyBundleAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetLegacyBundleFolder(siteId)}/{sanitizedSiteId}_CompiledBundle.asset";
        }

        public static string GetLegacyThemeAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetLegacySiteFolder(siteId)}/{sanitizedSiteId}_Theme.asset";
        }

        public static string GetLegacyElementLibraryAssetPath(string siteId)
        {
            var sanitizedSiteId = SanitizePathSegment(siteId);
            return $"{GetLegacySiteFolder(siteId)}/{sanitizedSiteId}_ElementLibrary.asset";
        }

        public static string GetLegacyPageAssetPath(string siteId, string pageId)
        {
            return $"{GetLegacySiteFolder(siteId)}/Pages/{SanitizePathSegment(pageId)}_Page.asset";
        }

        public static string GetLegacyPrefabFolder(string siteId)
        {
            return $"{LegacyPrefabOutputRoot}/{SanitizePathSegment(siteId)}";
        }

        public static string GetLegacyMetadataFolder(string siteId)
        {
            return $"{LegacyMetadataOutputRoot}/{SanitizePathSegment(siteId)}";
        }

        public static string GetLegacyGeneratedPanelFolder(string siteId)
        {
            return $"{LegacyGeneratedPanelRoot}/{SanitizePathSegment(siteId)}";
        }

        public static string ResolvePrefabOutputRoot(string siteId, string requestedRoot)
        {
            var normalized = NormalizeAssetFolder(requestedRoot);
            return string.IsNullOrWhiteSpace(normalized) || IsLegacyPrefabOutputRoot(normalized)
                ? GetPrefabsRoot(siteId)
                : normalized;
        }

        public static string ResolveMetadataOutputRoot(string siteId, string requestedRoot)
        {
            var normalized = NormalizeAssetFolder(requestedRoot);
            return string.IsNullOrWhiteSpace(normalized) || IsLegacyMetadataOutputRoot(normalized)
                ? GetMetadataRoot(siteId)
                : normalized;
        }

        public static bool IsLegacyPrefabOutputRoot(string folder)
        {
            return PathsEqual(folder, LegacyPrefabOutputRoot);
        }

        public static bool IsLegacyMetadataOutputRoot(string folder)
        {
            return PathsEqual(folder, LegacyMetadataOutputRoot);
        }

        public static bool IsManagedPrefabOutputRoot(string siteId, string folder)
        {
            return PathsEqual(folder, GetPrefabsRoot(siteId));
        }

        public static bool IsManagedMetadataOutputRoot(string siteId, string folder)
        {
            return PathsEqual(folder, GetMetadataRoot(siteId));
        }

        public static bool IsPackageFolder(string siteId, string folder)
        {
            var normalizedFolder = NormalizeAssetFolder(folder);
            var normalizedPackageRoot = NormalizeAssetFolder(GetPackageRoot(siteId));
            return !string.IsNullOrWhiteSpace(normalizedFolder) &&
                   !string.IsNullOrWhiteSpace(normalizedPackageRoot) &&
                   (string.Equals(normalizedFolder, normalizedPackageRoot, StringComparison.OrdinalIgnoreCase) ||
                    normalizedFolder.StartsWith(normalizedPackageRoot + "/", StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return string.Empty;
            }

            var normalized = folder.Replace("\\", "/").TrimEnd('/');
            return normalized.StartsWith("Assets", StringComparison.Ordinal)
                ? normalized
                : string.Empty;
        }

        public static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Generated";
            }

            var sanitized = value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(sanitized) ? "Generated" : sanitized;
        }

        private static bool PathsEqual(string left, string right)
        {
            var normalizedLeft = NormalizeAssetFolder(left);
            var normalizedRight = NormalizeAssetFolder(right);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
    }
}
