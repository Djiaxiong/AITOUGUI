#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Editor
{
    internal sealed class AIToUGUIParsedBundle : IDisposable
    {
        public AIToUGUICompiledBundleDefinition Definition;
        public CompiledSiteBundle BundleDto;
        public AIToUGUIThemeDefinition Theme;
        public readonly List<AIToUGUICompiledPage> Pages = new List<AIToUGUICompiledPage>();
        public readonly List<string> Warnings = new List<string>();

        public AIToUGUICompiledPage FindPage(string pageId)
        {
            for (var i = 0; i < Pages.Count; i++)
            {
                var page = Pages[i];
                if (page != null && string.Equals(page.PageId, pageId, StringComparison.Ordinal))
                {
                    return page;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (Theme != null)
            {
                UnityEngine.Object.DestroyImmediate(Theme);
                Theme = null;
            }
        }
    }

    internal static class AIToUGUIBundleParser
    {
        private static readonly Regex NumberRegex = new Regex("-?\\d+(?:\\.\\d+)?", RegexOptions.Compiled);

        public static AIToUGUIParsedBundle Parse(AIToUGUICompiledBundleDefinition definition)
        {
            var parsed = new AIToUGUIParsedBundle
            {
                Definition = definition
            };

            if (definition == null || definition.bundleJson == null || string.IsNullOrWhiteSpace(definition.bundleJson.text))
            {
                parsed.Warnings.Add("Compiled bundle JSON is missing.");
                return parsed;
            }

            try
            {
                parsed.BundleDto = JsonConvert.DeserializeObject<CompiledSiteBundle>(definition.bundleJson.text);
            }
            catch (Exception exception)
            {
                parsed.Warnings.Add($"Compiled bundle parse failed: {exception.Message}");
                return parsed;
            }

            if (parsed.BundleDto == null || parsed.BundleDto.site == null)
            {
                parsed.Warnings.Add("Compiled bundle JSON is empty or missing site metadata.");
                return parsed;
            }

            if (parsed.BundleDto.compiledWithForce)
            {
                parsed.Warnings.Add("Compiled bundle was produced with force-compile. Treat the generated assets as diagnostic only until contract errors are fixed.");
            }

            parsed.Theme = BuildTransientTheme(parsed.BundleDto.theme);
            var designResolution = new Vector2(
                Mathf.Max(1f, parsed.BundleDto.site.designWidth),
                Mathf.Max(1f, parsed.BundleDto.site.designHeight));

            if (parsed.BundleDto.pages == null)
            {
                return parsed;
            }

            for (var i = 0; i < parsed.BundleDto.pages.Length; i++)
            {
                var pageDto = parsed.BundleDto.pages[i];
                if (pageDto == null)
                {
                    continue;
                }

                var summary = FindSummary(definition, pageDto.pageId);
                var page = new AIToUGUICompiledPage
                {
                    SourceBundle = definition,
                    SourceBundleJsonAssetPath = AssetDatabase.GetAssetPath(definition.bundleJson),
                    SiteId = parsed.BundleDto.site.siteId,
                    PageId = pageDto.pageId,
                    SourcePageRelativePath = pageDto.sourceRelativePath ?? string.Empty,
                    RuntimePageId = AIToUGUIRuntimePageIdUtility.ResolveDefaultCompatibleRuntimePageId(
                        summary != null ? summary.runtimePageId : pageDto.runtimePageId,
                        parsed.BundleDto.site.siteId,
                        pageDto.pageId),
                    DisplayName = string.IsNullOrWhiteSpace(pageDto.displayName) ? pageDto.pageId : pageDto.displayName,
                    PrefabName = string.IsNullOrWhiteSpace(pageDto.prefabName) ? "CompiledPage" : pageDto.prefabName,
                    PanelComponentTypeName = summary != null ? summary.panelComponentTypeName : string.Empty,
                    AttachPanelComponent = summary != null && summary.attachPanelComponent,
                    LogicalPath = string.IsNullOrWhiteSpace(pageDto.logicalPath)
                        ? $"UI/Generated/{parsed.BundleDto.site.siteId}/{(string.IsNullOrWhiteSpace(pageDto.prefabName) ? "CompiledPage" : pageDto.prefabName)}"
                        : pageDto.logicalPath,
                    PrefabOutputRoot = AIToUGUIGeneratedAssetPaths.ResolvePrefabOutputRoot(
                        parsed.BundleDto.site.siteId,
                        string.IsNullOrWhiteSpace(parsed.BundleDto.site.prefabOutputRoot)
                            ? definition.prefabOutputRoot
                            : parsed.BundleDto.site.prefabOutputRoot),
                    MetadataOutputRoot = AIToUGUIGeneratedAssetPaths.ResolveMetadataOutputRoot(
                        parsed.BundleDto.site.siteId,
                        string.IsNullOrWhiteSpace(parsed.BundleDto.site.metadataOutputRoot)
                            ? definition.metadataOutputRoot
                            : parsed.BundleDto.site.metadataOutputRoot),
                    TargetLayer = summary != null ? summary.targetLayer : ParseLayer(pageDto.targetLayer),
                    DesignResolution = designResolution,
                    Theme = parsed.Theme,
                    KeepExportNodeMarkers = definition == null || definition.keepExportNodeMarkers,
                    KeepAssetBindingManifests = definition != null && definition.keepAssetBindingManifests,
                    UseOverflowMaskHosts = definition == null || definition.useOverflowMaskHosts
                };

                page.Root = ConvertNode(pageDto.root, true);
                if (page.Root == null)
                {
                    page.Warnings.Add($"Page '{page.PageId}' has no valid root node.");
                }

                parsed.Pages.Add(page);
            }

            return parsed;
        }

        private static AIToUGUICompiledBundlePageSummary FindSummary(AIToUGUICompiledBundleDefinition definition, string pageId)
        {
            if (definition == null || definition.pages == null || string.IsNullOrWhiteSpace(pageId))
            {
                return null;
            }

            for (var i = 0; i < definition.pages.Count; i++)
            {
                var page = definition.pages[i];
                if (page != null && string.Equals(page.pageId, pageId, StringComparison.Ordinal))
                {
                    return page;
                }
            }

            return null;
        }

        private static AIToUGUICompiledNode ConvertNode(CompiledNodeDto dto, bool isRoot)
        {
            if (dto == null)
            {
                return null;
            }

            var usesCompositeElementIdentity =
                string.IsNullOrWhiteSpace(dto.componentFamily) &&
                AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(dto.elementId);
            var resolvedComponentFamily = string.IsNullOrWhiteSpace(dto.componentFamily)
                ? (usesCompositeElementIdentity ? dto.elementId : string.Empty)
                : dto.componentFamily;

            var node = new AIToUGUICompiledNode
            {
                IsRoot = isRoot,
                Name = string.IsNullOrWhiteSpace(dto.name) ? (isRoot ? "CompiledPageRoot" : "Node") : dto.name,
                TagName = string.IsNullOrWhiteSpace(dto.tag) ? "div" : dto.tag.ToLowerInvariant(),
                Role = dto.role ?? string.Empty,
                ElementId = dto.elementId ?? string.Empty,
                VariantId = dto.variantId ?? string.Empty,
                ShapeId = dto.shapeId ?? string.Empty,
                FrameId = dto.frameId ?? string.Empty,
                SlotId = dto.slotId ?? string.Empty,
                ContainerId = dto.containerId ?? string.Empty,
                TemplateId = dto.templateId ?? string.Empty,
                ComponentFamily = AIToUGUIElementContractUtility.NormalizeComponentFamily(resolvedComponentFamily),
                ComponentVariant = string.IsNullOrWhiteSpace(resolvedComponentFamily)
                    ? string.Empty
                    : AIToUGUIElementContractUtility.NormalizeComponentVariantId(
                        string.IsNullOrWhiteSpace(dto.componentVariant) ? dto.variantId : dto.componentVariant),
                RenderStrategy = AIToUGUIElementContractUtility.NormalizeRenderStrategy(dto.renderStrategy),
                MotionId = !string.IsNullOrWhiteSpace(dto.motionId)
                    ? dto.motionId
                    : dto.motion != null ? dto.motion.presetId : string.Empty,
                Text = dto.text ?? string.Empty,
                StabilityLevel = string.IsNullOrWhiteSpace(dto.stabilityLevel) ? "suggested" : dto.stabilityLevel.Trim().ToLowerInvariant(),
                AbsoluteRect = dto.absoluteRect != null
                    ? new AIToUGUIMeasuredRect
                    {
                        X = dto.absoluteRect.x,
                        Y = dto.absoluteRect.y,
                        Width = dto.absoluteRect.width,
                        Height = dto.absoluteRect.height,
                        Measured = dto.absoluteRect.measured,
                        Source = dto.absoluteRect.source ?? string.Empty,
                    }
                    : default,
                ControlType = ParseControlType(dto.controlType)
            };

            NormalizeElementIdentity(node);
            node.HasExplicitTemplateId = dto.hasExplicitTemplateId || !string.IsNullOrWhiteSpace(node.TemplateId);
            node.HasExplicitComponentFamily = dto.hasExplicitComponentFamily;
            node.HasExplicitCompositeElement = dto.hasExplicitCompositeElement || usesCompositeElementIdentity;
            if (node.ControlType == AIToUGUIControlType.Auto || node.ControlType == AIToUGUIControlType.Div)
            {
                var primitiveType = AIToUGUIElementContractUtility.InferPrimitiveControlType(node.ElementId);
                if (primitiveType != AIToUGUIControlType.Div)
                {
                    node.ControlType = primitiveType;
                }
            }

            if (dto.classes != null)
            {
                node.Classes.AddRange(dto.classes);
            }

            WriteAttribute(node, "data-ui-name", node.Name);
            WriteAttribute(node, "data-ui-role", node.Role);
            WriteAttribute(node, "data-ui-element", node.ElementId);
            WriteAttribute(node, "data-ui-variant", node.VariantId);
            WriteAttribute(node, "data-ui-shape", node.ShapeId);
            WriteAttribute(node, "data-ui-frame", node.FrameId);
            WriteAttribute(node, "data-ui-slot", node.SlotId);
            WriteAttribute(node, "data-ui-container", node.ContainerId);
            WriteAttribute(node, "data-ui-template", node.TemplateId);
            WriteAttribute(node, "data-ui-component-family", node.ComponentFamily);
            WriteAttribute(node, "data-ui-component-variant", node.ComponentVariant);
            WriteAttribute(node, "data-ui-render-strategy", node.RenderStrategy);
            WriteAttribute(node, "data-ui-motion", node.MotionId);
            WriteAttribute(node, "data-ui-loop-motion", dto.motion != null ? dto.motion.loopPresetId : string.Empty);

            if (dto.assetRefs != null)
            {
                for (var i = 0; i < dto.assetRefs.Length; i++)
                {
                    var assetRef = dto.assetRefs[i];
                    if (assetRef == null || string.IsNullOrWhiteSpace(assetRef.assetId))
                    {
                        continue;
                    }

                    node.AssetRefs.Add(new AIToUGUIAssetReference
                    {
                        assetId = assetRef.assetId.Trim(),
                        assetType = ParseAssetType(assetRef.assetType),
                        usage = assetRef.usage ?? string.Empty,
                        importMode = ParseAssetImportMode(assetRef.importMode),
                        source = assetRef.source ?? string.Empty,
                        notes = assetRef.notes ?? string.Empty,
                        logicalAssetPath = assetRef.logicalAssetPath ?? string.Empty,
                        sliceBorder = assetRef.sliceBorder,
                        pixelsPerUnit = assetRef.pixelsPerUnit > 0f ? assetRef.pixelsPerUnit : 100f,
                        preferredWidth = assetRef.preferredWidth,
                        preferredHeight = assetRef.preferredHeight,
                        tintPolicy = assetRef.tintPolicy ?? string.Empty,
                        atlasGroup = assetRef.atlasGroup ?? string.Empty
                    });
                }
            }

            if (dto.fidelityNotes != null)
            {
                for (var i = 0; i < dto.fidelityNotes.Length; i++)
                {
                    var note = dto.fidelityNotes[i];
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        node.FidelityNotes.Add(note.Trim());
                    }
                }
            }

            if (dto.attributes != null)
            {
                for (var i = 0; i < dto.attributes.Length; i++)
                {
                    var attribute = dto.attributes[i];
                    if (attribute == null || string.IsNullOrWhiteSpace(attribute.name))
                    {
                        continue;
                    }

                    WriteAttribute(node, attribute.name, attribute.value);
                }
            }

            ApplyLayout(node, dto.layout);
            ApplyTextStyle(node, dto.textStyle);
            ApplyVisual(node, dto.visual);

            if (!string.IsNullOrWhiteSpace(node.MotionId))
            {
                node.Styles["-ai-motion"] = node.MotionId;
            }

            if (dto.motion != null)
            {
                ApplyMotion(node, dto.motion);
            }

            if (dto.children != null)
            {
                for (var i = 0; i < dto.children.Length; i++)
                {
                    var child = ConvertNode(dto.children[i], false);
                    if (child != null)
                    {
                        node.Children.Add(child);
                    }
                }
            }

            return node;
        }

        private static void ApplyLayout(AIToUGUICompiledNode node, CompiledLayoutDto layout)
        {
            if (layout == null)
            {
                return;
            }

            WriteStyle(node, "display", layout.display);
            WriteStyle(node, "position", layout.position);
            WriteStyle(node, "left", layout.left);
            WriteStyle(node, "right", layout.right);
            WriteStyle(node, "top", layout.top);
            WriteStyle(node, "bottom", layout.bottom);
            WriteStyle(node, "width", layout.width);
            WriteStyle(node, "height", layout.height);
            WriteStyle(node, "min-width", layout.minWidth);
            WriteStyle(node, "max-width", layout.maxWidth);
            WriteStyle(node, "min-height", layout.minHeight);
            WriteStyle(node, "max-height", layout.maxHeight);
            WriteStyle(node, "padding", layout.padding);
            WriteStyle(node, "margin", layout.margin);
            WriteStyle(node, "margin-left", layout.marginLeft);
            WriteStyle(node, "margin-right", layout.marginRight);
            WriteStyle(node, "margin-top", layout.marginTop);
            WriteStyle(node, "margin-bottom", layout.marginBottom);
            WriteStyle(node, "flex", layout.flex);
            WriteStyle(node, "flex-grow", layout.flexGrow);
            WriteStyle(node, "flex-shrink", layout.flexShrink);
            WriteStyle(node, "gap", layout.gap);
            WriteStyle(node, "justify-content", layout.justifyContent);
            WriteStyle(node, "align-items", layout.alignItems);
            WriteStyle(node, "flex-direction", layout.flexDirection);
            WriteStyle(node, "flex-wrap", layout.flexWrap);
            WriteStyle(node, "overflow", layout.overflow);
            WriteStyle(node, "overflow-x", layout.overflowX);
            WriteStyle(node, "overflow-y", layout.overflowY);
            WriteStyle(node, "box-sizing", layout.boxSizing);
            WriteStyle(node, "-ai-translate-x", layout.translateX);
            WriteStyle(node, "-ai-translate-y", layout.translateY);
            WriteAttribute(node, "data-ui-layout", layout.layoutMode);
            ApplyGridLayoutAttributes(node, layout.gridLayout);
            ApplyCurveLayoutAttributes(node, layout.curveLayout);
            if (Mathf.Abs(layout.rotationZ) > 0.001f)
            {
                WriteStyle(node, "-ai-rotation-z", $"{layout.rotationZ:0.###}");
            }
        }

        private static void ApplyGridLayoutAttributes(AIToUGUICompiledNode node, CompiledGridLayoutDto gridLayout)
        {
            if (gridLayout == null)
            {
                return;
            }

            WriteAttribute(node, "data-ui-grid-columns", gridLayout.columns > 0 ? gridLayout.columns.ToString(CultureInfo.InvariantCulture) : string.Empty);
            WriteAttribute(node, "data-ui-grid-rows", gridLayout.rows > 0 ? gridLayout.rows.ToString(CultureInfo.InvariantCulture) : string.Empty);
            WriteAttribute(node, "data-ui-grid-layers", gridLayout.layers > 0 ? gridLayout.layers.ToString(CultureInfo.InvariantCulture) : string.Empty);
            WriteAttribute(node, "data-ui-grid-cell-type", gridLayout.cellType);
            WriteAttribute(node, "data-ui-grid-cell-width", gridLayout.cellWidth);
            WriteAttribute(node, "data-ui-grid-cell-height", gridLayout.cellHeight);
            WriteAttribute(node, "data-ui-grid-gap-x", gridLayout.gapX);
            WriteAttribute(node, "data-ui-grid-gap-y", gridLayout.gapY);
            WriteAttribute(node, "data-ui-grid-column-direction", gridLayout.columnDirection);
            WriteAttribute(node, "data-ui-grid-row-direction", gridLayout.rowDirection);
            WriteAttribute(node, "data-ui-grid-align-x", gridLayout.horizontalAlign);
            WriteAttribute(node, "data-ui-grid-align-y", gridLayout.verticalAlign);
        }

        private static void ApplyCurveLayoutAttributes(AIToUGUICompiledNode node, CompiledCurveLayoutDto curveLayout)
        {
            if (curveLayout == null)
            {
                return;
            }

            WriteAttribute(node, "data-ui-curve-spacing-mode", curveLayout.spacingMode);
            WriteAttribute(node, "data-ui-curve-spacing", Mathf.Abs(curveLayout.spacing) > 0.001f ? curveLayout.spacing.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty);
            WriteAttribute(node, "data-ui-curve-start-at", Mathf.Abs(curveLayout.startAt) > 0.001f ? curveLayout.startAt.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty);
            WriteAttribute(node, "data-ui-curve-rotation", curveLayout.rotation);
            WriteAttribute(node, "data-ui-curve-extend-before", curveLayout.extendBefore);
            WriteAttribute(node, "data-ui-curve-extend-after", curveLayout.extendAfter);
            WriteAttribute(node, "data-ui-curve-lock-tangents", curveLayout.lockTangents ? "true" : string.Empty);
            WriteAttribute(node, "data-ui-curve-lock-positions", curveLayout.lockPositions ? "true" : string.Empty);
            WriteAttribute(node, "data-ui-curve-points", SerializeCurvePoints(curveLayout.points));
        }

        private static string SerializeCurvePoints(CompiledCurvePointDto[] points)
        {
            if (points == null || points.Length == 0)
            {
                return string.Empty;
            }

            return JsonConvert.SerializeObject(points);
        }

        private static void ApplyTextStyle(AIToUGUICompiledNode node, CompiledTextStyleDto textStyle)
        {
            if (textStyle == null)
            {
                return;
            }

            WriteStyle(node, "color", textStyle.color);
            WriteStyle(node, "font-size", textStyle.fontSize);
            WriteStyle(node, "font-family", textStyle.fontFamily);
            WriteStyle(node, "font-weight", textStyle.fontWeight);
            WriteStyle(node, "line-height", textStyle.lineHeight);
            WriteStyle(node, "text-align", textStyle.textAlign);
            WriteStyle(node, "letter-spacing", textStyle.letterSpacing);
            WriteStyle(node, "text-transform", textStyle.textTransform);
        }

        private static void ApplyVisual(AIToUGUICompiledNode node, CompiledVisualDto visual)
        {
            if (visual == null)
            {
                return;
            }

            WriteStyle(node, "background", visual.background);
            WriteStyle(node, "background-color", visual.backgroundColor);
            WriteStyle(node, "border", visual.border);
            WriteStyle(node, "border-style", visual.borderStyle);
            WriteStyle(node, "border-radius", visual.borderRadius);
            WriteStyle(node, "box-shadow", visual.boxShadow);
            WriteStyle(node, "opacity", visual.opacity);
            if (!string.IsNullOrWhiteSpace(visual.borderStyle))
            {
                WriteStyle(node, "-ai-border-style", visual.borderStyle);
            }

            if (!string.IsNullOrWhiteSpace(visual.fillColor) && !node.Styles.ContainsKey("background-color"))
            {
                WriteStyle(node, "background-color", visual.fillColor);
            }

            if (visual.useGradient &&
                !string.IsNullOrWhiteSpace(visual.fillColor) &&
                !string.IsNullOrWhiteSpace(visual.gradientColor) &&
                !node.Styles.ContainsKey("background"))
            {
                WriteStyle(node, "background", BuildGradient(ParseGradientDirection(visual.gradientDirection), visual.fillColor, visual.gradientColor));
            }

            if (visual.cornerRadius > 0f && !node.Styles.ContainsKey("border-radius"))
            {
                WriteStyle(node, "border-radius", $"{visual.cornerRadius:0.###}px");
            }

            if (visual.outlineWidth > 0f && !node.Styles.ContainsKey("border"))
            {
                var outlineColor = string.IsNullOrWhiteSpace(visual.outlineColor) ? "rgba(255,255,255,0.12)" : visual.outlineColor;
                WriteStyle(node, "border", $"{visual.outlineWidth:0.###}px solid {outlineColor}");
            }

            if ((Mathf.Abs(visual.shadowOffsetX) > 0.001f || Mathf.Abs(visual.shadowOffsetY) > 0.001f || visual.shadowBlur > 0.001f) &&
                !node.Styles.ContainsKey("box-shadow"))
            {
                var shadowColor = string.IsNullOrWhiteSpace(visual.shadowColor) ? "rgba(0,0,0,0.25)" : visual.shadowColor;
                WriteStyle(
                    node,
                    "box-shadow",
                    $"{visual.shadowOffsetX:0.###}px {visual.shadowOffsetY:0.###}px {visual.shadowBlur:0.###}px {shadowColor}");
            }

            if (visual.enableGlow)
            {
                WriteStyle(node, "-ai-glow", "true");
                WriteStyle(node, "-ai-glow-color", visual.glowColor);
                if (visual.glowBlur > 0f)
                {
                    WriteStyle(node, "-ai-glow-blur", $"{visual.glowBlur:0.###}");
                }

                if (visual.glowIntensity > 0f)
                {
                    WriteStyle(node, "-ai-glow-intensity", $"{visual.glowIntensity:0.###}");
                }
            }
        }

        private static void ApplyMotion(AIToUGUICompiledNode node, CompiledMotionDto motion)
        {
            if (motion == null || string.IsNullOrWhiteSpace(motion.presetId))
            {
                if (!string.IsNullOrWhiteSpace(motion?.loopPresetId))
                {
                    WriteStyle(node, "-ai-loop-motion", motion.loopPresetId);
                    if (Mathf.Abs(motion.loopDelay) > 0.001f)
                    {
                        WriteStyle(node, "-ai-loop-delay", $"{motion.loopDelay:0.###}");
                    }
                }

                return;
            }

            WriteStyle(node, "-ai-motion", motion.presetId);
            if (!string.IsNullOrWhiteSpace(motion.loopPresetId))
            {
                WriteStyle(node, "-ai-loop-motion", motion.loopPresetId);
            }

            if (Mathf.Abs(motion.loopDelay) > 0.001f)
            {
                WriteStyle(node, "-ai-loop-delay", $"{motion.loopDelay:0.###}");
            }
        }

        private static AIToUGUIThemeDefinition BuildTransientTheme(CompiledThemeDto themeDto)
        {
            var theme = ScriptableObject.CreateInstance<AIToUGUIThemeDefinition>();
            theme.hideFlags = HideFlags.HideAndDontSave;

            if (themeDto == null)
            {
                theme.themeId = "compiled_theme";
                theme.displayName = "Compiled Theme";
                return theme;
            }

            theme.themeId = string.IsNullOrWhiteSpace(themeDto.themeId) ? "compiled_theme" : themeDto.themeId;
            theme.displayName = string.IsNullOrWhiteSpace(themeDto.displayName) ? theme.themeId : themeDto.displayName;
            theme.pageBackground = ParseColor(themeDto.pageBackground, new Color(0.06f, 0.08f, 0.11f, 1f));
            theme.panelFill = ParseColor(themeDto.panelFill, new Color(0.13f, 0.16f, 0.2f, 0.96f));
            theme.cardFill = ParseColor(themeDto.cardFill, theme.panelFill);
            theme.buttonFill = ParseColor(themeDto.buttonFill, theme.panelFill);
            theme.accentColor = ParseColor(themeDto.accentColor, new Color(0.93f, 0.79f, 0.42f, 1f));
            theme.textPrimary = ParseColor(themeDto.textPrimary, Color.white);
            theme.textSecondary = ParseColor(themeDto.textSecondary, new Color(1f, 1f, 1f, 0.7f));
            theme.outlineColor = ParseColor(themeDto.outlineColor, new Color(1f, 1f, 1f, 0.12f));
            theme.shadowColor = ParseColor(themeDto.shadowColor, new Color(0f, 0f, 0f, 0.28f));
            theme.glowColor = theme.accentColor;
            theme.panelRadius = ResolveThemeCornerRadius(themeDto, "panel/default");
            theme.cardRadius = ResolveThemeCornerRadius(themeDto, "card/default");
            theme.buttonRadius = ResolveThemeCornerRadius(themeDto, "button/default");

            theme.tokens.Clear();
            if (themeDto.tokens != null)
            {
                for (var i = 0; i < themeDto.tokens.Length; i++)
                {
                    var token = themeDto.tokens[i];
                    if (token == null || string.IsNullOrWhiteSpace(token.tokenId))
                    {
                        continue;
                    }

                    theme.tokens.Add(new AIToUGUIThemeToken
                    {
                        tokenId = token.tokenId,
                        value = token.value ?? string.Empty
                    });
                }
            }

            ApplyThemeTokenOverrides(theme);

            theme.visualPresets.Clear();
            if (themeDto.visualPresets != null)
            {
                for (var i = 0; i < themeDto.visualPresets.Length; i++)
                {
                    var presetDto = themeDto.visualPresets[i];
                    if (presetDto == null || string.IsNullOrWhiteSpace(presetDto.presetId))
                    {
                        continue;
                    }

                    theme.visualPresets.Add(BuildVisualPreset(theme, presetDto));
                }
            }

            EnsureDefaultVisualPreset(theme, "panel/default");
            EnsureDefaultVisualPreset(theme, "card/default");
            EnsureDefaultVisualPreset(theme, "button/default");

            theme.motionPresets.Clear();
            if (themeDto.motionPresets != null)
            {
                for (var i = 0; i < themeDto.motionPresets.Length; i++)
                {
                    var presetDto = themeDto.motionPresets[i];
                    if (presetDto == null || string.IsNullOrWhiteSpace(presetDto.presetId))
                    {
                        continue;
                    }

                    theme.motionPresets.Add(new AIToUGUIMotionPreset
                    {
                        presetId = presetDto.presetId,
                        enterMotion = ParseMotionType(presetDto.enterMotion),
                        hoverMotion = ParseMotionType(presetDto.hoverMotion),
                        pressMotion = ParseMotionType(presetDto.pressMotion),
                        duration = presetDto.duration,
                        distance = presetDto.distance,
                        scale = presetDto.scale,
                        ease = ParseEase(presetDto.ease)
                    });
                }
            }

            theme.loopMotionPresets.Clear();
            if (themeDto.loopMotionPresets != null)
            {
                for (var i = 0; i < themeDto.loopMotionPresets.Length; i++)
                {
                    var presetDto = themeDto.loopMotionPresets[i];
                    if (presetDto == null || string.IsNullOrWhiteSpace(presetDto.presetId))
                    {
                        continue;
                    }

                    theme.loopMotionPresets.Add(new AIToUGUILoopMotionPreset
                    {
                        presetId = presetDto.presetId,
                        loopType = ParseLoopMotionType(presetDto.loopType),
                        duration = presetDto.duration,
                        amplitude = presetDto.amplitude,
                        ease = ParseEase(presetDto.ease)
                    });
                }
            }

            EnsureDefaultLoopMotionPreset(theme, "loop/rotate-slow", AIToUGUILoopMotionType.Rotate, 20f, 1f, AIToUGUI.Ease.Linear);
            EnsureDefaultLoopMotionPreset(theme, "loop/rotate-slow-reverse", AIToUGUILoopMotionType.RotateReverse, 15f, 1f, AIToUGUI.Ease.Linear);
            EnsureDefaultLoopMotionPreset(theme, "loop/float-soft", AIToUGUILoopMotionType.Float, 8f, 20f, AIToUGUI.Ease.InOutSine);
            EnsureDefaultLoopMotionPreset(theme, "loop/pulse-soft", AIToUGUILoopMotionType.Pulse, 3f, 0.06f, AIToUGUI.Ease.InOutSine);

            return theme;
        }

        private static void ApplyThemeTokenOverrides(AIToUGUIThemeDefinition theme)
        {
            if (theme == null || theme.tokens == null || theme.tokens.Count == 0)
            {
                return;
            }

            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < theme.tokens.Count; i++)
            {
                var token = theme.tokens[i];
                if (token == null || string.IsNullOrWhiteSpace(token.tokenId) || string.IsNullOrWhiteSpace(token.value))
                {
                    continue;
                }

                tokens[token.tokenId.Trim()] = token.value.Trim();
            }

            theme.pageBackground = GetThemeTokenColor(tokens, "--page-bg", theme.pageBackground);
            theme.panelFill = GetThemeTokenColor(tokens, "--panel-fill", theme.panelFill);
            theme.cardFill = GetThemeTokenColor(tokens, "--card-fill", theme.cardFill);
            theme.buttonFill = GetThemeTokenColor(tokens, "--button-fill", theme.buttonFill);
            theme.buttonFill = GetThemeTokenColor(tokens, "--accent-amber", theme.buttonFill);
            theme.accentColor = GetThemeTokenColor(tokens, "--accent", theme.accentColor);
            theme.accentColor = GetThemeTokenColor(tokens, "--accent-amber", theme.accentColor);
            theme.textPrimary = GetThemeTokenColor(tokens, "--text-primary", theme.textPrimary);
            theme.textSecondary = GetThemeTokenColor(tokens, "--text-secondary", theme.textSecondary);
            theme.outlineColor = GetThemeTokenColor(tokens, "--outline-color", theme.outlineColor);
        }

        private static Color GetThemeTokenColor(Dictionary<string, string> tokens, string tokenId, Color fallback)
        {
            if (tokens == null || string.IsNullOrWhiteSpace(tokenId))
            {
                return fallback;
            }

            return tokens.TryGetValue(tokenId, out var value)
                ? ParseColor(value, fallback)
                : fallback;
        }

        private static float ResolveThemeCornerRadius(CompiledThemeDto themeDto, string presetId)
        {
            if (themeDto == null)
            {
                return 0f;
            }

            var explicitRadius = presetId switch
            {
                "panel/default" => themeDto.panelRadius,
                "card/default" => themeDto.cardRadius,
                "button/default" => themeDto.buttonRadius,
                _ => 0f
            };

            if (explicitRadius > 0.001f)
            {
                return Mathf.Max(0f, explicitRadius);
            }

            if (themeDto.visualPresets != null)
            {
                for (var i = 0; i < themeDto.visualPresets.Length; i++)
                {
                    var presetDto = themeDto.visualPresets[i];
                    if (presetDto == null || !string.Equals(presetDto.presetId, presetId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return Mathf.Max(0f, presetDto.cornerRadius);
                }
            }

            return 0f;
        }

        private static AIToUGUIVisualPreset BuildVisualPreset(AIToUGUIThemeDefinition theme, CompiledVisualPresetDto presetDto)
        {
            if (IsPlaceholderVisualPreset(presetDto))
            {
                return BuildFallbackVisualPreset(theme, presetDto != null ? presetDto.presetId : string.Empty);
            }

            return new AIToUGUIVisualPreset
            {
                presetId = presetDto.presetId,
                enableFill = presetDto.enableFill,
                fillColor = ParseColor(presetDto.fillColor, theme.panelFill),
                useGradient = presetDto.useGradient,
                gradientColor = ParseColor(presetDto.gradientColor, theme.panelFill),
                gradientDirection = ParseGradientDirection(presetDto.gradientDirection),
                cornerRadius = presetDto.cornerRadius,
                useMaxRoundness = presetDto.useMaxRoundness,
                outlineWidth = presetDto.outlineWidth,
                outlineColor = ParseColor(presetDto.outlineColor, theme.outlineColor),
                shadowSize = Mathf.Max(Mathf.Abs(presetDto.shadowOffsetX), Mathf.Abs(presetDto.shadowOffsetY)),
                shadowBlur = presetDto.shadowBlur,
                shadowColor = ParseColor(presetDto.shadowColor, theme.shadowColor),
                enableGlow = presetDto.enableGlow,
                glowColor = ParseColor(presetDto.glowColor, theme.glowColor),
                glowBlur = presetDto.glowBlur,
                glowIntensity = presetDto.glowIntensity
            };
        }

        private static bool IsPlaceholderVisualPreset(CompiledVisualPresetDto presetDto)
        {
            if (presetDto == null)
            {
                return true;
            }

            return !presetDto.enableFill &&
                   string.IsNullOrWhiteSpace(presetDto.fillColor) &&
                   !presetDto.useGradient &&
                   string.IsNullOrWhiteSpace(presetDto.gradientColor) &&
                   Mathf.Approximately(presetDto.cornerRadius, 0f) &&
                   !presetDto.useMaxRoundness &&
                   Mathf.Approximately(presetDto.outlineWidth, 0f) &&
                   string.IsNullOrWhiteSpace(presetDto.outlineColor) &&
                   Mathf.Approximately(presetDto.shadowOffsetX, 0f) &&
                   Mathf.Approximately(presetDto.shadowOffsetY, 0f) &&
                   Mathf.Approximately(presetDto.shadowBlur, 0f) &&
                   string.IsNullOrWhiteSpace(presetDto.shadowColor) &&
                   !presetDto.enableGlow &&
                   string.IsNullOrWhiteSpace(presetDto.glowColor) &&
                   Mathf.Approximately(presetDto.glowBlur, 0f);
        }

        private static void EnsureDefaultVisualPreset(AIToUGUIThemeDefinition theme, string presetId)
        {
            if (theme == null || string.IsNullOrWhiteSpace(presetId))
            {
                return;
            }

            var existing = theme.ResolveVisualPreset(presetId);
            if (existing != null)
            {
                return;
            }

            theme.visualPresets.Add(BuildFallbackVisualPreset(theme, presetId));
        }

        private static void EnsureDefaultLoopMotionPreset(AIToUGUIThemeDefinition theme, string presetId, AIToUGUILoopMotionType loopType, float duration, float amplitude, AIToUGUI.Ease ease)
        {
            if (theme == null || string.IsNullOrWhiteSpace(presetId))
            {
                return;
            }

            if (theme.ResolveLoopMotionPreset(presetId) != null)
            {
                return;
            }

            theme.loopMotionPresets.Add(new AIToUGUILoopMotionPreset
            {
                presetId = presetId,
                loopType = loopType,
                duration = duration,
                amplitude = amplitude,
                ease = ease
            });
        }

        private static AIToUGUIVisualPreset BuildFallbackVisualPreset(AIToUGUIThemeDefinition theme, string presetId)
        {
            var normalizedPresetId = string.IsNullOrWhiteSpace(presetId) ? "panel/default" : presetId.Trim();
            if (string.Equals(normalizedPresetId, "button/default", StringComparison.Ordinal))
            {
                return new AIToUGUIVisualPreset
                {
                    presetId = normalizedPresetId,
                    enableFill = true,
                    fillColor = theme.buttonFill,
                    useGradient = false,
                    gradientColor = theme.buttonFill,
                    gradientDirection = AIToUGUIGradientDirection.None,
                    cornerRadius = theme.buttonRadius,
                    useMaxRoundness = false,
                    outlineWidth = theme.outlineWidth,
                    outlineColor = theme.outlineColor,
                    shadowSize = theme.shadowSize,
                    shadowBlur = theme.shadowBlur,
                    shadowColor = theme.shadowColor,
                    enableGlow = false,
                    glowColor = theme.glowColor,
                    glowBlur = theme.glowBlur,
                    glowIntensity = theme.glowIntensity
                };
            }

            if (string.Equals(normalizedPresetId, "card/default", StringComparison.Ordinal))
            {
                return new AIToUGUIVisualPreset
                {
                    presetId = normalizedPresetId,
                    enableFill = true,
                    fillColor = theme.cardFill,
                    useGradient = false,
                    gradientColor = theme.cardFill,
                    gradientDirection = AIToUGUIGradientDirection.None,
                    cornerRadius = theme.cardRadius,
                    useMaxRoundness = false,
                    outlineWidth = Mathf.Max(1f, theme.outlineWidth - 1f),
                    outlineColor = theme.outlineColor,
                    shadowSize = Mathf.Max(6f, theme.shadowSize - 4f),
                    shadowBlur = Mathf.Max(12f, theme.shadowBlur - 8f),
                    shadowColor = theme.shadowColor,
                    enableGlow = false,
                    glowColor = theme.glowColor,
                    glowBlur = theme.glowBlur,
                    glowIntensity = theme.glowIntensity
                };
            }

            return new AIToUGUIVisualPreset
            {
                presetId = normalizedPresetId,
                enableFill = true,
                fillColor = theme.panelFill,
                useGradient = false,
                gradientColor = theme.panelFill,
                gradientDirection = AIToUGUIGradientDirection.None,
                cornerRadius = theme.panelRadius,
                useMaxRoundness = false,
                outlineWidth = theme.outlineWidth,
                outlineColor = theme.outlineColor,
                shadowSize = theme.shadowSize,
                shadowBlur = theme.shadowBlur,
                shadowColor = theme.shadowColor,
                enableGlow = false,
                glowColor = theme.glowColor,
                glowBlur = theme.glowBlur,
                glowIntensity = theme.glowIntensity
            };
        }

        private static void WriteStyle(AIToUGUICompiledNode node, string key, string value)
        {
            if (node == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            node.Styles[key] = value.Trim();
        }

        private static void WriteAttribute(AIToUGUICompiledNode node, string key, string value)
        {
            if (node == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            node.Attributes[key] = value;
        }

        private static string BuildGradient(AIToUGUIGradientDirection direction, string primary, string secondary)
        {
            var angle = direction switch
            {
                AIToUGUIGradientDirection.Horizontal => "90deg",
                AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight => "315deg",
                AIToUGUIGradientDirection.DiagonalBottomLeftToTopRight => "45deg",
                _ => "180deg"
            };
            return $"linear-gradient({angle}, {primary}, {secondary})";
        }

        private static AIToUGUIControlType ParseControlType(string raw)
        {
            return Enum.TryParse(raw, true, out AIToUGUIControlType controlType)
                ? controlType
                : AIToUGUIControlType.Div;
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
        }

        private static UILayer ParseLayer(string raw)
        {
            return Enum.TryParse(raw, true, out UILayer layer) ? layer : UILayer.Normal;
        }

        private static AIToUGUIGradientDirection ParseGradientDirection(string raw)
        {
            return Enum.TryParse(raw, true, out AIToUGUIGradientDirection direction)
                ? direction
                : AIToUGUIGradientDirection.None;
        }

        private static AIToUGUIMotionType ParseMotionType(string raw)
        {
            return Enum.TryParse(raw, true, out AIToUGUIMotionType motion)
                ? motion
                : AIToUGUIMotionType.None;
        }

        private static AIToUGUILoopMotionType ParseLoopMotionType(string raw)
        {
            return Enum.TryParse(raw, true, out AIToUGUILoopMotionType motion)
                ? motion
                : AIToUGUILoopMotionType.None;
        }

        private static AIToUGUIAssetType ParseAssetType(string raw)
        {
            return AIToUGUIElementContractUtility.NormalizeAssetTypeId(raw) switch
            {
                nameof(AIToUGUIAssetType.Ornament) => AIToUGUIAssetType.Ornament,
                nameof(AIToUGUIAssetType.Snapshot) => AIToUGUIAssetType.Snapshot,
                nameof(AIToUGUIAssetType.Frame) => AIToUGUIAssetType.Frame,
                nameof(AIToUGUIAssetType.Background) => AIToUGUIAssetType.Background,
                _ => AIToUGUIAssetType.Icon
            };
        }

        private static AIToUGUIAssetImportMode ParseAssetImportMode(string raw)
        {
            return AIToUGUIElementContractUtility.NormalizeAssetImportModeId(raw) switch
            {
                nameof(AIToUGUIAssetImportMode.Sprite) => AIToUGUIAssetImportMode.Sprite,
                nameof(AIToUGUIAssetImportMode.NineSlice) => AIToUGUIAssetImportMode.NineSlice,
                nameof(AIToUGUIAssetImportMode.Tile) => AIToUGUIAssetImportMode.Tile,
                nameof(AIToUGUIAssetImportMode.ReadOnlyOverlay) => AIToUGUIAssetImportMode.ReadOnlyOverlay,
                _ => AIToUGUIAssetImportMode.Auto
            };
        }

        private static AIToUGUI.Ease ParseEase(string raw)
        {
            return Enum.TryParse(raw, true, out AIToUGUI.Ease ease) ? ease : AIToUGUI.Ease.OutCubic;
        }

        private static Color ParseColor(string raw, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            raw = raw.Trim();
            if (string.Equals(raw, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0f, 0f, 0f, 0f);
            }

            if (raw.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString(raw, out var htmlColor))
            {
                return htmlColor;
            }

            if (raw.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var values = NumberRegex.Matches(raw);
                if (values.Count >= 3)
                {
                    var r = Mathf.Clamp01(float.Parse(values[0].Value) / 255f);
                    var g = Mathf.Clamp01(float.Parse(values[1].Value) / 255f);
                    var b = Mathf.Clamp01(float.Parse(values[2].Value) / 255f);
                    var a = values.Count >= 4 ? float.Parse(values[3].Value) : 1f;
                    if (a > 1f)
                    {
                        a /= 255f;
                    }

                    return new Color(r, g, b, Mathf.Clamp01(a));
                }
            }

            return fallback;
        }
    }
}

#endif
