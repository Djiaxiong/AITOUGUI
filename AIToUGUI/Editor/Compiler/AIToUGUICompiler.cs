#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Editor
{
    internal sealed class AIToUGUICompiledPage
    {
        public AIToUGUISiteDefinition SourceSite;
        public AIToUGUIPageDefinition SourcePage;
        public AIToUGUICompiledBundleDefinition SourceBundle;
        public string SourceBundleJsonAssetPath;
        public string SiteId;
        public string PageId;
        public string SourcePageRelativePath;
        public string RuntimePageId;
        public string DisplayName;
        public string PrefabName;
        public string PanelComponentTypeName;
        public bool AttachPanelComponent;
        public string LogicalPath;
        public string PrefabOutputRoot;
        public string MetadataOutputRoot;
        public UILayer TargetLayer;
        public Vector2 DesignResolution;
        public AIToUGUIThemeDefinition Theme;
        public AIToUGUIElementLibrary ElementLibrary;
        public bool KeepExportNodeMarkers = true;
        public bool KeepAssetBindingManifests;
        public bool UseOverflowMaskHosts = true;
        public AIToUGUICompiledNode Root;
        public int AutoNamedNodeCount;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    internal sealed class AIToUGUICompiledNode
    {
        public bool IsRoot;
        public string Name;
        public string TagName;
        public string Role;
        public string ElementId;
        public string VariantId;
        public string ShapeId;
        public string FrameId;
        public string SlotId;
        public string ContainerId;
        public string TemplateId;
        public string ComponentFamily;
        public string ComponentVariant;
        public bool HasExplicitTemplateId;
        public bool HasExplicitComponentFamily;
        public bool HasExplicitCompositeElement;
        public string RenderStrategy = AIToUGUIElementContractUtility.ProceduralRenderStrategyId;
        public string MotionId;
        public string Text;
        // Track C: locked nodes have authoritative geometry from the Python snapshot and
        // should bypass Unity's flex/content-size fallbacks. suggested nodes keep the
        // current behavior.
        public string StabilityLevel = "suggested";
        public AIToUGUIMeasuredRect AbsoluteRect;
        public AIToUGUIControlType ControlType;
        public readonly List<AIToUGUIAssetReference> AssetRefs = new List<AIToUGUIAssetReference>();
        public readonly List<string> FidelityNotes = new List<string>();
        public readonly List<string> Classes = new List<string>();
        public readonly Dictionary<string, string> Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<AIToUGUICompiledNode> Children = new List<AIToUGUICompiledNode>();
    }

    internal struct AIToUGUIMeasuredRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public bool Measured;
        public string Source;

        public bool IsLockedTo(string stabilityLevel)
        {
            return Measured && string.Equals(stabilityLevel, "locked", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class AIToUGUICssRule
    {
        public string Selector;
        public readonly Dictionary<string, string> Declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class AIToUGUICompiler
    {
        private static readonly Regex StyleBlockRegex = new Regex("<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex VoidTagRegex = new Regex("<(input|img|br)([^>/]*?)>", RegexOptions.IgnoreCase);
        private static readonly Regex RuleRegex = new Regex("(?<selector>[^{}]+)\\{(?<body>[^{}]*)\\}", RegexOptions.Singleline);
        private static readonly Regex DeclarationRegex = new Regex("(?<name>[\\w\\-]+)\\s*:\\s*(?<value>[^;]+);?", RegexOptions.Singleline);
        private static readonly Regex VarRegex = new Regex("var\\((?<token>--[\\w\\-]+)\\)", RegexOptions.IgnoreCase);
        private static readonly Regex RoleSelectorRegex = new Regex("^\\[(data-ui-role|data-u-role)\\s*=\\s*\"(?<value>[^\"]+)\"\\]$", RegexOptions.IgnoreCase);
        private static readonly Regex LinkStyleRegex = new Regex("<link(?=[^>]*rel\\s*=\\s*[\"']stylesheet[\"'])(?=[^>]*href\\s*=\\s*[\"'](?<href>[^\"']+)[\"'])[^>]*>", RegexOptions.IgnoreCase);
        private static readonly Regex MultiWhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new Regex("-?\\d+(?:\\.\\d+)?", RegexOptions.Compiled);
        private static readonly Regex RotateTransformRegex = new Regex("^rotate\\(\\s*(?<angle>-?\\d+(?:\\.\\d+)?)deg\\s*\\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnimationRegex = new Regex(
            "^(?<name>rotate|float|pulse)\\s+(?<duration>-?\\d+(?:\\.\\d+)?)s\\s+(?<timing>linear|ease-in-out)\\s+infinite(?:\\s+(?<direction>reverse))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] InheritableTextStyleKeys =
        {
            "color",
            "font-size",
            "font-family",
            "font-weight",
            "line-height",
            "text-align",
            "letter-spacing",
            "text-transform"
        };

        private static readonly HashSet<string> SupportedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "class",
            "style",
            "data-ui-page",
            "data-ui-type",
            "data-ui-name",
            "data-ui-role",
            "data-ui-element",
            "data-ui-variant",
            "data-ui-shape",
            "data-ui-frame",
            "data-ui-template-size",
            "data-ui-slot",
            "data-ui-container",
            "data-ui-template",
            "data-ui-component-family",
            "data-ui-component-variant",
            "data-ui-render-strategy",
            "data-ui-motion",
            "data-ui-rotation",
            "data-ui-loop-motion",
            "data-ui-glow",
            "data-ui-glow-color",
            "data-ui-glow-blur",
            "data-ui-glow-power",
            "data-ui-icon",
            "data-ui-icon-size",
            "data-ui-asset-id",
            "data-ui-asset-type",
            "data-ui-asset-usage",
            "data-ui-asset-import",
            "data-ui-asset-slice",
            "data-ui-asset-ppu",
            "data-ui-asset-width",
            "data-ui-asset-height",
            "data-ui-asset-tint",
            "data-ui-asset-atlas",
            "data-ui-fidelity-note",
            "data-ui-value",
            "data-ui-dir",
            "data-ui-layout",
            "data-ui-layout-item",
            "data-ui-grid-columns",
            "data-ui-grid-rows",
            "data-ui-grid-layers",
            "data-ui-grid-cell-type",
            "data-ui-grid-cell-width",
            "data-ui-grid-cell-height",
            "data-ui-grid-gap-x",
            "data-ui-grid-gap-y",
            "data-ui-grid-column-direction",
            "data-ui-grid-row-direction",
            "data-ui-grid-align-x",
            "data-ui-grid-align-y",
            "data-ui-curve-spacing-mode",
            "data-ui-curve-spacing",
            "data-ui-curve-start-at",
            "data-ui-curve-rotation",
            "data-ui-curve-extend-before",
            "data-ui-curve-extend-after",
            "data-ui-curve-lock-tangents",
            "data-ui-curve-lock-positions",
            "data-ui-curve-points"
        };

        public static AIToUGUICompiledPage Compile(AIToUGUISiteDefinition site, AIToUGUIPageDefinition page)
        {
            var sourceContext = AIToUGUISiteImportUtility.ResolveSourceContext(site, page);
            var resolvedSiteId = site != null && !string.IsNullOrWhiteSpace(site.siteId)
                ? site.siteId
                : sourceContext.manifest != null ? sourceContext.manifest.siteId : string.Empty;
            var resolvedPageId = page != null ? page.pageId : string.Empty;
            var compiled = new AIToUGUICompiledPage
            {
                SourceSite = site,
                SourcePage = page,
                SiteId = resolvedSiteId,
                PageId = resolvedPageId,
                SourcePageRelativePath = page != null ? page.sourceRelativePath : string.Empty,
                RuntimePageId = AIToUGUIRuntimePageIdUtility.ResolveDefaultCompatibleRuntimePageId(
                    page != null ? page.runtimePageId : string.Empty,
                    resolvedSiteId,
                    resolvedPageId),
                DisplayName = page != null ? page.displayName : string.Empty,
                PrefabName = page != null && !string.IsNullOrWhiteSpace(page.prefabName) ? page.prefabName : "GeneratedPanel",
                PanelComponentTypeName = page != null ? page.panelComponentTypeName : string.Empty,
                AttachPanelComponent = page != null && page.attachPanelComponent,
                LogicalPath = $"UI/Generated/{resolvedSiteId}/{(page != null && !string.IsNullOrWhiteSpace(page.prefabName) ? page.prefabName : "GeneratedPanel")}".Trim('/'),
                PrefabOutputRoot = site != null ? site.prefabOutputRoot : AIToUGUIGeneratedAssetPaths.GetPrefabsRoot(resolvedSiteId),
                MetadataOutputRoot = site != null ? site.metadataOutputRoot : AIToUGUIGeneratedAssetPaths.GetMetadataRoot(resolvedSiteId),
                DesignResolution = site != null ? site.designResolution : new Vector2(1920f, 1080f),
                TargetLayer = page != null ? page.targetLayer : site != null ? site.defaultLayer : UILayer.Normal,
                Theme = page != null && page.overrideTheme != null ? page.overrideTheme : site != null ? site.sharedTheme : null,
                ElementLibrary = site != null ? site.elementLibrary : null,
                KeepExportNodeMarkers = site == null || site.keepExportNodeMarkers,
                KeepAssetBindingManifests = site != null && site.keepAssetBindingManifests,
                UseOverflowMaskHosts = site == null || site.useOverflowMaskHosts
            };

            var htmlAsset = page != null && page.htmlAsset != null
                ? page.htmlAsset
                : AIToUGUISiteImportUtility.ResolveTextAsset(sourceContext.sourceRootFolder, page != null ? page.sourceRelativePath : string.Empty);
            if (page != null && page.htmlAsset == null && htmlAsset != null)
            {
                page.htmlAsset = htmlAsset;
                EditorUtility.SetDirty(page);
            }

            if (page == null || htmlAsset == null)
            {
                compiled.Warnings.Add("页面或 HTML 输入为空，无法继续编译。");
                return compiled;
            }

            var htmlText = htmlAsset.text ?? string.Empty;
            var inlineStyles = ExtractStyleBlocks(htmlText, out var strippedHtml);
            var allStyleTexts = new List<string>();
            if (site != null && site.sharedStyleSheets != null)
            {
                allStyleTexts.AddRange(site.sharedStyleSheets.Where(asset => asset != null).Select(asset => asset.text));
            }

            allStyleTexts.AddRange(LoadSiteStylesFromManifest(sourceContext, compiled.Warnings));

            if (page.localStyleSheets != null)
            {
                allStyleTexts.AddRange(page.localStyleSheets.Where(asset => asset != null).Select(asset => asset.text));
            }

            allStyleTexts.AddRange(LoadPageStylesFromManifest(sourceContext, page, compiled.Warnings));
            allStyleTexts.AddRange(LoadLinkedStylesheets(strippedHtml, sourceContext, site, page, compiled.Warnings));
            allStyleTexts.AddRange(inlineStyles);
            allStyleTexts = allStyleTexts
                .Where(styleText => !string.IsNullOrWhiteSpace(styleText))
                .Distinct()
                .ToList();

            var tokens = BuildThemeTokens(compiled.Theme);
            var rules = ParseRules(allStyleTexts, tokens, compiled.Warnings);
            var rootElement = ParseHtml(strippedHtml, compiled.Warnings);
            if (rootElement == null)
            {
                compiled.Warnings.Add("HTML 解析失败，未找到可导出的根节点。");
                return compiled;
            }

            var autoNameCounter = 0;
            compiled.Root = CompileNode(rootElement, compiled, rules, tokens, ref autoNameCounter, true, null);
            if (compiled.Root == null)
            {
                compiled.Warnings.Add("HTML 编译失败，未生成页面根节点。");
                return compiled;
            }

            if (string.IsNullOrWhiteSpace(compiled.PageId) &&
                compiled.Root.Attributes.TryGetValue("data-ui-page", out var pageId))
            {
                compiled.PageId = pageId;
            }

            compiled.RuntimePageId = AIToUGUIRuntimePageIdUtility.ResolveDefaultCompatibleRuntimePageId(
                compiled.RuntimePageId,
                compiled.SiteId,
                compiled.PageId);

            if (compiled.Root.Styles.Count == 0 && compiled.Theme != null)
            {
                compiled.Root.Styles["background-color"] = ToHtmlColor(compiled.Theme.pageBackground);
            }

            compiled.AutoNamedNodeCount = compiled.Warnings.Count(warning =>
                !string.IsNullOrWhiteSpace(warning) &&
                warning.IndexOf("data-ui-name", StringComparison.OrdinalIgnoreCase) >= 0);
            compiled.Warnings.RemoveAll(warning =>
                !string.IsNullOrWhiteSpace(warning) &&
                warning.IndexOf("data-ui-name", StringComparison.OrdinalIgnoreCase) >= 0);

            if (compiled.AutoNamedNodeCount > 0)
            {
                compiled.Warnings.Add($"There are {compiled.AutoNamedNodeCount} nodes without data-ui-name. Generated names were assigned automatically.");
            }

            ValidateDuplicateExportNames(compiled.Root, compiled.Errors);
            ValidateDuplicateSemanticIds(compiled.Root, compiled.Errors);
            DeduplicateWarnings(compiled.Warnings);
            DeduplicateWarnings(compiled.Errors);

            return compiled;
        }

        private static AIToUGUICompiledNode CompileNode(
            XElement element,
            AIToUGUICompiledPage page,
            List<AIToUGUICssRule> rules,
            Dictionary<string, string> tokens,
            ref int autoNameCounter,
            bool isRoot,
            AIToUGUICompiledNode parentNode)
        {
            if (element == null)
            {
                return null;
            }

            var tagName = element.Name.LocalName.ToLowerInvariant();
            if (tagName == "style" || tagName == "script" || tagName == "head" || tagName == "meta" || tagName == "link")
            {
                return null;
            }

            var node = new AIToUGUICompiledNode
            {
                IsRoot = isRoot,
                TagName = tagName
            };

            foreach (var attribute in element.Attributes())
            {
                var key = NormalizeAttributeName(attribute.Name.LocalName);
                var value = attribute.Value?.Trim() ?? string.Empty;
                node.Attributes[key] = value;
            }

            PromoteLegacyAttributes(node, page);

            var explicitType = GetAttribute(node, "data-ui-type");
            node.ControlType = ParseControlType(explicitType, tagName);
            node.Role = GetAttribute(node, "data-ui-role");
            node.ElementId = GetAttribute(node, "data-ui-element");
            node.VariantId = GetAttribute(node, "data-ui-variant");
            node.ShapeId = GetAttribute(node, "data-ui-shape");
            node.FrameId = GetAttribute(node, "data-ui-frame");
            node.SlotId = GetAttribute(node, "data-ui-slot");
            node.ContainerId = GetAttribute(node, "data-ui-container");
            node.TemplateId = GetAttribute(node, "data-ui-template");
            node.ComponentFamily = GetAttribute(node, "data-ui-component-family");
            node.ComponentVariant = GetAttribute(node, "data-ui-component-variant");
            node.RenderStrategy = GetAttribute(node, "data-ui-render-strategy");
            node.MotionId = GetAttribute(node, "data-ui-motion");
            node.Name = GetAttribute(node, "data-ui-name");
            node.HasExplicitTemplateId = !string.IsNullOrWhiteSpace(node.TemplateId);
            node.HasExplicitComponentFamily = !string.IsNullOrWhiteSpace(node.ComponentFamily);
            node.HasExplicitCompositeElement =
                !string.IsNullOrWhiteSpace(node.ElementId) &&
                AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(node.ElementId);

            NormalizeElementIdentity(node);
            NormalizeComponentIntent(node, null, element);
            if (node.ControlType == AIToUGUIControlType.Auto || node.ControlType == AIToUGUIControlType.Div)
            {
                var primitiveType = AIToUGUIElementContractUtility.InferPrimitiveControlType(node.ElementId);
                if (primitiveType != AIToUGUIControlType.Div)
                {
                    node.ControlType = primitiveType;
                }
            }

            if (string.IsNullOrWhiteSpace(node.Name))
            {
                var requiresExplicitName = RequiresExplicitName(node, isRoot);
                node.Name = isRoot
                    ? page.PrefabName
                    : $"{ObjectNames.NicifyVariableName(tagName).Replace(" ", string.Empty)}_{++autoNameCounter}";
                if (requiresExplicitName)
                {
                    page.Errors.Add($"节点 <{tagName}> 缺少必填的 data-ui-name。已临时使用 {node.Name} 继续编译，但该页面不可正式烘焙。");
                }
                else
                {
                    page.Warnings.Add($"节点 <{tagName}> 未声明 data-ui-name，已自动命名为 {node.Name}。");
                }
            }

            var classValue = GetAttribute(node, "class");
            if (!string.IsNullOrWhiteSpace(classValue))
            {
                node.Classes.AddRange(classValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            WarnUnsupportedAttributes(node, page);

            var template = ResolveAuthoringTemplate(page, node);
            if (AIToUGUIElementContractUtility.IsPrimitiveElement(node.ElementId) &&
                template == null &&
                !HasCompositeIntent(node))
            {
                page.Errors.Add($"Node '{node.Name}' uses unsupported primitive variant '{AIToUGUIElementContractUtility.BuildElementKey(node.ElementId, node.VariantId)}'.");
            }

            if (template != null)
            {
                if (node.ControlType == AIToUGUIControlType.Auto)
                {
                    node.ControlType = template.controlType;
                }

                if (string.IsNullOrWhiteSpace(node.Role))
                {
                    node.Role = template.defaultRole;
                }

                if (string.IsNullOrWhiteSpace(node.MotionId))
                {
                    node.MotionId = template.motionPresetId;
                }

                if (!string.IsNullOrWhiteSpace(template.visualPresetId))
                {
                    node.Styles["-ai-preset"] = template.visualPresetId;
                }
            }

            ApplyRuleSet(node, rules, tokens);
            ApplyInlineStyle(node, tokens);
            ApplyInheritedTextStyles(parentNode, node);
            NormalizeExtendedCapabilities(node, page);
            NormalizeComponentIntent(node, template, element);
            PopulateAssetIntent(node, page);

            if (!string.IsNullOrWhiteSpace(node.MotionId))
            {
                node.Styles["-ai-motion"] = node.MotionId;
            }

            var useTemplateSize = ShouldUseTemplateSize(node);
            if (template != null && useTemplateSize && !node.Styles.ContainsKey("width") && template.defaultSize.x > 0f)
            {
                node.Styles["width"] = $"{template.defaultSize.x}px";
            }

            if (template != null && useTemplateSize && !node.Styles.ContainsKey("height") && template.defaultSize.y > 0f)
            {
                node.Styles["height"] = $"{template.defaultSize.y}px";
            }

            var directText = ExtractDirectInnerText(element);
            if (ShouldBindText(node, tagName, directText, element))
            {
                node.Text = ApplyTextTransform(directText, GetStyle(node, "text-transform"));
            }

            WarnUnsupportedStyles(node, page);

            foreach (var child in element.Elements())
            {
                var compiledChild = CompileNode(child, page, rules, tokens, ref autoNameCounter, false, node);
                if (compiledChild != null)
                {
                    node.Children.Add(compiledChild);
                }
            }

            AutoAssignCompositeChildSlots(node, template);
            PromotePrimitiveTextChild(node);
            ValidatePrimitiveContract(node, page);

            return node;
        }

        private static void PromoteLegacyAttributes(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            PromoteAttribute(node, "data-ui-glow", "-ai-glow");
            PromoteAttribute(node, "data-ui-glow-color", "-ai-glow-color");
            PromoteAttribute(node, "data-ui-glow-blur", "-ai-glow-blur");
            PromoteAttribute(node, "data-ui-glow-power", "-ai-glow-intensity");
            PromoteAttribute(node, "data-ui-icon", "-ai-icon");
            PromoteAttribute(node, "data-ui-icon-size", "-ai-icon-size");
            PromoteAttribute(node, "data-ui-value", "-ai-value");
            PromoteAttribute(node, "data-ui-dir", "-ai-direction");

            if (node.Attributes.ContainsKey("data-ui-icon"))
            {
                page.Warnings.Add($"节点 {node.Name ?? node.TagName} 使用了 data-ui-icon，当前会生成 TMP 占位图标，正式项目建议后续替换为真实图像资源。");
            }
        }

        private static void PromoteAttribute(AIToUGUICompiledNode node, string attributeKey, string styleKey)
        {
            if (node.Attributes.TryGetValue(attributeKey, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                node.Styles[styleKey] = value;
            }
        }

        private static void WarnUnsupportedAttributes(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            foreach (var pair in node.Attributes)
            {
                if (SupportedAttributes.Contains(pair.Key))
                {
                    continue;
                }

                if (!pair.Key.StartsWith("data-ui-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                page.Warnings.Add($"节点 {node.Name} 包含暂未支持的属性 {pair.Key}，当前烘焙会忽略它。");
            }
        }

        private static void WarnUnsupportedStyles(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (TryGetStyle(node, "background", out var background) &&
                background.IndexOf("radial-gradient", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                page.Warnings.Add($"节点 {node.Name} 使用了 radial-gradient，当前会退化为近似纯色/线性渐变。");
            }
        }

        private static void ApplyRuleSet(AIToUGUICompiledNode node, List<AIToUGUICssRule> rules, Dictionary<string, string> tokens)
        {
            if (node == null || rules == null)
            {
                return;
            }

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!Matches(rule.Selector, node))
                {
                    continue;
                }

                foreach (var pair in rule.Declarations)
                {
                    node.Styles[pair.Key] = ReplaceTokens(pair.Value, tokens);
                }
            }
        }

        private static void ApplyInlineStyle(AIToUGUICompiledNode node, Dictionary<string, string> tokens)
        {
            var styleValue = GetAttribute(node, "style");
            if (string.IsNullOrWhiteSpace(styleValue))
            {
                return;
            }

            foreach (var pair in ParseDeclarationBlock(styleValue))
            {
                node.Styles[pair.Key] = ReplaceTokens(pair.Value, tokens);
            }
        }

        private static void NormalizeExtendedCapabilities(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (node == null)
            {
                return;
            }

            NormalizeRotation(node, page);
            NormalizeLoopMotion(node, page);
            NormalizeBorderStyle(node);
        }

        private static void NormalizeRotation(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            var rawRotation = GetAttribute(node, "data-ui-rotation");
            if (TryParseRotationDegrees(rawRotation, out var explicitRotation))
            {
                node.Styles["-ai-rotation-z"] = $"{explicitRotation:0.###}";
                return;
            }

            var transform = GetStyle(node, "transform");
            if (string.IsNullOrWhiteSpace(transform))
            {
                return;
            }

            if (TryParseRotationDegrees(transform, out var rotation))
            {
                node.Styles["-ai-rotation-z"] = $"{rotation:0.###}";
                return;
            }

            page?.Warnings.Add($"节点 {node.Name} 使用了不受支持的 transform，当前仅兼容 rotate(<deg>)。");
        }

        private static void NormalizeLoopMotion(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            var explicitLoopMotion = GetAttribute(node, "data-ui-loop-motion");
            if (!string.IsNullOrWhiteSpace(explicitLoopMotion))
            {
                node.Styles["-ai-loop-motion"] = explicitLoopMotion.Trim();
            }

            var animation = GetStyle(node, "animation");
            if (!string.IsNullOrWhiteSpace(animation) && !node.Styles.ContainsKey("-ai-loop-motion"))
            {
                var match = AnimationRegex.Match(animation.Trim());
                if (match.Success)
                {
                    node.Styles["-ai-loop-motion"] = ResolveLoopMotionPresetId(
                        match.Groups["name"].Value,
                        match.Groups["direction"].Value);
                }
                else
                {
                    page?.Warnings.Add($"节点 {node.Name} 使用了不受支持的 animation，当前仅兼容 rotate/float/pulse 循环动效。");
                }
            }

            var animationDelay = GetStyle(node, "animation-delay");
            if (TryParseAnimationDelay(animationDelay, out var delay))
            {
                node.Styles["-ai-loop-delay"] = $"{delay:0.###}";
            }
        }

        private static void NormalizeBorderStyle(AIToUGUICompiledNode node)
        {
            var explicitBorderStyle = GetStyle(node, "border-style");
            if (!string.IsNullOrWhiteSpace(explicitBorderStyle))
            {
                var normalized = explicitBorderStyle.Trim().ToLowerInvariant();
                if (normalized == "dashed" || normalized == "solid")
                {
                    node.Styles["-ai-border-style"] = normalized;
                    return;
                }
            }

            var border = GetStyle(node, "border");
            if (string.IsNullOrWhiteSpace(border))
            {
                return;
            }

            var loweredBorder = border.ToLowerInvariant();
            if (loweredBorder.Contains("dashed"))
            {
                node.Styles["-ai-border-style"] = "dashed";
            }
            else if (loweredBorder.Contains("solid"))
            {
                node.Styles["-ai-border-style"] = "solid";
            }
        }

        private static void NormalizeComponentIntent(AIToUGUICompiledNode node, AIToUGUIElementTemplate template, XElement element)
        {
            if (node == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.ComponentFamily) && template != null)
            {
                node.ComponentFamily = template.componentFamily;
            }

            if (string.IsNullOrWhiteSpace(node.ComponentVariant) && template != null)
            {
                node.ComponentVariant = template.componentVariant;
            }

            if (string.IsNullOrWhiteSpace(node.ComponentFamily))
            {
                node.ComponentFamily = InferComponentFamily(node, element);
            }

            node.ComponentFamily = AIToUGUIElementContractUtility.NormalizeComponentFamily(node.ComponentFamily);
            if (!string.IsNullOrWhiteSpace(node.ComponentFamily))
            {
                if (string.IsNullOrWhiteSpace(node.ComponentVariant))
                {
                    node.ComponentVariant = InferComponentVariant(node, template);
                }

                node.ComponentVariant = AIToUGUIElementContractUtility.NormalizeComponentVariantId(node.ComponentVariant);
                node.Attributes["data-ui-component-family"] = node.ComponentFamily;
                node.Attributes["data-ui-component-variant"] = node.ComponentVariant;
            }
            else
            {
                node.ComponentVariant = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(node.RenderStrategy) && template != null)
            {
                node.RenderStrategy = template.defaultRenderStrategy.ToString();
            }

            node.RenderStrategy = InferRenderStrategy(node);
            node.Attributes["data-ui-render-strategy"] = node.RenderStrategy;
        }

        private static AIToUGUIElementTemplate ResolveAuthoringTemplate(AIToUGUICompiledPage page, AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return null;
            }

            AIToUGUIElementTemplate template = null;
            if (!string.IsNullOrWhiteSpace(node.TemplateId))
            {
                template = page?.ElementLibrary != null
                    ? page.ElementLibrary.ResolveTemplateByTemplateId(node.TemplateId)
                    : null;
            }

            if (template == null && !string.IsNullOrWhiteSpace(node.ComponentFamily))
            {
                template = page?.ElementLibrary != null
                    ? page.ElementLibrary.ResolveTemplateByComponent(node.ComponentFamily, node.ComponentVariant)
                    : null;
            }

            if (template == null && !string.IsNullOrWhiteSpace(node.ElementId))
            {
                template = page?.ElementLibrary != null
                    ? page.ElementLibrary.ResolveTemplate(node.ElementId, node.VariantId)
                    : null;
            }

            return template;
        }

        private static bool HasCompositeIntent(AIToUGUICompiledNode node)
        {
            return node != null &&
                   (!string.IsNullOrWhiteSpace(node.ComponentFamily) ||
                    (!string.IsNullOrWhiteSpace(node.TemplateId) &&
                     !AIToUGUIElementContractUtility.IsPrimitiveElement(node.ElementId)));
        }

        private static string InferComponentFamily(AIToUGUICompiledNode node, XElement element)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(node.ElementId))
            {
                return node.ElementId;
            }

            var semantic = BuildSemanticFingerprint(node, element);
            if (ContainsSemanticToken(semantic, "tab", "nav", "navigation", "sidebar-item", "menu-item"))
            {
                return "nav/tab";
            }

            if (ContainsSemanticToken(semantic, "window", "modal", "dialog", "sidebar", "split-panel", "frame"))
            {
                return "frame/window";
            }

            if (ContainsSemanticToken(semantic, "card", "item", "reward", "inventory", "slot"))
            {
                return "card/item";
            }

            if (ContainsSemanticToken(semantic, "header", "title-bar", "banner"))
            {
                return "header/section";
            }

            if (ContainsSemanticToken(semantic, "list-row", "row", "entry", "shop-item", "task-item"))
            {
                return "list/row";
            }

            var hasIconIntent =
                !string.IsNullOrWhiteSpace(GetAttribute(node, "data-ui-icon")) ||
                string.Equals(node.TagName, "img", StringComparison.OrdinalIgnoreCase);
            if (node.ControlType == AIToUGUIControlType.Button && hasIconIntent)
            {
                return "button/compound";
            }

            return string.Empty;
        }

        private static string InferComponentVariant(AIToUGUICompiledNode node, AIToUGUIElementTemplate template)
        {
            if (node == null)
            {
                return AIToUGUIElementContractUtility.DefaultComponentVariantId;
            }

            if (!string.IsNullOrWhiteSpace(template?.componentVariant))
            {
                return template.componentVariant;
            }

            var semantic = BuildSemanticFingerprint(node, null);
            return node.ComponentFamily switch
            {
                "frame/window" when ContainsSemanticToken(semantic, "modal", "dialog", "popup") => "modal",
                "frame/window" when ContainsSemanticToken(semantic, "split", "sidebar") => "split",
                "button/compound" when ContainsSemanticToken(semantic, "badge", "count", "notification") || !string.IsNullOrWhiteSpace(GetAttribute(node, "data-ui-value")) => "badge",
                "button/compound" when !string.IsNullOrWhiteSpace(GetAttribute(node, "data-ui-icon")) || string.Equals(node.TagName, "img", StringComparison.OrdinalIgnoreCase) => "icon",
                "card/item" when ContainsSemanticToken(semantic, "reward", "loot", "prize") => "reward",
                "card/item" when ContainsSemanticToken(semantic, "empty", "slot") => "empty-slot",
                "header/section" when ContainsSemanticToken(semantic, "hero", "banner") => "hero",
                "list/row" when ContainsSemanticToken(semantic, "shop", "store", "vendor") => "shop",
                "nav/tab" when ContainsSemanticToken(semantic, "sidebar", "vertical") => "sidebar",
                _ => AIToUGUIElementContractUtility.DefaultComponentVariantId
            };
        }

        private static string InferRenderStrategy(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return AIToUGUIElementContractUtility.ProceduralRenderStrategyId;
            }

            var explicitValue = AIToUGUIElementContractUtility.NormalizeRenderStrategy(node.RenderStrategy);
            if (!string.IsNullOrWhiteSpace(GetAttribute(node, "data-ui-render-strategy")))
            {
                return explicitValue;
            }

            var background = GetStyle(node, "background");
            var hasUrlBackground = !string.IsNullOrWhiteSpace(background) &&
                                   background.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasImageSource = !string.IsNullOrWhiteSpace(GetAttribute(node, "src"));
            var hasIconIntent = !string.IsNullOrWhiteSpace(GetAttribute(node, "data-ui-icon")) ||
                                !string.IsNullOrWhiteSpace(GetAttribute(node, "data-ui-asset-id"));
            var hasRasterEffect =
                !string.IsNullOrWhiteSpace(GetStyle(node, "backdrop-filter")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "filter")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "mask-image")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "mix-blend-mode"));

            if (hasRasterEffect)
            {
                return AIToUGUIElementContractUtility.RasterRenderStrategyId;
            }

            if (hasUrlBackground || hasImageSource || hasIconIntent)
            {
                return AIToUGUIElementContractUtility.HybridRenderStrategyId;
            }

            return explicitValue;
        }

        private static void PopulateAssetIntent(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (node == null)
            {
                return;
            }

            TryAddAssetRef(
                node,
                GetAttribute(node, "data-ui-asset-id"),
                GetAttribute(node, "data-ui-asset-type"),
                GetAttribute(node, "data-ui-asset-usage"),
                GetAttribute(node, "data-ui-asset-import"),
                ResolveExplicitAssetSource(node),
                string.Empty);

            var iconId = GetAttribute(node, "data-ui-icon");
            if (!string.IsNullOrWhiteSpace(iconId))
            {
                TryAddAssetRef(node, iconId, "icon", "icon-slot", "sprite", iconId, "Semantic icon request.");
                TryAddFidelityNote(node, "Replace semantic icon placeholder with a real Unity sprite.");
            }

            var imageSource = GetAttribute(node, "src");
            if (string.Equals(node.TagName, "img", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(imageSource))
            {
                TryAddAssetRef(
                    node,
                    BuildGeneratedAssetId(node, "image"),
                    ResolveImageAssetType(node),
                    "image-node",
                    "sprite",
                    imageSource,
                    "HTML image source.");
            }

            var background = GetStyle(node, "background");
            if (!string.IsNullOrWhiteSpace(background) &&
                background.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var assetType = string.Equals(node.RenderStrategy, AIToUGUIElementContractUtility.RasterRenderStrategyId, StringComparison.OrdinalIgnoreCase)
                    ? "snapshot"
                    : "ornament";
                var importMode = assetType == "snapshot" ? "read-only-overlay" : "sprite";
                TryAddAssetRef(
                    node,
                    BuildGeneratedAssetId(node, assetType),
                    assetType,
                    "background-effect",
                    importMode,
                    background,
                    "Background image or effect source.");
            }

            var fidelityNote = GetAttribute(node, "data-ui-fidelity-note");
            if (!string.IsNullOrWhiteSpace(fidelityNote))
            {
                TryAddFidelityNote(node, fidelityNote);
            }

            if (string.Equals(node.RenderStrategy, AIToUGUIElementContractUtility.RasterRenderStrategyId, StringComparison.OrdinalIgnoreCase))
            {
                TryAddFidelityNote(node, "This node requires raster or snapshot-backed local effect restoration.");
                if (node.IsRoot)
                {
                    page?.Warnings.Add($"Node '{node.Name}' requested raster render strategy at page root. Treat it as local overlays only and avoid full-page raster fallback.");
                }
            }

            if (!string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-color")))
            {
                TryAddFidelityNote(node, "Keep local glow/highlight treatment during Unity restoration.");
            }
        }

        private static string ResolveExplicitAssetSource(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            var imageSource = GetAttribute(node, "src");
            if (!string.IsNullOrWhiteSpace(imageSource))
            {
                return imageSource;
            }

            var background = GetStyle(node, "background");
            if (!string.IsNullOrWhiteSpace(background) &&
                background.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return background;
            }

            return string.Empty;
        }

        private static void TryAddAssetRef(
            AIToUGUICompiledNode node,
            string assetId,
            string assetType,
            string usage,
            string importMode,
            string source,
            string notes)
        {
            if (node == null)
            {
                return;
            }

            var normalizedAssetId = string.IsNullOrWhiteSpace(assetId)
                ? BuildGeneratedAssetId(node, assetType)
                : assetId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedAssetId))
            {
                return;
            }

            for (var i = 0; i < node.AssetRefs.Count; i++)
            {
                var existing = node.AssetRefs[i];
                if (existing != null &&
                    string.Equals(existing.assetId, normalizedAssetId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            node.AssetRefs.Add(new AIToUGUIAssetReference
            {
                assetId = normalizedAssetId,
                assetType = ParseAssetType(AIToUGUIElementContractUtility.NormalizeAssetTypeId(assetType)),
                usage = usage ?? string.Empty,
                importMode = ParseAssetImportMode(AIToUGUIElementContractUtility.NormalizeAssetImportModeId(importMode)),
                source = source ?? string.Empty,
                notes = notes ?? string.Empty,
                sliceBorder = ParseAssetSliceBorder(GetAttribute(node, "data-ui-asset-slice")),
                pixelsPerUnit = ParseAssetPixelsPerUnit(GetAttribute(node, "data-ui-asset-ppu")),
                preferredWidth = ParseAssetLength(GetAttribute(node, "data-ui-asset-width")),
                preferredHeight = ParseAssetLength(GetAttribute(node, "data-ui-asset-height")),
                tintPolicy = NormalizeAssetTintPolicy(GetAttribute(node, "data-ui-asset-tint")),
                atlasGroup = GetAttribute(node, "data-ui-asset-atlas") ?? string.Empty
            });
        }

        private static Vector4 ParseAssetSliceBorder(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Vector4.zero;
            }

            var parts = rawValue.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
            {
                return Vector4.zero;
            }

            return new Vector4(
                ParseAssetNumber(parts[0], 0f),
                ParseAssetNumber(parts[1], 0f),
                ParseAssetNumber(parts[2], 0f),
                ParseAssetNumber(parts[3], 0f));
        }

        private static float ParseAssetPixelsPerUnit(string rawValue)
        {
            var parsed = ParseAssetNumber(rawValue, 100f);
            return parsed > 0f ? parsed : 100f;
        }

        private static float ParseAssetLength(string rawValue)
        {
            return ParseAssetNumber(rawValue, 0f);
        }

        private static float ParseAssetNumber(string rawValue, float fallback)
        {
            return float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        private static string NormalizeAssetTintPolicy(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            var normalized = rawValue.Trim().ToLowerInvariant();
            return normalized switch
            {
                "none" => "none",
                "source" => "source",
                "white" => "none",
                _ => string.Empty
            };
        }

        private static void TryAddFidelityNote(AIToUGUICompiledNode node, string note)
        {
            if (node == null || string.IsNullOrWhiteSpace(note))
            {
                return;
            }

            for (var i = 0; i < node.FidelityNotes.Count; i++)
            {
                if (string.Equals(node.FidelityNotes[i], note, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            node.FidelityNotes.Add(note.Trim());
        }

        private static string BuildGeneratedAssetId(AIToUGUICompiledNode node, string suffix)
        {
            if (node == null)
            {
                return string.Empty;
            }

            var baseName = string.IsNullOrWhiteSpace(node.Name) ? node.TagName : node.Name;
            baseName = MultiWhitespaceRegex.Replace(baseName ?? "node", "-").Trim('-');
            return string.IsNullOrWhiteSpace(baseName)
                ? string.Empty
                : $"{baseName.ToLowerInvariant()}/{suffix?.Trim().ToLowerInvariant()}";
        }

        private static string ResolveImageAssetType(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return "icon";
            }

            var width = ParsePx(GetStyle(node, "width"));
            var height = ParsePx(GetStyle(node, "height"));
            if (width > 0f && width <= 64f && height > 0f && height <= 64f)
            {
                return "icon";
            }

            return string.Equals(node.RenderStrategy, AIToUGUIElementContractUtility.RasterRenderStrategyId, StringComparison.OrdinalIgnoreCase)
                ? "snapshot"
                : "ornament";
        }

        private static string BuildSemanticFingerprint(AIToUGUICompiledNode node, XElement element)
        {
            var parts = new List<string>();
            if (node != null)
            {
                parts.Add(node.Role);
                parts.Add(node.ElementId);
                parts.Add(node.TemplateId);
                parts.Add(node.Name);
                parts.Add(string.Join(" ", node.Classes));
            }

            if (element != null)
            {
                parts.Add(string.Join(" ", element.Attributes()));
            }

            return string.Join(" ", parts).ToLowerInvariant();
        }

        private static bool ContainsSemanticToken(string semantic, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(semantic) || tokens == null)
            {
                return false;
            }

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (!string.IsNullOrWhiteSpace(token) &&
                    semantic.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AutoAssignCompositeChildSlots(AIToUGUICompiledNode node, AIToUGUIElementTemplate template)
        {
            if (node == null || node.Children == null || node.Children.Count == 0 || string.IsNullOrWhiteSpace(node.ComponentFamily))
            {
                return;
            }

            if (template == null ||
                template.backingMode != AIToUGUIElementBackingMode.PrefabBacked ||
                (!node.HasExplicitTemplateId && !node.HasExplicitComponentFamily && !node.HasExplicitCompositeElement))
            {
                return;
            }

            var normalizedFamily = AIToUGUIElementContractUtility.NormalizeComponentFamily(template.componentFamily);
            if (string.IsNullOrWhiteSpace(normalizedFamily))
            {
                normalizedFamily = AIToUGUIElementContractUtility.NormalizeComponentFamily(node.ComponentFamily);
            }

            if (!AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(normalizedFamily))
            {
                return;
            }

            var textCount = 0;
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (child == null || !string.IsNullOrWhiteSpace(child.SlotId))
                {
                    continue;
                }

                var semantic = BuildSemanticFingerprint(child, null);
                var slotId = string.Empty;
                switch (normalizedFamily)
                {
                    case "frame/window":
                        if (ContainsSemanticToken(semantic, "header", "title-bar", "top", "caption"))
                        {
                            slotId = "Header";
                        }
                        else if (ContainsSemanticToken(semantic, "footer", "bottom", "actions"))
                        {
                            slotId = "Footer";
                        }
                        else if (ContainsSemanticToken(semantic, "deco", "decoration", "corner", "ornament", "flare"))
                        {
                            slotId = "Decoration";
                        }
                        else
                        {
                            slotId = "Content";
                        }

                        break;
                    case "button/compound":
                        if (ContainsSemanticToken(semantic, "badge", "count", "notification"))
                        {
                            slotId = "Badge";
                        }
                        else if (IsImageLikeChild(child) || ContainsSemanticToken(semantic, "icon"))
                        {
                            slotId = "Icon";
                        }
                        else if (IsTextLikeChild(child))
                        {
                            slotId = textCount == 0 ? "Label" : "SecondaryText";
                            textCount++;
                        }
                        else if (ContainsSemanticToken(semantic, "content", "body"))
                        {
                            slotId = "Content";
                        }
                        else
                        {
                            slotId = "Decoration";
                        }

                        break;
                    case "card/item":
                        if (ContainsSemanticToken(semantic, "badge", "count", "tag", "reward"))
                        {
                            slotId = "Badge";
                        }
                        else if (ContainsSemanticToken(semantic, "footer", "bottom", "actions"))
                        {
                            slotId = "Footer";
                        }
                        else if (IsImageLikeChild(child) || ContainsSemanticToken(semantic, "icon", "thumb", "avatar"))
                        {
                            slotId = "Icon";
                        }
                        else if (IsTextLikeChild(child))
                        {
                            slotId = textCount == 0 ? "PrimaryText" : "SecondaryText";
                            textCount++;
                        }
                        else if (ContainsSemanticToken(semantic, "content", "body", "desc", "description"))
                        {
                            slotId = "Content";
                        }
                        else
                        {
                            slotId = "Decoration";
                        }

                        break;
                    case "header/section":
                        if (child.ControlType == AIToUGUIControlType.Button ||
                            child.ControlType == AIToUGUIControlType.Toggle ||
                            child.ControlType == AIToUGUIControlType.Dropdown ||
                            ContainsSemanticToken(semantic, "action", "button", "cta"))
                        {
                            slotId = "Action";
                        }
                        else if (IsImageLikeChild(child) || ContainsSemanticToken(semantic, "icon"))
                        {
                            slotId = "Icon";
                        }
                        else if (IsTextLikeChild(child))
                        {
                            slotId = textCount == 0 ? "Title" : "Subtitle";
                            textCount++;
                        }
                        else
                        {
                            slotId = "Decoration";
                        }

                        break;
                    case "list/row":
                        if (ContainsSemanticToken(semantic, "badge", "count", "tag"))
                        {
                            slotId = "Badge";
                        }
                        else if (child.ControlType == AIToUGUIControlType.Button ||
                                 child.ControlType == AIToUGUIControlType.Toggle ||
                                 child.ControlType == AIToUGUIControlType.Dropdown ||
                                 ContainsSemanticToken(semantic, "trailing", "meta", "right", "arrow", "chevron"))
                        {
                            slotId = "Trailing";
                        }
                        else if (IsImageLikeChild(child) || ContainsSemanticToken(semantic, "leading", "icon", "avatar", "thumb"))
                        {
                            slotId = "Leading";
                        }
                        else
                        {
                            slotId = "Content";
                        }

                        break;
                    case "nav/tab":
                        if (ContainsSemanticToken(semantic, "indicator", "underline", "active-line"))
                        {
                            slotId = "Indicator";
                        }
                        else if (ContainsSemanticToken(semantic, "badge", "count", "notification"))
                        {
                            slotId = "Badge";
                        }
                        else if (IsImageLikeChild(child) || ContainsSemanticToken(semantic, "icon"))
                        {
                            slotId = "Icon";
                        }
                        else if (IsTextLikeChild(child))
                        {
                            slotId = "Label";
                        }
                        else
                        {
                            slotId = "Decoration";
                        }

                        break;
                }

                if (string.IsNullOrWhiteSpace(slotId))
                {
                    continue;
                }

                child.SlotId = slotId;
                child.Attributes["data-ui-slot"] = slotId;
            }
        }

        private static bool IsTextLikeChild(AIToUGUICompiledNode child)
        {
            return child != null &&
                   (child.ControlType == AIToUGUIControlType.Text || !string.IsNullOrWhiteSpace(child.Text));
        }

        private static bool IsImageLikeChild(AIToUGUICompiledNode child)
        {
            if (child == null)
            {
                return false;
            }

            if (child.ControlType == AIToUGUIControlType.Image ||
                string.Equals(child.TagName, "img", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (child.AssetRefs == null)
            {
                return false;
            }

            for (var i = 0; i < child.AssetRefs.Count; i++)
            {
                var assetRef = child.AssetRefs[i];
                if (assetRef != null &&
                    (assetRef.assetType == AIToUGUIAssetType.Icon || assetRef.assetType == AIToUGUIAssetType.Ornament))
                {
                    return true;
                }
            }

            return false;
        }

        private static AIToUGUIAssetType ParseAssetType(string assetType)
        {
            return assetType switch
            {
                nameof(AIToUGUIAssetType.Ornament) => AIToUGUIAssetType.Ornament,
                nameof(AIToUGUIAssetType.Snapshot) => AIToUGUIAssetType.Snapshot,
                nameof(AIToUGUIAssetType.Frame) => AIToUGUIAssetType.Frame,
                nameof(AIToUGUIAssetType.Background) => AIToUGUIAssetType.Background,
                _ => AIToUGUIAssetType.Icon
            };
        }

        private static AIToUGUIAssetImportMode ParseAssetImportMode(string importMode)
        {
            return importMode switch
            {
                nameof(AIToUGUIAssetImportMode.Sprite) => AIToUGUIAssetImportMode.Sprite,
                nameof(AIToUGUIAssetImportMode.NineSlice) => AIToUGUIAssetImportMode.NineSlice,
                nameof(AIToUGUIAssetImportMode.Tile) => AIToUGUIAssetImportMode.Tile,
                nameof(AIToUGUIAssetImportMode.ReadOnlyOverlay) => AIToUGUIAssetImportMode.ReadOnlyOverlay,
                _ => AIToUGUIAssetImportMode.Auto
            };
        }

        private static float ParsePx(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0f;
            }

            var match = NumberRegex.Match(value);
            if (!match.Success)
            {
                return 0f;
            }

            return float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private static List<AIToUGUICssRule> ParseRules(List<string> stylesheets, Dictionary<string, string> tokens, List<string> warnings)
        {
            var rules = new List<AIToUGUICssRule>();
            if (stylesheets == null)
            {
                return rules;
            }

            for (var i = 0; i < stylesheets.Count; i++)
            {
                var sheet = stylesheets[i];
                if (string.IsNullOrWhiteSpace(sheet))
                {
                    continue;
                }

                foreach (Match match in RuleRegex.Matches(sheet))
                {
                    var selector = match.Groups["selector"].Value.Trim();
                    var declarations = ParseDeclarationBlock(match.Groups["body"].Value);
                    if (string.IsNullOrWhiteSpace(selector) || declarations.Count == 0)
                    {
                        continue;
                    }

                    if (selector.Equals(":root", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var declaration in declarations)
                        {
                            if (declaration.Key.StartsWith("--", StringComparison.Ordinal))
                            {
                                tokens[declaration.Key] = declaration.Value;
                            }
                            else
                            {
                                warnings?.Add($":root 中的属性 {declaration.Key} 当前不会直接参与烘焙，仅 token 生效。");
                            }
                        }

                        continue;
                    }

                    if (!IsSupportedSelector(selector))
                    {
                        warnings?.Add($"选择器 {selector} 当前不受支持，已忽略。仅支持 :root、.class 和 [data-ui-role=\"...\"]。");
                        continue;
                    }

                    var rule = new AIToUGUICssRule { Selector = selector };
                    foreach (var declaration in declarations)
                    {
                        rule.Declarations[declaration.Key] = ReplaceTokens(declaration.Value, tokens);
                    }

                    rules.Add(rule);
                }
            }

            return rules;
        }

        private static Dictionary<string, string> ParseDeclarationBlock(string block)
        {
            var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(block))
            {
                return declarations;
            }

            foreach (Match match in DeclarationRegex.Matches(block))
            {
                var key = match.Groups["name"].Value.Trim().ToLowerInvariant();
                var value = match.Groups["value"].Value.Trim();
                declarations[key] = value;
            }

            return declarations;
        }

        private static XElement ParseHtml(string html, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var normalized = html.Replace("\r", string.Empty);
            normalized = Regex.Replace(normalized, "<!DOCTYPE.*?>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            normalized = VoidTagRegex.Replace(normalized, "<$1$2 />");
            normalized = Regex.Replace(normalized, "&(?!#?\\w+;)", "&amp;");

            try
            {
                var document = XDocument.Parse($"<Root>{normalized}</Root>", LoadOptions.PreserveWhitespace);
                var explicitRoot = document.Root?
                    .Descendants()
                    .FirstOrDefault(element => element.Attributes()
                        .Any(attribute => string.Equals(
                            NormalizeAttributeName(attribute.Name.LocalName),
                            "data-ui-page",
                            StringComparison.OrdinalIgnoreCase)));
                if (explicitRoot != null)
                {
                    return explicitRoot;
                }

                var body = document.Root?
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase));
                if (body != null)
                {
                    return body.Elements().FirstOrDefault();
                }

                var htmlElement = document.Root?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase));
                if (htmlElement != null)
                {
                    return htmlElement.Elements().FirstOrDefault();
                }

                return document.Root?.Elements().FirstOrDefault();
            }
            catch (Exception exception)
            {
                warnings?.Add($"HTML 解析失败: {exception.Message}");
                return null;
            }
        }

        private static List<string> ExtractStyleBlocks(string html, out string strippedHtml)
        {
            var styles = new List<string>();
            strippedHtml = html ?? string.Empty;
            foreach (Match match in StyleBlockRegex.Matches(strippedHtml))
            {
                styles.Add(match.Groups[1].Value);
            }

            strippedHtml = StyleBlockRegex.Replace(strippedHtml, string.Empty);
            return styles;
        }

        private static IEnumerable<string> LoadSiteStylesFromManifest(AIToUGUISourceContext sourceContext, List<string> warnings)
        {
            if (sourceContext == null || sourceContext.manifest == null)
            {
                return Array.Empty<string>();
            }

            var relativePaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(sourceContext.manifest.themeCss))
            {
                relativePaths.Add(sourceContext.manifest.themeCss);
            }

            if (sourceContext.manifest.sharedStyles != null)
            {
                relativePaths.AddRange(sourceContext.manifest.sharedStyles.Where(path => !string.IsNullOrWhiteSpace(path)));
            }

            return LoadRelativeStyleFiles(sourceContext.sourceRootFolder, relativePaths, warnings);
        }

        private static IEnumerable<string> LoadPageStylesFromManifest(AIToUGUISourceContext sourceContext, AIToUGUIPageDefinition page, List<string> warnings)
        {
            if (sourceContext == null || sourceContext.manifest == null || page == null || (page.localStyleSheets != null && page.localStyleSheets.Count > 0))
            {
                return Array.Empty<string>();
            }

            if (sourceContext.manifest.pages == null)
            {
                return Array.Empty<string>();
            }

            var pageManifest = sourceContext.manifest.pages.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.pageId, page.pageId, StringComparison.OrdinalIgnoreCase));
            if (pageManifest == null || pageManifest.localStyles == null || pageManifest.localStyles.Length == 0)
            {
                return Array.Empty<string>();
            }

            return LoadRelativeStyleFiles(sourceContext.sourceRootFolder, pageManifest.localStyles, warnings);
        }

        private static IEnumerable<string> LoadLinkedStylesheets(string html, AIToUGUISourceContext sourceContext, AIToUGUISiteDefinition site, AIToUGUIPageDefinition page, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return Array.Empty<string>();
            }

            var pageAssetPath = ResolvePageAssetPath(sourceContext, site, page);
            if (string.IsNullOrWhiteSpace(pageAssetPath))
            {
                return Array.Empty<string>();
            }

            var pageDirectory = Path.GetDirectoryName(pageAssetPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(pageDirectory))
            {
                return Array.Empty<string>();
            }

            var styles = new List<string>();
            foreach (Match match in LinkStyleRegex.Matches(html))
            {
                var href = match.Groups["href"].Value?.Trim();
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                var assetPath = ResolveRelativeAssetPath(pageDirectory, href);
                var text = ReadAssetText(assetPath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    styles.Add(text);
                }
                else
                {
                    warnings?.Add($"样式文件 {href} 未能从页面链接中读取，已跳过。");
                }
            }

            return styles;
        }

        private static IEnumerable<string> LoadRelativeStyleFiles(string rootFolder, IEnumerable<string> relativePaths, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || relativePaths == null)
            {
                return Array.Empty<string>();
            }

            var styles = new List<string>();
            foreach (var relativePath in relativePaths)
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var assetPath = ResolveRelativeAssetPath(rootFolder, relativePath);
                var text = ReadAssetText(assetPath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    styles.Add(text);
                }
                else
                {
                    warnings?.Add($"样式文件 {relativePath} 未能从站点包中读取，已跳过。");
                }
            }

            return styles;
        }

        private static string ResolvePageAssetPath(AIToUGUISourceContext sourceContext, AIToUGUISiteDefinition site, AIToUGUIPageDefinition page)
        {
            if (page != null && page.htmlAsset != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(page.htmlAsset);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    return assetPath.Replace("\\", "/");
                }
            }

            var sourceRootFolder = !string.IsNullOrWhiteSpace(sourceContext?.sourceRootFolder)
                ? sourceContext.sourceRootFolder
                : site != null ? site.sourceRootFolder : string.Empty;
            if (page == null || string.IsNullOrWhiteSpace(sourceRootFolder) || string.IsNullOrWhiteSpace(page.sourceRelativePath))
            {
                return string.Empty;
            }

            return ResolveRelativeAssetPath(sourceRootFolder, page.sourceRelativePath);
        }

        private static string ResolveRelativeAssetPath(string baseAssetFolder, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(baseAssetFolder) || string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            var normalizedBase = baseAssetFolder.Replace("\\", "/");
            var combined = Path.GetFullPath(Path.Combine(normalizedBase, relativePath));
            var projectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            combined = combined.Replace("\\", "/");
            projectRoot = projectRoot.Replace("\\", "/").TrimEnd('/');
            if (!combined.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                combined = Path.GetFullPath(Path.Combine(projectRoot, normalizedBase, relativePath)).Replace("\\", "/");
            }

            if (!combined.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return combined.Substring(projectRoot.Length + 1).Replace("\\", "/");
        }

        private static string ReadAssetText(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset != null)
            {
                return textAsset.text ?? string.Empty;
            }

            var fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, assetPath).Replace("\\", "/");
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        }

        private static Dictionary<string, string> BuildThemeTokens(AIToUGUIThemeDefinition theme)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (theme == null)
            {
                return tokens;
            }

            tokens["--page-bg"] = ToHtmlColor(theme.pageBackground);
            tokens["--panel-fill"] = ToHtmlColor(theme.panelFill);
            tokens["--card-fill"] = ToHtmlColor(theme.cardFill);
            tokens["--button-fill"] = ToHtmlColor(theme.buttonFill);
            tokens["--accent"] = ToHtmlColor(theme.accentColor);
            tokens["--text-primary"] = ToHtmlColor(theme.textPrimary);
            tokens["--text-secondary"] = ToHtmlColor(theme.textSecondary);
            tokens["--outline"] = ToHtmlColor(theme.outlineColor);
            tokens["--shadow"] = ToHtmlColor(theme.shadowColor);
            tokens["--glow"] = ToHtmlColor(theme.glowColor);
            tokens["--panel-radius"] = $"{theme.panelRadius}px";
            tokens["--card-radius"] = $"{theme.cardRadius}px";
            tokens["--button-radius"] = $"{theme.buttonRadius}px";
            tokens["--page-padding"] = $"{theme.pagePadding}px";

            if (theme.tokens != null)
            {
                for (var i = 0; i < theme.tokens.Count; i++)
                {
                    var token = theme.tokens[i];
                    if (token == null || string.IsNullOrWhiteSpace(token.tokenId))
                    {
                        continue;
                    }

                    tokens[token.tokenId.StartsWith("--", StringComparison.Ordinal) ? token.tokenId : $"--{token.tokenId}"] = token.value;
                }
            }

            return tokens;
        }

        private static bool Matches(string selector, AIToUGUICompiledNode node)
        {
            if (string.IsNullOrWhiteSpace(selector) || node == null)
            {
                return false;
            }

            selector = selector.Trim();
            if (selector.StartsWith(".", StringComparison.Ordinal))
            {
                var className = selector.Substring(1);
                return node.Classes.Contains(className);
            }

            var roleMatch = RoleSelectorRegex.Match(selector);
            if (roleMatch.Success)
            {
                return string.Equals(node.Role, roleMatch.Groups["value"].Value.Trim(), StringComparison.Ordinal);
            }

            if (IsRootTagSelector(selector))
            {
                return node.IsRoot;
            }

            return false;
        }

        private static bool IsSupportedSelector(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return false;
            }

            selector = selector.Trim();
            return selector.StartsWith(".", StringComparison.Ordinal) ||
                   RoleSelectorRegex.IsMatch(selector) ||
                   IsRootTagSelector(selector);
        }

        private static bool IsRootTagSelector(string selector)
        {
            return string.Equals(selector, "body", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selector, "html", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceTokens(string value, Dictionary<string, string> tokens)
        {
            if (string.IsNullOrWhiteSpace(value) || tokens == null || tokens.Count == 0)
            {
                return value;
            }

            return VarRegex.Replace(value, match =>
            {
                var tokenId = match.Groups["token"].Value;
                return tokens.TryGetValue(tokenId, out var tokenValue) ? tokenValue : match.Value;
            });
        }

        private static void DeduplicateWarnings(List<string> warnings)
        {
            if (warnings == null || warnings.Count <= 1)
            {
                return;
            }

            var ordered = warnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct()
                .ToList();
            warnings.Clear();
            warnings.AddRange(ordered);
        }

        private static string NormalizeAttributeName(string name)
        {
            var normalized = name.Trim();
            switch (normalized)
            {
                case "data-u-type":
                    return "data-ui-type";
                case "data-u-name":
                    return "data-ui-name";
                case "data-u-role":
                    return "data-ui-role";
                case "data-u-element":
                    return "data-ui-element";
                case "data-u-template-size":
                    return "data-ui-template-size";
                case "data-u-slot":
                    return "data-ui-slot";
                case "data-u-container":
                    return "data-ui-container";
                case "data-u-template":
                    return "data-ui-template";
                case "data-u-motion":
                    return "data-ui-motion";
                case "data-u-glow":
                    return "data-ui-glow";
                case "data-u-glow-color":
                    return "data-ui-glow-color";
                case "data-u-glow-blur":
                    return "data-ui-glow-blur";
                case "data-u-glow-power":
                    return "data-ui-glow-power";
                case "data-u-icon":
                    return "data-ui-icon";
                case "data-u-icon-size":
                    return "data-ui-icon-size";
                case "data-u-value":
                    return "data-ui-value";
                case "data-u-dir":
                    return "data-ui-dir";
                default:
                    return normalized;
            }
        }

        private static string GetAttribute(AIToUGUICompiledNode node, string key)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return node.Attributes.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static bool ShouldUseTemplateSize(AIToUGUICompiledNode node)
        {
            var rawValue = GetAttribute(node, "data-ui-template-size");
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetStyle(AIToUGUICompiledNode node, string key, out string value)
        {
            if (node != null && node.Styles.TryGetValue(key, out value))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static string GetStyle(AIToUGUICompiledNode node, string key)
        {
            return TryGetStyle(node, key, out var value) ? value : string.Empty;
        }

        private static void ApplyInheritedTextStyles(AIToUGUICompiledNode parentNode, AIToUGUICompiledNode node)
        {
            if (parentNode == null || node == null)
            {
                return;
            }

            for (var i = 0; i < InheritableTextStyleKeys.Length; i++)
            {
                var key = InheritableTextStyleKeys[i];
                if (!string.IsNullOrWhiteSpace(GetStyle(node, key)))
                {
                    continue;
                }

                if (TryGetStyle(parentNode, key, out var inheritedValue) && !string.IsNullOrWhiteSpace(inheritedValue))
                {
                    node.Styles[key] = inheritedValue;
                }
            }
        }

        private static string ExtractDirectInnerText(XElement element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            var textParts = element
                .Nodes()
                .OfType<XText>()
                .Select(textNode => textNode.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            if (textParts.Length == 0)
            {
                return string.Empty;
            }

            var joined = string.Join(" ", textParts);
            return NormalizeText(WebUtility.HtmlDecode(joined));
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return MultiWhitespaceRegex.Replace(value, " ").Trim();
        }

        private static bool ShouldBindText(AIToUGUICompiledNode node, string tagName, string directText, XElement element)
        {
            if (node == null || string.IsNullOrWhiteSpace(directText))
            {
                return false;
            }

            if (node.ControlType == AIToUGUIControlType.Text ||
                node.ControlType == AIToUGUIControlType.Button ||
                node.ControlType == AIToUGUIControlType.Input ||
                node.ControlType == AIToUGUIControlType.Toggle ||
                node.ControlType == AIToUGUIControlType.Dropdown ||
                node.ControlType == AIToUGUIControlType.Progress ||
                tagName == "span")
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(node.Role) &&
                (node.Role.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                 node.Role.StartsWith("chip/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return element == null || !element.Elements().Any();
        }

        private static string ApplyTextTransform(string text, string transform)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return transform switch
            {
                "uppercase" => text.ToUpperInvariant(),
                "lowercase" => text.ToLowerInvariant(),
                _ => text
            };
        }

        private static bool TryParseRotationDegrees(string raw, out float degrees)
        {
            degrees = 0f;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var trimmed = raw.Trim();
            var match = RotateTransformRegex.Match(trimmed);
            if (match.Success && float.TryParse(match.Groups["angle"].Value, out degrees))
            {
                return true;
            }

            var normalized = trimmed.EndsWith("deg", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(0, trimmed.Length - 3)
                : trimmed;
            return float.TryParse(normalized, out degrees);
        }

        private static string ResolveLoopMotionPresetId(string animationName, string direction)
        {
            var normalizedName = animationName?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalizedName == "rotate")
            {
                return string.Equals(direction?.Trim(), "reverse", StringComparison.OrdinalIgnoreCase)
                    ? "loop/rotate-slow-reverse"
                    : "loop/rotate-slow";
            }

            if (normalizedName == "float")
            {
                return "loop/float-soft";
            }

            if (normalizedName == "pulse")
            {
                return "loop/pulse-soft";
            }

            return string.Empty;
        }

        private static bool TryParseAnimationDelay(string raw, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var trimmed = raw.Trim();
            if (trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return float.TryParse(trimmed, out seconds);
        }

        private static bool RequiresExplicitName(AIToUGUICompiledNode node, bool isRoot)
        {
            if (node == null)
            {
                return false;
            }

            if (isRoot)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(node.SlotId))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(node.ContainerId) ||
                !string.IsNullOrWhiteSpace(node.TemplateId))
            {
                return true;
            }

            switch (node.ControlType)
            {
                case AIToUGUIControlType.Button:
                case AIToUGUIControlType.Input:
                case AIToUGUIControlType.Scroll:
                case AIToUGUIControlType.Scrollbar:
                case AIToUGUIControlType.Toggle:
                case AIToUGUIControlType.Slider:
                case AIToUGUIControlType.Dropdown:
                case AIToUGUIControlType.Progress:
                    return true;
                default:
                    return false;
            }
        }

        private static void ValidateDuplicateExportNames(AIToUGUICompiledNode root, List<string> errors)
        {
            if (root == null || errors == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            CollectDuplicateExportNames(root, seen, duplicates);
            foreach (var duplicate in duplicates)
            {
                errors.Add($"页面存在重复的导出节点名 '{duplicate}'。");
            }
        }

        private static void ValidateDuplicateSemanticIds(AIToUGUICompiledNode root, List<string> errors)
        {
            if (root == null || errors == null)
            {
                return;
            }

            ValidateDuplicateSemanticIds(root, errors, node => node.SlotId, "data-ui-slot");
            ValidateDuplicateSemanticIds(root, errors, node => node.ContainerId, "data-ui-container");
            ValidateDuplicateSemanticIds(root, errors, node => node.TemplateId, "data-ui-template");
        }

        private static void ValidateDuplicateSemanticIds(
            AIToUGUICompiledNode root,
            List<string> errors,
            Func<AIToUGUICompiledNode, string> selector,
            string attributeName)
        {
            if (root == null || errors == null || selector == null || string.IsNullOrWhiteSpace(attributeName))
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            CollectDuplicateSemanticIds(root, selector, seen, duplicates);
            foreach (var duplicate in duplicates)
            {
                errors.Add($"页面存在重复的 {attributeName} '{duplicate}'。");
            }
        }

        private static void CollectDuplicateSemanticIds(
            AIToUGUICompiledNode node,
            Func<AIToUGUICompiledNode, string> selector,
            HashSet<string> seen,
            HashSet<string> duplicates)
        {
            if (node == null || selector == null)
            {
                return;
            }

            var value = selector(node);
            if (!string.IsNullOrWhiteSpace(value) && !seen.Add(value))
            {
                duplicates.Add(value);
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                CollectDuplicateSemanticIds(node.Children[i], selector, seen, duplicates);
            }
        }

        private static void CollectDuplicateExportNames(AIToUGUICompiledNode node, HashSet<string> seen, HashSet<string> duplicates)
        {
            if (node == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(node.Name) && !seen.Add(node.Name))
            {
                duplicates.Add(node.Name);
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                CollectDuplicateExportNames(node.Children[i], seen, duplicates);
            }
        }

        private static AIToUGUIControlType ParseControlType(string explicitType, string tagName)
        {
            if (!string.IsNullOrWhiteSpace(explicitType) &&
                Enum.TryParse(explicitType, true, out AIToUGUIControlType parsedType))
            {
                return parsedType;
            }

            switch (tagName)
            {
                case "span":
                case "label":
                case "p":
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    return AIToUGUIControlType.Text;
                case "button":
                    return AIToUGUIControlType.Button;
                case "input":
                    return AIToUGUIControlType.Input;
                case "img":
                    return AIToUGUIControlType.Image;
                default:
                    return AIToUGUIControlType.Div;
            }
        }

        private static void NormalizeElementIdentity(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return;
            }

            if (!AIToUGUIElementContractUtility.TryNormalizeElementIdentity(node.ElementId, node.VariantId, out var elementId, out var variantId))
            {
                return;
            }

            node.ElementId = elementId;
            node.VariantId = variantId;
            node.Attributes["data-ui-element"] = node.ElementId;
            node.Attributes["data-ui-variant"] = node.VariantId;
        }

        private static void ValidatePrimitiveContract(AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (node == null || page == null)
            {
                return;
            }

            if (AIToUGUIElementContractUtility.RequiresExplicitElementMarker(node.ControlType) &&
                string.IsNullOrWhiteSpace(node.ElementId))
            {
                page.Errors.Add($"Node '{node.Name}' ({node.ControlType}) is missing required data-ui-element.");
                return;
            }

            if (!AIToUGUIElementContractUtility.IsPrimitiveElement(node.ElementId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.VariantId))
            {
                node.VariantId = AIToUGUIElementContractUtility.DefaultVariantId;
                node.Attributes["data-ui-variant"] = node.VariantId;
            }

            if (HasCompositeIntent(node))
            {
                return;
            }

            if (!SupportsPrimitiveAuthoredChildren(node))
            {
                page.Errors.Add($"Node '{node.Name}' ({node.ElementId}/{node.VariantId}) contains authored child structure that is not allowed for prefab-backed primitives.");
            }
        }

        private static void PromotePrimitiveTextChild(AIToUGUICompiledNode node)
        {
            if (node == null ||
                !string.IsNullOrWhiteSpace(node.Text) ||
                node.Children == null ||
                node.Children.Count != 1)
            {
                return;
            }

            switch (node.ElementId)
            {
                case "button":
                case "toggle":
                case "dropdown":
                case "progress":
                    var onlyChild = node.Children[0];
                    if (onlyChild != null &&
                        onlyChild.ControlType == AIToUGUIControlType.Text &&
                        onlyChild.Children.Count == 0)
                    {
                        node.Text = onlyChild.Text;
                        node.Children.Clear();
                    }

                    break;
            }
        }

        private static bool SupportsPrimitiveAuthoredChildren(AIToUGUICompiledNode node)
        {
            if (node == null || node.Children == null || node.Children.Count == 0)
            {
                return true;
            }

            switch (node.ElementId)
            {
                case "scrollview":
                    return true;
                case "button":
                case "toggle":
                case "dropdown":
                case "progress":
                    if (node.Children.Count > 1)
                    {
                        return false;
                    }

                    var onlyChild = node.Children[0];
                    return onlyChild.Children.Count == 0 &&
                           onlyChild.ControlType == AIToUGUIControlType.Text;
                default:
                    return false;
            }
        }

        private static string ToHtmlColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }
    }
}

#endif
