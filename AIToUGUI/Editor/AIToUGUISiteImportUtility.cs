#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Editor
{
    internal sealed class AIToUGUISourceContext
    {
        public TextAsset manifestAsset;
        public AIToUGUISitePackageManifest manifest;
        public string sourceRootFolder;
    }

    internal static class AIToUGUISiteImportUtility
    {
        private static readonly Regex RootRuleRegex = new Regex(":root\\s*\\{(?<body>[^}]*)\\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex DeclarationRegex = new Regex("(?<name>[\\w\\-]+)\\s*:\\s*(?<value>[^;]+);?", RegexOptions.Singleline);

        public static AIToUGUISiteDefinition ImportSitePackage(TextAsset manifestAsset)
        {
            if (!TryLoadManifest(manifestAsset, out var manifest))
            {
                if (manifestAsset == null)
                {
                    Debug.LogWarning("[AIToUGUI] 未指定站点清单文件。");
                }
                else
                {
                    Debug.LogError($"[AIToUGUI] 站点清单解析失败: {AssetDatabase.GetAssetPath(manifestAsset)}");
                }

                return null;
            }

            var siteFolder = AIToUGUIGeneratedAssetPaths.GetConfigRoot(manifest.siteId);
            var pageFolder = AIToUGUIGeneratedAssetPaths.GetPagesRoot(manifest.siteId);
            TryMigrateLegacySiteFolder(manifest.siteId, siteFolder);
            EnsureFolder(siteFolder);
            EnsureFolder(pageFolder);

            var sitePath = AIToUGUIGeneratedAssetPaths.GetSiteAssetPath(manifest.siteId);
            var site = LoadOrCreateAsset<AIToUGUISiteDefinition>(sitePath, AIToUGUIGeneratedAssetPaths.GetLegacySiteAssetPath(manifest.siteId));
            var sourceRootFolder = ResolveManifestFolder(manifestAsset);
            ApplyManifestToSite(site, manifestAsset, manifest, sourceRootFolder, siteFolder, pageFolder);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = site;
            return site;
        }

        public static bool SyncSiteFromManifest(AIToUGUISiteDefinition site, bool saveAssets = true)
        {
            if (site == null)
            {
                return false;
            }

            var context = ResolveSourceContext(site);
            if (context.manifest == null)
            {
                return false;
            }

            var siteFolder = AIToUGUIGeneratedAssetPaths.GetConfigRoot(context.manifest.siteId);
            var pageFolder = AIToUGUIGeneratedAssetPaths.GetPagesRoot(context.manifest.siteId);
            TryMigrateLegacySiteFolder(context.manifest.siteId, siteFolder);
            EnsureFolder(siteFolder);
            EnsureFolder(pageFolder);

            ApplyManifestToSite(site, context.manifestAsset, context.manifest, context.sourceRootFolder, siteFolder, pageFolder);

            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return true;
        }

        public static AIToUGUISourceContext ResolveSourceContext(AIToUGUISiteDefinition site, AIToUGUIPageDefinition fallbackPage = null)
        {
            var context = new AIToUGUISourceContext();
            if (site == null)
            {
                return context;
            }

            if (TryLoadManifest(site.manifestAsset, out var manifest))
            {
                context.manifestAsset = site.manifestAsset;
                context.manifest = manifest;
                context.sourceRootFolder = ResolveManifestFolder(site.manifestAsset);
                return context;
            }

            var inferredRoot = ResolveSourceRootFolder(site, fallbackPage);
            if (!string.IsNullOrWhiteSpace(inferredRoot))
            {
                context.sourceRootFolder = inferredRoot;
                var manifestPath = $"{inferredRoot}/site.json";
                var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
                if (TryLoadManifest(manifestAsset, out manifest))
                {
                    context.manifestAsset = manifestAsset;
                    context.manifest = manifest;
                    return context;
                }
            }

            if (TryLoadManifestFromPages(site, out var pageManifestAsset, out manifest, out inferredRoot))
            {
                context.manifestAsset = pageManifestAsset;
                context.manifest = manifest;
                context.sourceRootFolder = inferredRoot;
            }

            return context;
        }

        public static TextAsset ResolveTextAsset(string sourceRootFolder, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(sourceRootFolder) || string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var normalizedRoot = sourceRootFolder.Replace("\\", "/").TrimEnd('/');
            var normalizedRelative = relativePath.Replace("\\", "/").TrimStart('/');
            var directPath = $"{normalizedRoot}/{normalizedRelative}";
            var directAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(directPath);
            if (directAsset != null)
            {
                return directAsset;
            }

            var fileName = Path.GetFileName(normalizedRelative);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var fileStem = Path.GetFileNameWithoutExtension(fileName);
            var guids = AssetDatabase.FindAssets($"{fileStem} t:TextAsset", new[] { normalizedRoot });
            for (var i = 0; i < guids.Length; i++)
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace("\\", "/");
                if (string.Equals(candidatePath, directPath, StringComparison.OrdinalIgnoreCase) ||
                    candidatePath.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(candidatePath), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(candidatePath);
                    if (asset != null)
                    {
                        return asset;
                    }
                }
            }

            return null;
        }

        public static string ResolveSourceRootFolder(AIToUGUISiteDefinition site, AIToUGUIPageDefinition fallbackPage = null)
        {
            if (site == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(site.sourceRootFolder) && AssetDatabase.IsValidFolder(site.sourceRootFolder))
            {
                return site.sourceRootFolder.Replace("\\", "/").TrimEnd('/');
            }

            var manifestFolder = ResolveManifestFolder(site.manifestAsset);
            if (!string.IsNullOrWhiteSpace(manifestFolder))
            {
                return manifestFolder;
            }

            var candidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var page in EnumeratePages(site, fallbackPage))
            {
                if (page?.htmlAsset == null)
                {
                    continue;
                }

                var htmlPath = AssetDatabase.GetAssetPath(page.htmlAsset)?.Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(htmlPath))
                {
                    continue;
                }

                var candidateRoot = InferRootFromPage(htmlPath, page.sourceRelativePath);
                if (string.IsNullOrWhiteSpace(candidateRoot) || !AssetDatabase.IsValidFolder(candidateRoot))
                {
                    continue;
                }

                if (candidates.ContainsKey(candidateRoot))
                {
                    candidates[candidateRoot]++;
                }
                else
                {
                    candidates[candidateRoot] = 1;
                }
            }

            return candidates.Count == 0
                ? string.Empty
                : candidates.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
        }

        public static bool TryLoadManifest(TextAsset manifestAsset, out AIToUGUISitePackageManifest manifest)
        {
            manifest = null;
            if (manifestAsset == null || string.IsNullOrWhiteSpace(manifestAsset.text))
            {
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<AIToUGUISitePackageManifest>(manifestAsset.text);
                return manifest != null && !string.IsNullOrWhiteSpace(manifest.siteId);
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyManifestToSite(
            AIToUGUISiteDefinition site,
            TextAsset manifestAsset,
            AIToUGUISitePackageManifest manifest,
            string sourceRootFolder,
            string siteFolder,
            string pageFolder)
        {
            site.siteId = manifest.siteId;
            site.displayName = string.IsNullOrWhiteSpace(manifest.displayName) ? manifest.siteId : manifest.displayName;
            site.designResolution = new Vector2(Mathf.Max(1, manifest.designWidth), Mathf.Max(1, manifest.designHeight));
            site.manifestAsset = manifestAsset;
            site.sourceRootFolder = sourceRootFolder;
            site.prefabOutputRoot = AIToUGUIGeneratedAssetPaths.ResolvePrefabOutputRoot(manifest.siteId, manifest.prefabOutputRoot);
            site.metadataOutputRoot = AIToUGUIGeneratedAssetPaths.ResolveMetadataOutputRoot(manifest.siteId, manifest.metadataOutputRoot);
            site.sharedStyleSheets = ResolveStyles(sourceRootFolder, manifest.themeCss, manifest.sharedStyles);
            site.pages = BuildPages(manifest.siteId, pageFolder, sourceRootFolder, manifest.pages);
            site.sharedTheme = GenerateThemeAsset(siteFolder, manifest, site.sharedStyleSheets);
            site.elementLibrary = null;
            EditorUtility.SetDirty(site);
        }

        private static List<AIToUGUIPageDefinition> BuildPages(
            string siteId,
            string pageFolder,
            string sourceRootFolder,
            AIToUGUIPagePackageManifest[] pageManifests)
        {
            var pages = new List<AIToUGUIPageDefinition>();
            if (pageManifests == null)
            {
                return pages;
            }

            for (var i = 0; i < pageManifests.Length; i++)
            {
                var pageManifest = pageManifests[i];
                if (pageManifest == null || string.IsNullOrWhiteSpace(pageManifest.pageId))
                {
                    continue;
                }

                var pagePath = AIToUGUIGeneratedAssetPaths.GetPageAssetPath(siteId, pageManifest.pageId);
                var page = LoadOrCreateAsset<AIToUGUIPageDefinition>(pagePath, AIToUGUIGeneratedAssetPaths.GetLegacyPageAssetPath(siteId, pageManifest.pageId));
                page.pageId = pageManifest.pageId;
                page.displayName = string.IsNullOrWhiteSpace(pageManifest.displayName) ? pageManifest.pageId : pageManifest.displayName;
                page.sourceRelativePath = pageManifest.html;
                page.htmlAsset = ResolveTextAsset(sourceRootFolder, pageManifest.html);
                page.localStyleSheets = ResolveStyles(sourceRootFolder, null, pageManifest.localStyles);
                page.prefabName = string.IsNullOrWhiteSpace(pageManifest.prefabName)
                    ? ObjectNames.NicifyVariableName(pageManifest.pageId).Replace(" ", string.Empty)
                    : pageManifest.prefabName;
                var requestedRuntimePageId = string.IsNullOrWhiteSpace(pageManifest.runtimePageId)
                    ? AIToUGUIRuntimePageIdUtility.BuildDefaultRuntimePageId(siteId, page.pageId)
                    : pageManifest.runtimePageId.Trim();
                page.runtimePageId = AIToUGUIRuntimePageIdUtility.IsPlaceholderRuntimePageId(page.runtimePageId) ||
                                     AIToUGUIRuntimePageIdUtility.IsLegacyDefaultRuntimePageId(page.runtimePageId, page.pageId)
                    ? requestedRuntimePageId
                    : page.runtimePageId.Trim();
                page.targetLayer = Enum.TryParse(pageManifest.targetLayer, true, out UILayer layer) ? layer : UILayer.Normal;

                if (pageManifest.attachPanelComponent)
                {
                    page.attachPanelComponent = true;
                    page.panelComponentTypeName = string.IsNullOrWhiteSpace(pageManifest.panelComponentTypeName)
                        ? string.Empty
                        : pageManifest.panelComponentTypeName.Trim();
                }
                else
                {
                    page.attachPanelComponent = false;
                    page.panelComponentTypeName = string.Empty;
                }
                EditorUtility.SetDirty(page);
                pages.Add(page);
            }

            return pages;
        }

        private static List<TextAsset> ResolveStyles(string sourceRootFolder, string firstStyle, string[] additionalStyles)
        {
            var styles = new List<TextAsset>();
            AddStyle(styles, ResolveTextAsset(sourceRootFolder, firstStyle));
            if (additionalStyles != null)
            {
                for (var i = 0; i < additionalStyles.Length; i++)
                {
                    AddStyle(styles, ResolveTextAsset(sourceRootFolder, additionalStyles[i]));
                }
            }

            return styles;
        }

        private static void AddStyle(List<TextAsset> styles, TextAsset asset)
        {
            if (asset != null && !styles.Contains(asset))
            {
                styles.Add(asset);
            }
        }

        private static AIToUGUIThemeDefinition GenerateThemeAsset(
            string siteFolder,
            AIToUGUISitePackageManifest manifest,
            List<TextAsset> sharedStyleSheets)
        {
            var themePath = AIToUGUIGeneratedAssetPaths.GetThemeAssetPath(manifest.siteId);
            var theme = LoadOrCreateAsset<AIToUGUIThemeDefinition>(themePath, AIToUGUIGeneratedAssetPaths.GetLegacyThemeAssetPath(manifest.siteId));
            var tokens = ExtractCssTokens(sharedStyleSheets);

            theme.themeId = $"{manifest.siteId}_theme";
            theme.displayName = $"{(string.IsNullOrWhiteSpace(manifest.displayName) ? manifest.siteId : manifest.displayName)} Theme";
            theme.pageBackground = GetColorToken(tokens, "--page-bg", theme.pageBackground);
            theme.panelFill = GetColorToken(tokens, "--panel-fill", theme.panelFill);
            theme.cardFill = GetColorToken(tokens, "--card-fill", theme.cardFill);
            theme.buttonFill = GetColorToken(tokens, "--accent-yellow", theme.buttonFill);
            theme.accentColor = GetColorToken(tokens, "--accent-yellow", theme.accentColor);
            theme.textPrimary = GetColorToken(tokens, "--text-primary", theme.textPrimary);
            theme.textSecondary = GetColorToken(tokens, "--text-muted", theme.textSecondary);
            theme.outlineColor = GetColorToken(tokens, "--outline-dark", theme.outlineColor);
            theme.shadowColor = GetColorToken(tokens, "--shadow-hard", theme.shadowColor);
            theme.glowColor = GetColorToken(tokens, "--accent-blue", theme.glowColor);
            theme.pagePadding = GetFloatToken(tokens, "--page-pad", theme.pagePadding);
            theme.panelRadius = GetFloatToken(tokens, "--radius-md", theme.panelRadius);
            theme.cardRadius = GetFloatToken(tokens, "--radius-sm", theme.cardRadius);
            theme.buttonRadius = GetFloatToken(tokens, "--radius-sm", theme.buttonRadius);
            theme.outlineWidth = GetFloatToken(tokens, "--line-heavy", theme.outlineWidth);
            theme.shadowSize = 10f;
            theme.shadowBlur = 24f;
            theme.glowBlur = 18f;
            theme.glowIntensity = 1f;
            theme.tokens = tokens
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new AIToUGUIThemeToken
                {
                    tokenId = pair.Key,
                    value = pair.Value
                })
                .ToList();
            theme.visualPresets = BuildDefaultVisualPresets(theme);
            theme.motionPresets = BuildDefaultMotionPresets();
            theme.loopMotionPresets = BuildDefaultLoopMotionPresets();
            EditorUtility.SetDirty(theme);
            return theme;
        }

        private static AIToUGUIElementLibrary GenerateElementLibrary(string siteFolder, AIToUGUISitePackageManifest manifest)
        {
            var libraryPath = AIToUGUIGeneratedAssetPaths.GetElementLibraryAssetPath(manifest.siteId);
            var library = LoadOrCreateAsset<AIToUGUIElementLibrary>(libraryPath, AIToUGUIGeneratedAssetPaths.GetLegacyElementLibraryAssetPath(manifest.siteId));
            library.templates = new List<AIToUGUIElementTemplate>
            {
                CreateTemplate("window/main", null, AIToUGUIControlType.Div, "window/main", "panel/default", "motion/page", new Vector2(1920f, 1080f)),
                CreateTemplate("window/overlay", null, AIToUGUIControlType.Div, "window/overlay", "panel/default", "motion/page", new Vector2(1920f, 1080f)),
                CreateTemplate("panel/primary", null, AIToUGUIControlType.Div, "panel/primary", "panel/default", "motion/default", new Vector2(480f, 240f)),
                CreateTemplate("panel/dark", null, AIToUGUIControlType.Div, "panel/dark", "panel/default", "motion/default", new Vector2(480f, 240f)),
                CreateTemplate("card/resource", null, AIToUGUIControlType.Div, "card/resource", "card/default", "motion/default", new Vector2(250f, 116f)),
                CreateTemplate("card/info", null, AIToUGUIControlType.Div, "card/info", "card/default", "motion/default", new Vector2(250f, 116f)),
                CreateTemplate("button", "default", AIToUGUIControlType.Button, "button/default", "button/default", "motion/button", new Vector2(240f, 78f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("button", "primary", AIToUGUIControlType.Button, "button/primary", "button/default", "motion/button", new Vector2(240f, 78f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("button", "secondary", AIToUGUIControlType.Button, "button/secondary", "button/default", "motion/button", new Vector2(240f, 78f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("button", "danger", AIToUGUIControlType.Button, "button/danger", "button/default", "motion/button", new Vector2(240f, 78f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("button", "ghost", AIToUGUIControlType.Button, "button/ghost", "button/default", "motion/button", new Vector2(240f, 58f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("input", "default", AIToUGUIControlType.Input, "input/default", "panel/default", "motion/default", new Vector2(320f, 64f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("toggle", "default", AIToUGUIControlType.Toggle, "toggle/default", "panel/default", "motion/default", new Vector2(220f, 28f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("slider", "default", AIToUGUIControlType.Slider, "slider/default", "panel/default", "motion/default", new Vector2(320f, 24f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("dropdown", "default", AIToUGUIControlType.Dropdown, "dropdown/default", "panel/default", "motion/default", new Vector2(320f, 60f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("scrollbar", "default", AIToUGUIControlType.Scrollbar, "scrollbar/default", "panel/default", "motion/default", new Vector2(16f, 180f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("scrollview", "default", AIToUGUIControlType.Scroll, "scrollview/default", "panel/default", "motion/default", new Vector2(480f, 320f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("image", "default", AIToUGUIControlType.Image, "image/default", "panel/default", "motion/default", new Vector2(128f, 128f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("progress", "default", AIToUGUIControlType.Progress, "progress/default", "panel/default", "motion/default", new Vector2(320f, 24f), AIToUGUIElementBackingMode.PrefabBacked),
                CreateTemplate("chip/tag", null, AIToUGUIControlType.Div, "chip/tag", "card/default", "motion/default", new Vector2(180f, 42f))
            };
            EditorUtility.SetDirty(library);
            return library;
        }

        private static AIToUGUIElementTemplate CreateTemplate(
            string elementId,
            string variantId,
            AIToUGUIControlType controlType,
            string defaultRole,
            string visualPresetId,
            string motionPresetId,
            Vector2 defaultSize,
            AIToUGUIElementBackingMode backingMode = AIToUGUIElementBackingMode.StyleBacked)
        {
            return new AIToUGUIElementTemplate
            {
                elementId = elementId,
                variantId = variantId ?? string.Empty,
                controlType = controlType,
                backingMode = backingMode,
                defaultRole = defaultRole,
                visualPresetId = visualPresetId,
                motionPresetId = motionPresetId,
                defaultSize = defaultSize,
                useThemeTextColor = true,
                useThemeAccentColor = false
            };
        }

        private static List<AIToUGUIVisualPreset> BuildDefaultVisualPresets(AIToUGUIThemeDefinition theme)
        {
            return new List<AIToUGUIVisualPreset>
            {
                new AIToUGUIVisualPreset
                {
                    presetId = "panel/default",
                    enableFill = true,
                    fillColor = theme.panelFill,
                    useGradient = false,
                    cornerRadius = theme.panelRadius,
                    outlineWidth = theme.outlineWidth,
                    outlineColor = theme.outlineColor,
                    shadowSize = theme.shadowSize,
                    shadowBlur = theme.shadowBlur,
                    shadowColor = theme.shadowColor
                },
                new AIToUGUIVisualPreset
                {
                    presetId = "card/default",
                    enableFill = true,
                    fillColor = theme.cardFill,
                    useGradient = false,
                    cornerRadius = theme.cardRadius,
                    outlineWidth = Mathf.Max(1f, theme.outlineWidth - 1f),
                    outlineColor = theme.outlineColor,
                    shadowSize = Mathf.Max(6f, theme.shadowSize - 4f),
                    shadowBlur = Mathf.Max(12f, theme.shadowBlur - 8f),
                    shadowColor = theme.shadowColor
                },
                new AIToUGUIVisualPreset
                {
                    presetId = "button/default",
                    enableFill = true,
                    fillColor = theme.buttonFill,
                    useGradient = false,
                    cornerRadius = theme.buttonRadius,
                    outlineWidth = theme.outlineWidth,
                    outlineColor = theme.outlineColor,
                    shadowSize = theme.shadowSize,
                    shadowBlur = theme.shadowBlur,
                    shadowColor = theme.shadowColor
                }
            };
        }

        private static List<AIToUGUIMotionPreset> BuildDefaultMotionPresets()
        {
            return new List<AIToUGUIMotionPreset>
            {
                new AIToUGUIMotionPreset
                {
                    presetId = "motion/default",
                    enterMotion = AIToUGUIMotionType.Fade,
                    hoverMotion = AIToUGUIMotionType.None,
                    pressMotion = AIToUGUIMotionType.None,
                    duration = 0.18f
                },
                new AIToUGUIMotionPreset
                {
                    presetId = "motion/page",
                    enterMotion = AIToUGUIMotionType.SlideUp,
                    hoverMotion = AIToUGUIMotionType.None,
                    pressMotion = AIToUGUIMotionType.None,
                    duration = 0.24f,
                    distance = 30f
                },
                new AIToUGUIMotionPreset
                {
                    presetId = "motion/button",
                    enterMotion = AIToUGUIMotionType.None,
                    hoverMotion = AIToUGUIMotionType.HoverLift,
                    pressMotion = AIToUGUIMotionType.ScaleIn,
                    duration = 0.16f,
                    distance = 10f,
                    scale = 0.97f
                }
            };
        }

        private static List<AIToUGUILoopMotionPreset> BuildDefaultLoopMotionPresets()
        {
            return new List<AIToUGUILoopMotionPreset>
            {
                new AIToUGUILoopMotionPreset
                {
                    presetId = "loop/rotate-slow",
                    loopType = AIToUGUILoopMotionType.Rotate,
                    duration = 20f,
                    amplitude = 1f,
                    ease = Ease.Linear
                },
                new AIToUGUILoopMotionPreset
                {
                    presetId = "loop/rotate-slow-reverse",
                    loopType = AIToUGUILoopMotionType.RotateReverse,
                    duration = 15f,
                    amplitude = 1f,
                    ease = Ease.Linear
                },
                new AIToUGUILoopMotionPreset
                {
                    presetId = "loop/float-soft",
                    loopType = AIToUGUILoopMotionType.Float,
                    duration = 8f,
                    amplitude = 20f,
                    ease = Ease.InOutSine
                },
                new AIToUGUILoopMotionPreset
                {
                    presetId = "loop/pulse-soft",
                    loopType = AIToUGUILoopMotionType.Pulse,
                    duration = 3f,
                    amplitude = 0.06f,
                    ease = Ease.InOutSine
                }
            };
        }

        private static Dictionary<string, string> ExtractCssTokens(IEnumerable<TextAsset> styleAssets)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (styleAssets == null)
            {
                return tokens;
            }

            foreach (var styleAsset in styleAssets)
            {
                if (styleAsset == null || string.IsNullOrWhiteSpace(styleAsset.text))
                {
                    continue;
                }

                foreach (Match rootMatch in RootRuleRegex.Matches(styleAsset.text))
                {
                    var body = rootMatch.Groups["body"].Value;
                    foreach (Match declarationMatch in DeclarationRegex.Matches(body))
                    {
                        var name = declarationMatch.Groups["name"].Value.Trim();
                        var value = declarationMatch.Groups["value"].Value.Trim();
                        if (name.StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value))
                        {
                            tokens[name] = value;
                        }
                    }
                }
            }

            return tokens;
        }

        private static Color GetColorToken(Dictionary<string, string> tokens, string tokenId, Color fallback)
        {
            if (tokens == null || !tokens.TryGetValue(tokenId, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (TryExtractColor(value, out var color))
            {
                return color;
            }

            return fallback;
        }

        private static float GetFloatToken(Dictionary<string, string> tokens, string tokenId, float fallback)
        {
            if (tokens == null || !tokens.TryGetValue(tokenId, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var match = Regex.Match(value, "-?\\d+(?:\\.\\d+)?");
            return match.Success && float.TryParse(match.Value, out var parsed) ? parsed : fallback;
        }

        private static bool TryExtractColor(string value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            if (normalized.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString(normalized, out color))
            {
                return true;
            }

            var rgbaIndex = normalized.IndexOf("rgba", StringComparison.OrdinalIgnoreCase);
            if (rgbaIndex < 0)
            {
                rgbaIndex = normalized.IndexOf("rgb", StringComparison.OrdinalIgnoreCase);
            }

            if (rgbaIndex >= 0)
            {
                var start = normalized.IndexOf('(', rgbaIndex);
                var end = normalized.IndexOf(')', start + 1);
                if (start >= 0 && end > start)
                {
                    var parts = normalized.Substring(start + 1, end - start - 1)
                        .Split(',')
                        .Select(part => part.Trim())
                        .ToArray();
                    if (parts.Length >= 3 &&
                        float.TryParse(parts[0], out var r) &&
                        float.TryParse(parts[1], out var g) &&
                        float.TryParse(parts[2], out var b))
                    {
                        var a = 1f;
                        if (parts.Length >= 4 && float.TryParse(parts[3], out var alpha))
                        {
                            a = alpha;
                        }

                        color = new Color(r / 255f, g / 255f, b / 255f, Mathf.Clamp01(a));
                        return true;
                    }
                }
            }

            return false;
        }

        private static string ResolveManifestFolder(TextAsset manifestAsset)
        {
            if (manifestAsset == null)
            {
                return string.Empty;
            }

            var assetPath = AssetDatabase.GetAssetPath(manifestAsset);
            return string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        }

        private static bool TryLoadManifestFromPages(
            AIToUGUISiteDefinition site,
            out TextAsset manifestAsset,
            out AIToUGUISitePackageManifest manifest,
            out string sourceRootFolder)
        {
            manifestAsset = null;
            manifest = null;
            sourceRootFolder = string.Empty;

            var rootFolder = ResolveSourceRootFolder(site);
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return false;
            }

            var siteJsonPath = $"{rootFolder}/site.json";
            manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(siteJsonPath);
            if (!TryLoadManifest(manifestAsset, out manifest))
            {
                return false;
            }

            sourceRootFolder = rootFolder;
            return true;
        }

        private static IEnumerable<AIToUGUIPageDefinition> EnumeratePages(AIToUGUISiteDefinition site, AIToUGUIPageDefinition fallbackPage)
        {
            if (site?.pages != null)
            {
                for (var i = 0; i < site.pages.Count; i++)
                {
                    yield return site.pages[i];
                }
            }

            if (fallbackPage != null && (site?.pages == null || !site.pages.Contains(fallbackPage)))
            {
                yield return fallbackPage;
            }
        }

        private static string InferRootFromPage(string htmlAssetPath, string sourceRelativePath)
        {
            if (string.IsNullOrWhiteSpace(htmlAssetPath))
            {
                return string.Empty;
            }

            var normalizedHtmlPath = htmlAssetPath.Replace("\\", "/");
            var normalizedRelative = sourceRelativePath?.Replace("\\", "/").TrimStart('/');
            if (!string.IsNullOrWhiteSpace(normalizedRelative) &&
                normalizedHtmlPath.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedHtmlPath.Substring(0, normalizedHtmlPath.Length - normalizedRelative.Length).TrimEnd('/');
            }

            var htmlFolder = Path.GetDirectoryName(normalizedHtmlPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(htmlFolder))
            {
                return string.Empty;
            }

            if (htmlFolder.EndsWith("/pages", StringComparison.OrdinalIgnoreCase))
            {
                return htmlFolder.Substring(0, htmlFolder.Length - "/pages".Length);
            }

            return htmlFolder;
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

        private static void TryMigrateLegacySiteFolder(string siteId, string targetConfigFolder)
        {
            var legacyFolder = AIToUGUIGeneratedAssetPaths.GetLegacySiteFolder(siteId);
            if (string.IsNullOrWhiteSpace(siteId) ||
                string.IsNullOrWhiteSpace(targetConfigFolder) ||
                AssetDatabase.IsValidFolder(targetConfigFolder) ||
                !AssetDatabase.IsValidFolder(legacyFolder))
            {
                return;
            }

            var packageRoot = AIToUGUIGeneratedAssetPaths.GetPackageRoot(siteId);
            EnsureFolder(packageRoot);
            AssetDatabase.MoveAsset(legacyFolder, targetConfigFolder);
        }

        private static bool TryMoveAsset(string sourcePath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(targetPath) ||
                string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadMainAssetAtPath(sourcePath) == null ||
                AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
            {
                return false;
            }

            var targetFolder = Path.GetDirectoryName(targetPath)?.Replace("\\", "/");
            EnsureFolder(targetFolder);
            return string.IsNullOrEmpty(AssetDatabase.MoveAsset(sourcePath, targetPath));
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}

#endif
