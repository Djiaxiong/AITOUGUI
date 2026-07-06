#if UNITY_EDITOR

using System;

namespace AIToUGUI.Editor
{
    internal static class AIToUGUIRuntimePageIdUtility
    {
        public static string BuildDefaultRuntimePageId(string siteId, string pageId)
        {
            var normalizedSiteId = Normalize(siteId);
            var normalizedPageId = Normalize(pageId);
            if (string.IsNullOrWhiteSpace(normalizedPageId))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(normalizedSiteId)
                ? normalizedPageId
                : $"{normalizedSiteId}/{normalizedPageId}";
        }

        public static bool IsPlaceholderRuntimePageId(string runtimePageId)
        {
            var normalized = Normalize(runtimePageId);
            return string.IsNullOrWhiteSpace(normalized) ||
                   string.Equals(normalized, "page_id", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLegacyDefaultRuntimePageId(string runtimePageId, string pageId)
        {
            var normalizedRuntimePageId = Normalize(runtimePageId);
            var normalizedPageId = Normalize(pageId);
            return !string.IsNullOrWhiteSpace(normalizedPageId) &&
                   string.Equals(normalizedRuntimePageId, normalizedPageId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldUpgradeToNamespacedDefault(string runtimePageId, string siteId, string pageId)
        {
            if (IsPlaceholderRuntimePageId(runtimePageId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(Normalize(siteId)))
            {
                return false;
            }

            return IsLegacyDefaultRuntimePageId(runtimePageId, pageId);
        }

        public static string ResolveDefaultCompatibleRuntimePageId(string runtimePageId, string siteId, string pageId)
        {
            return ShouldUpgradeToNamespacedDefault(runtimePageId, siteId, pageId)
                ? BuildDefaultRuntimePageId(siteId, pageId)
                : Normalize(runtimePageId);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

#endif
