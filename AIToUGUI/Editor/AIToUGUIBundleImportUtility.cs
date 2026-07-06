#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Editor
{
    internal static class AIToUGUIBundleImportUtility
    {
        public static AIToUGUICompiledBundleDefinition ImportBundle(TextAsset bundleJsonAsset)
        {
            if (!TryParseBundle(bundleJsonAsset, out var bundle))
            {
                Debug.LogError($"[AIToUGUI] Failed to parse compiled bundle JSON: {AssetDatabase.GetAssetPath(bundleJsonAsset)}");
                return null;
            }

            var siteFolder = AIToUGUIGeneratedAssetPaths.GetConfigRoot(bundle.site.siteId);
            EnsureFolder(siteFolder);

            var assetPath = AIToUGUIGeneratedAssetPaths.GetBundleAssetPath(bundle.site.siteId);
            var definition = LoadOrCreateAsset<AIToUGUICompiledBundleDefinition>(assetPath, AIToUGUIGeneratedAssetPaths.GetLegacyBundleAssetPath(bundle.site.siteId));
            ApplyBundle(definition, bundleJsonAsset, bundle);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = definition;
            return definition;
        }

        public static bool RefreshBundle(AIToUGUICompiledBundleDefinition definition, bool saveAssets = true)
        {
            if (!TryResolveBundle(definition, out var bundleJsonAsset, out var bundle))
            {
                return false;
            }

            ApplyBundle(definition, bundleJsonAsset, bundle);
            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return true;
        }

        private static bool TryParseBundle(TextAsset bundleJsonAsset, out CompiledSiteBundle bundle)
        {
            bundle = null;
            if (bundleJsonAsset == null || string.IsNullOrWhiteSpace(bundleJsonAsset.text))
            {
                return false;
            }

            try
            {
                bundle = JsonConvert.DeserializeObject<CompiledSiteBundle>(bundleJsonAsset.text);
                return bundle != null && bundle.site != null && !string.IsNullOrWhiteSpace(bundle.site.siteId);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AIToUGUI] Compiled bundle parse exception: {exception}");
                return false;
            }
        }

        private static bool TryResolveBundle(
            AIToUGUICompiledBundleDefinition definition,
            out TextAsset bundleJsonAsset,
            out CompiledSiteBundle bundle)
        {
            bundleJsonAsset = definition != null ? definition.bundleJson : null;
            if (TryParseBundle(bundleJsonAsset, out bundle))
            {
                return true;
            }

            bundleJsonAsset = null;
            bundle = null;
            if (definition == null)
            {
                return false;
            }

            var explicitPath = AIToUGUIGeneratedAssetPaths.NormalizeAssetFolder(definition.bundleJsonAssetPath);
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                var explicitAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(explicitPath);
                if (TryParseBundle(explicitAsset, out bundle))
                {
                    bundleJsonAsset = explicitAsset;
                    return true;
                }
            }

            return TryFindBundleJsonAsset(definition, out bundleJsonAsset, out bundle);
        }

        private static bool TryFindBundleJsonAsset(
            AIToUGUICompiledBundleDefinition definition,
            out TextAsset bundleJsonAsset,
            out CompiledSiteBundle bundle)
        {
            bundleJsonAsset = null;
            bundle = null;

            var siteIdCandidates = BuildSiteIdCandidates(definition);
            var pageIds = BuildPageIdSet(definition);
            var assetGuids = AssetDatabase.FindAssets("compiled_site_bundle");
            var bestScore = int.MinValue;

            for (var i = 0; i < assetGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                if (string.IsNullOrWhiteSpace(path) ||
                    !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidateAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (!TryParseBundle(candidateAsset, out var candidateBundle))
                {
                    continue;
                }

                var score = ScoreBundleCandidate(definition, candidateBundle, siteIdCandidates, pageIds);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bundleJsonAsset = candidateAsset;
                bundle = candidateBundle;
            }

            return bundleJsonAsset != null && bundle != null && bestScore > 0;
        }

        private static HashSet<string> BuildSiteIdCandidates(AIToUGUICompiledBundleDefinition definition)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (definition == null)
            {
                return candidates;
            }

            AddSiteIdCandidate(candidates, definition.siteId);
            AddSiteIdCandidate(candidates, ExtractSiteIdFromBundleAssetName(definition.name));
            AddSiteIdCandidate(candidates, ExtractSiteIdFromOutputRoot(definition.prefabOutputRoot));
            AddSiteIdCandidate(candidates, ExtractSiteIdFromOutputRoot(definition.metadataOutputRoot));

            if (definition.pages == null)
            {
                return candidates;
            }

            for (var i = 0; i < definition.pages.Count; i++)
            {
                var page = definition.pages[i];
                if (page == null)
                {
                    continue;
                }

                AddSiteIdCandidate(candidates, ExtractSiteIdFromRuntimePageId(page.runtimePageId));
                AddSiteIdCandidate(candidates, ExtractSiteIdFromLogicalPath(page.logicalPath));
            }

            return candidates;
        }

        private static HashSet<string> BuildPageIdSet(AIToUGUICompiledBundleDefinition definition)
        {
            var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (definition?.pages == null)
            {
                return pageIds;
            }

            for (var i = 0; i < definition.pages.Count; i++)
            {
                var page = definition.pages[i];
                if (page != null && !string.IsNullOrWhiteSpace(page.pageId))
                {
                    pageIds.Add(page.pageId.Trim());
                }
            }

            return pageIds;
        }

        private static int ScoreBundleCandidate(
            AIToUGUICompiledBundleDefinition definition,
            CompiledSiteBundle bundle,
            HashSet<string> siteIdCandidates,
            HashSet<string> pageIds)
        {
            if (bundle?.site == null)
            {
                return int.MinValue;
            }

            var score = 0;
            if (siteIdCandidates.Contains(bundle.site.siteId))
            {
                score += 100;
            }

            if (!IsPlaceholderDisplayName(definition?.displayName) &&
                string.Equals(definition.displayName.Trim(), bundle.site.displayName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }

            if (pageIds.Count > 0 && bundle.pages != null)
            {
                var matchedPages = 0;
                for (var i = 0; i < bundle.pages.Length; i++)
                {
                    var page = bundle.pages[i];
                    if (page != null && !string.IsNullOrWhiteSpace(page.pageId) && pageIds.Contains(page.pageId))
                    {
                        matchedPages++;
                    }
                }

                score += matchedPages * 10;
                if (matchedPages == pageIds.Count && bundle.pages.Length == pageIds.Count)
                {
                    score += 20;
                }
            }

            if (score == 0 &&
                siteIdCandidates.Count == 0 &&
                pageIds.Count == 0 &&
                !string.IsNullOrWhiteSpace(bundle.site.siteId))
            {
                score = 1;
            }

            return score;
        }

        private static void AddSiteIdCandidate(HashSet<string> candidates, string value)
        {
            if (candidates == null || IsPlaceholderSiteId(value))
            {
                return;
            }

            candidates.Add(value.Trim());
        }

        private static string ExtractSiteIdFromBundleAssetName(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
            {
                return string.Empty;
            }

            const string suffix = "_CompiledBundle";
            var trimmed = assetName.Trim();
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
            }

            return trimmed;
        }

        private static string ExtractSiteIdFromRuntimePageId(string runtimePageId)
        {
            if (string.IsNullOrWhiteSpace(runtimePageId))
            {
                return string.Empty;
            }

            var slashIndex = runtimePageId.IndexOf('/');
            return slashIndex > 0 ? runtimePageId.Substring(0, slashIndex).Trim() : string.Empty;
        }

        private static string ExtractSiteIdFromLogicalPath(string logicalPath)
        {
            if (string.IsNullOrWhiteSpace(logicalPath))
            {
                return string.Empty;
            }

            var segments = logicalPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (string.Equals(segments[i], "Generated", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i + 1].Trim();
                }
            }

            return string.Empty;
        }

        private static string ExtractSiteIdFromOutputRoot(string folder)
        {
            var normalized = AIToUGUIGeneratedAssetPaths.NormalizeAssetFolder(folder);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (string.Equals(segments[i], "AIToUGUI_Generated", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i + 1].Trim();
                }
            }

            return string.Empty;
        }

        private static bool IsPlaceholderSiteId(string siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId))
            {
                return true;
            }

            var normalized = siteId.Trim();
            return string.Equals(normalized, "site_id", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "site", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "generated", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlaceholderDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return true;
            }

            var normalized = displayName.Trim();
            return string.Equals(normalized, "Compiled Bundle", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyBundle(AIToUGUICompiledBundleDefinition definition, TextAsset bundleJsonAsset, CompiledSiteBundle bundle)
        {
            definition.bundleJson = bundleJsonAsset;
            definition.bundleJsonAssetPath = bundleJsonAsset != null ? AssetDatabase.GetAssetPath(bundleJsonAsset) : string.Empty;
            definition.schemaVersion = string.IsNullOrWhiteSpace(bundle.schemaVersion) ? "1.1" : bundle.schemaVersion;
            definition.siteId = bundle.site.siteId;
            definition.displayName = string.IsNullOrWhiteSpace(bundle.site.displayName) ? bundle.site.siteId : bundle.site.displayName;
            definition.designResolution = new Vector2(Mathf.Max(1, bundle.site.designWidth), Mathf.Max(1, bundle.site.designHeight));
            definition.prefabOutputRoot = AIToUGUIGeneratedAssetPaths.ResolvePrefabOutputRoot(bundle.site.siteId, bundle.site.prefabOutputRoot);
            definition.metadataOutputRoot = AIToUGUIGeneratedAssetPaths.ResolveMetadataOutputRoot(bundle.site.siteId, bundle.site.metadataOutputRoot);

            var existingPages = new Dictionary<string, AIToUGUICompiledBundlePageSummary>();
            if (definition.pages != null)
            {
                for (var i = 0; i < definition.pages.Count; i++)
                {
                    var page = definition.pages[i];
                    if (page != null && !string.IsNullOrWhiteSpace(page.pageId))
                    {
                        existingPages[page.pageId] = page;
                    }
                }
            }

            var mergedPages = new List<AIToUGUICompiledBundlePageSummary>();
            if (bundle.pages != null)
            {
                for (var i = 0; i < bundle.pages.Length; i++)
                {
                    var page = bundle.pages[i];
                    if (page == null || string.IsNullOrWhiteSpace(page.pageId))
                    {
                        continue;
                    }

                    existingPages.TryGetValue(page.pageId, out var existing);
                    mergedPages.Add(CreateOrMergePageSummary(bundle, page, existing));
                }
            }

            definition.pages = mergedPages;
            EditorUtility.SetDirty(definition);
        }

        private static AIToUGUICompiledBundlePageSummary CreateOrMergePageSummary(
            CompiledSiteBundle bundle,
            CompiledPageDto page,
            AIToUGUICompiledBundlePageSummary existing)
        {
            var summary = existing ?? new AIToUGUICompiledBundlePageSummary();
            var defaultRuntimePageId = AIToUGUIRuntimePageIdUtility.ResolveDefaultCompatibleRuntimePageId(
                page.runtimePageId,
                bundle.site != null ? bundle.site.siteId : string.Empty,
                page.pageId);
            summary.pageId = page.pageId;
            summary.displayName = string.IsNullOrWhiteSpace(page.displayName) ? page.pageId : page.displayName;
            summary.prefabName = string.IsNullOrWhiteSpace(page.prefabName) ? "CompiledPage" : page.prefabName;
            summary.targetLayer = ParseLayer(page.targetLayer);
            summary.logicalPath = string.IsNullOrWhiteSpace(page.logicalPath)
                ? $"UI/Generated/{bundle.site.siteId}/{(string.IsNullOrWhiteSpace(page.prefabName) ? "CompiledPage" : page.prefabName)}"
                : page.logicalPath;

            if (AIToUGUIRuntimePageIdUtility.ShouldUpgradeToNamespacedDefault(
                summary.runtimePageId,
                bundle.site != null ? bundle.site.siteId : string.Empty,
                page.pageId))
            {
                summary.runtimePageId = defaultRuntimePageId;
            }

            summary.attachPanelComponent = false;
            summary.panelComponentTypeName = string.Empty;

            return summary;
        }

        private static T LoadOrCreateAsset<T>(string assetPath, string legacyPath = null) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            if (TryMoveAsset(legacyPath, assetPath))
            {
                asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null)
                {
                    return asset;
                }
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var normalized = folderPath.Replace("\\", "/").TrimEnd('/');
            var parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            var folderName = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(folderName))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static bool TryMoveAsset(string sourcePath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(targetPath) ||
                string.Equals(sourcePath, targetPath, System.StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadMainAssetAtPath(sourcePath) == null ||
                AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
            {
                return false;
            }

            var targetFolder = Path.GetDirectoryName(targetPath)?.Replace("\\", "/");
            EnsureFolder(targetFolder);
            return string.IsNullOrEmpty(AssetDatabase.MoveAsset(sourcePath, targetPath));
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "CompiledBundle";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Trim();
        }

        private static UILayer ParseLayer(string raw)
        {
            return System.Enum.TryParse(raw, true, out UILayer layer) ? layer : UILayer.Normal;
        }
    }
}

#endif
