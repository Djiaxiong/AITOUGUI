#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AIToUGUI.Editor
{
    internal sealed class AIToUGUIBakeReport
    {
        public readonly List<AIToUGUICompiledPage> Pages = new List<AIToUGUICompiledPage>();
        public bool HasErrors => Pages.Any(page => page.Root == null || page.Errors.Count > 0);

        public AIToUGUICompiledPage FindPage(AIToUGUIPageDefinition page)
        {
            return Pages.FirstOrDefault(candidate => candidate.SourcePage == page);
        }
    }

    internal enum AIToUGUILengthUnit
    {
        None,
        Pixel,
        Percent,
        Auto
    }

    internal readonly struct AIToUGUICssLength
    {
        public AIToUGUICssLength(float value, AIToUGUILengthUnit unit)
        {
            Value = value;
            Unit = unit;
        }

        public float Value { get; }
        public AIToUGUILengthUnit Unit { get; }
        public bool IsValid => Unit != AIToUGUILengthUnit.None;
        public bool IsAuto => Unit == AIToUGUILengthUnit.Auto;
    }

    internal sealed class AIToUGUIBuiltNode
    {
        public AIToUGUICompiledNode Node;
        public GameObject GameObject;
        public RectTransform RectTransform;
        public Transform ChildHost;
        public AIToUGUIElementSlots Slots;
        public bool IsPrefabBacked;
        // Track C: top-left of this node in page-root-relative pixels (designSpace, y growing
        // downward). Recorded during LayoutBuiltNode so a locked child can convert its
        // snapshot-measured page-absolute rect to parent-local coordinates.
        public Vector2 AbsoluteTopLeftInPage;
        public bool HasAbsoluteTopLeft;
        public readonly List<AIToUGUIBuiltNode> Children = new List<AIToUGUIBuiltNode>();
    }

    internal sealed class AIToUGUIViewFieldSpec
    {
        public string NodeName;
        public string FieldName;
        public string ComponentTypeName;
    }

    internal readonly struct AIToUGUICssEdges
    {
        public AIToUGUICssEdges(AIToUGUICssLength left, AIToUGUICssLength right, AIToUGUICssLength top, AIToUGUICssLength bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public AIToUGUICssLength Left { get; }
        public AIToUGUICssLength Right { get; }
        public AIToUGUICssLength Top { get; }
        public AIToUGUICssLength Bottom { get; }
    }

    internal enum AIToUGUILayoutMode
    {
        Block,
        Flex,
        Grid,
        Curve
    }

    internal static class AIToUGUISiteBaker
    {
        private const string SvgResolutionEditorPrefsKey = "AIToUGUI.SvgTargetResolution";
        private const string ResourceConfigAssetPath = "Assets/Resources/Config/ResourceConfig.asset";
        private const string RuntimeRegistryAssetPath = "Assets/Resources/Config/AIToUGUIRuntimeRegistry.asset";
        private const string InternalNodePrefix = "__ai_";
        private const string InternalCompositeRootName = "__ai_Composite";
        private const string InternalPrimitiveRootName = "__ai_Primitive";
        private const string InternalContentMaskName = "__ai_ContentMask";
        private const string InternalAssetVisualName = "__ai_AssetVisual";
        private const AdditionalCanvasShaderChannels RequiredProceduralUiChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;
        private const int UnlimitedVisibleLines = 9999;

        private static readonly Regex NumberRegex = new Regex("-?\\d+(?:\\.\\d+)?", RegexOptions.Compiled);
        private static readonly Regex ColorRegex = new Regex("#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8}|[0-9a-fA-F]{3}|[0-9a-fA-F]{4})|rgba?\\([^)]+\\)|transparent", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LinearGradientDirectionRegex = new Regex("linear-gradient\\((?<direction>[^,]+),", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BorderWidthRegex = new Regex("(?<width>-?\\d+(?:\\.\\d+)?)px", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RotateTransformRegex = new Regex("^rotate\\(\\s*(?<angle>-?\\d+(?:\\.\\d+)?)deg\\s*\\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ViewFieldNamePartRegex = new Regex("[A-Za-z0-9]+", RegexOptions.Compiled);
        private static readonly List<AIToUGUIBuiltNode> s_emptyBuiltNodeList = new List<AIToUGUIBuiltNode>(0);
        private static TMP_FontAsset s_fallbackFontAsset;
        private static bool s_fallbackFontResolved;

        public static AIToUGUIBakeReport Validate(AIToUGUISiteDefinition site)
        {
            AIToUGUISiteImportUtility.SyncSiteFromManifest(site);
            var report = new AIToUGUIBakeReport();
            if (site == null || site.pages == null)
            {
                return report;
            }

            for (var i = 0; i < site.pages.Count; i++)
            {
                var page = site.pages[i];
                if (page == null)
                {
                    continue;
                }

                report.Pages.Add(AIToUGUICompiler.Compile(site, page));
            }

            return report;
        }

        public static AIToUGUIBakeReport BakeSite(AIToUGUISiteDefinition site)
        {
            AIToUGUISiteImportUtility.SyncSiteFromManifest(site);
            var report = Validate(site);
            PrepareSvgSpriteAssets(report.Pages);
            for (var i = 0; i < report.Pages.Count; i++)
            {
                BakeCompiledPage(site, report.Pages[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return report;
        }

        public static AIToUGUICompiledPage BakePage(AIToUGUISiteDefinition site, AIToUGUIPageDefinition page)
        {
            AIToUGUISiteImportUtility.SyncSiteFromManifest(site);
            var compiled = AIToUGUICompiler.Compile(site, page);
            PrepareSvgSpriteAssets(compiled);
            BakeCompiledPage(site, compiled);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return compiled;
        }

        public static void BakeCompiledPage(AIToUGUICompiledPage page)
        {
            if (page == null || page.Root == null)
            {
                return;
            }

            PrepareSvgSpriteAssets(page);
            var prefabPath = BuildPrefabPath(page);
            var metadataPath = BuildMetadataPath(page);
            BakeCompiledPage(page, prefabPath, metadataPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static bool BakeCompiledPageFromPreview(AIToUGUICompiledPage page, AIToUGUIPreviewMount mount)
        {
            if (page == null || page.Root == null || mount == null)
            {
                return false;
            }

            var previewRoot = FindPreviewRoot(mount, page.SiteId, page.PageId);
            if (previewRoot == null)
            {
                return false;
            }

            var prefabPath = BuildPrefabPath(page);
            var metadataPath = BuildMetadataPath(page);
            TryMigrateLegacyManagedOutputFolders(page);
            EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace("\\", "/"));
            EnsureFolder(Path.GetDirectoryName(metadataPath)?.Replace("\\", "/"));

            var clone = UnityEngine.Object.Instantiate(previewRoot);
            clone.name = previewRoot.name;
            clone.hideFlags = HideFlags.None;

            try
            {
                if (clone.TryGetComponent<AIToUGUIPreviewInstance>(out var previewInstance))
                {
                    AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(previewInstance);
                }

                RemoveMissingScriptsRecursive(clone);
                if (!HasExportMarkers(clone.transform))
                {
                    return false;
                }

                var exportedNodes = CollectExportedNodesFromHierarchy(clone);
                ValidateCompiledPreviewForBake(page, clone.transform);
                TryGeneratePanelViewScript(page, exportedNodes);
                var metadata = UpdateMetadata(page, prefabPath, metadataPath, exportedNodes);
                BindMetadataToPageRoot(clone, page, metadata);
                if (page.Errors.Count > 0)
                {
                    return false;
                }

                if (!PreparePrefabRootForSave(clone, page))
                {
                    return false;
                }

                CleanupExistingPrefabMissingScripts(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                UpsertPrefabToResourceConfig(prefabPath, page.LogicalPath);
                UpsertRuntimeRegistry(page, metadataPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return true;
            }
            finally
            {
                AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(clone);
            }
        }

        internal static GameObject PreviewPage(AIToUGUISiteDefinition site, AIToUGUIPageDefinition page, AIToUGUIPreviewMount mount)
        {
            AIToUGUISiteImportUtility.SyncSiteFromManifest(site);
            var compiled = AIToUGUICompiler.Compile(site, page);
            return PreviewCompiledPage(compiled, mount);
        }

        internal static GameObject PreviewCompiledPage(AIToUGUICompiledPage page, AIToUGUIPreviewMount mount)
        {
            if (page == null || page.Root == null || mount == null)
            {
                return null;
            }

            PrepareSvgSpriteAssets(page);
            if (mount.clearBeforePreview)
            {
                ClearPreview(mount);
            }

            var root = BuildPageGameObject(page);
            if (root == null)
            {
                page.Warnings.Add("场景预览构建失败，未生成根对象。");
                return null;
            }

            RemoveMissingScriptsRecursive(root);

            var previewInstance = root.AddComponent<AIToUGUIPreviewInstance>();
            previewInstance.siteId = page.SiteId;
            previewInstance.pageId = page.PageId;
            root.transform.SetParent(mount.transform, false);
            root.transform.localScale = Vector3.one;
            EnsureCanvasChannels(mount.GetComponentInParent<Canvas>());

            if (root.TryGetComponent<AIToUGUIPageRoot>(out var pageRoot))
            {
                pageRoot.ApplyNow();
            }

            if (root.TryGetComponent<RectTransform>(out var rootRect))
            {
                rootRect.localScale = root.transform.localScale;
            }

            root.SetActive(true);
            ScheduleSemanticLayoutRefresh(root);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            if (mount.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(mount.gameObject.scene);
            }

            return root;
        }

        internal static int ClearPreview(AIToUGUIPreviewMount mount)
        {
            if (mount == null)
            {
                return 0;
            }

            var toDelete = new List<GameObject>();
            for (var i = 0; i < mount.transform.childCount; i++)
            {
                var child = mount.transform.GetChild(i);
                if (child != null &&
                    (child.GetComponent<AIToUGUIPreviewInstance>() != null ||
                     child.GetComponent<AIToUGUIPageRoot>() != null))
                {
                    toDelete.Add(child.gameObject);
                }
            }

            for (var i = 0; i < toDelete.Count; i++)
            {
                AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(toDelete[i]);
            }

            if (toDelete.Count > 0 && mount.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(mount.gameObject.scene);
            }

            CleanupOrphanedFlexalonSingleton();
            return toDelete.Count;
        }

        private static void BakeCompiledPage(AIToUGUISiteDefinition site, AIToUGUICompiledPage page)
        {
            if (site == null || page == null || page.Root == null)
            {
                return;
            }

            PrepareSvgSpriteAssets(page);
            var prefabPath = BuildPrefabPath(site, page);
            var metadataPath = BuildMetadataPath(site, page);
            BakeCompiledPage(page, prefabPath, metadataPath);
        }

        private static void BakeCompiledPage(AIToUGUICompiledPage page, string prefabPath, string metadataPath)
        {
            if (page == null || page.Root == null)
            {
                return;
            }

            TryMigrateLegacyManagedOutputFolders(page);
            EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace("\\", "/"));
            EnsureFolder(Path.GetDirectoryName(metadataPath)?.Replace("\\", "/"));

            var root = BuildPageGameObject(page, out var builtRoot);
            if (root == null)
            {
                AddMessage(page.Errors, "预制体构建失败，未生成根对象。");
                UpdateMetadata(page, prefabPath, metadataPath, (List<AIToUGUIBakeExportedNodeInfo>)null);
                return;
            }

            try
            {
                var exportedNodes = CollectExportedNodes(builtRoot);
                ValidateCompiledPageForBake(page, root, builtRoot);
                TryGeneratePanelViewScript(page, exportedNodes);
                var metadata = UpdateMetadata(page, prefabPath, metadataPath, builtRoot);
                BindMetadataToPageRoot(root, page, metadata);
                if (page.Errors.Count > 0)
                {
                    return;
                }

                root.SetActive(true);
                if (!PreparePrefabRootForSave(root, page))
                {
                    return;
                }

                CleanupExistingPrefabMissingScripts(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UpsertPrefabToResourceConfig(prefabPath, page.LogicalPath);
                UpsertRuntimeRegistry(page, metadataPath);
            }
            finally
            {
                AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(root);
            }
        }

        private static GameObject BuildPageGameObject(AIToUGUICompiledPage page)
        {
            return BuildPageGameObject(page, out _);
        }

        private static GameObject BuildPageGameObject(AIToUGUICompiledPage page, out AIToUGUIBuiltNode builtRoot)
        {
            builtRoot = null;
            var root = new GameObject(page.Root.Name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(AIToUGUIPageRoot));
            root.SetActive(false);
            var rootRect = root.GetComponent<RectTransform>();
            StretchToParent(rootRect);
            rootRect.localScale = Vector3.one;

            var pageRoot = root.GetComponent<AIToUGUIPageRoot>();
            pageRoot.siteId = page.SiteId;
            pageRoot.pageId = page.PageId;
            pageRoot.runtimePageId = page.RuntimePageId;
            pageRoot.themeId = page.Theme != null ? page.Theme.themeId : string.Empty;
            pageRoot.resourceLogicalPath = page.LogicalPath;
            pageRoot.targetLayer = page.TargetLayer;
            pageRoot.generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            pageRoot.designResolution = page.DesignResolution;
            pageRoot.ApplyNow();

            builtRoot = BuildNodeTree(root, page.Root, page, page.DesignResolution, true);
            LayoutBuiltNode(builtRoot, page.DesignResolution, true, false);
            RefreshTextComponents(builtRoot);
            RefreshProceduralAdapters(builtRoot);

            TryAttachPanelComponent(root, page);
            ApplyBuiltNodePostLayoutStyles(builtRoot, true);
            return root;
        }

        private static AIToUGUIBuiltNode BuildNodeTree(GameObject go, AIToUGUICompiledNode node, AIToUGUICompiledPage page, Vector2 parentSize, bool isRoot)
        {
            var childHost = ApplyNodeToGameObject(go, node, page, parentSize, isRoot, out var slots, out var isPrefabBacked);
            var built = new AIToUGUIBuiltNode
            {
                Node = node,
                GameObject = go,
                RectTransform = go.GetComponent<RectTransform>(),
                ChildHost = childHost,
                Slots = slots,
                IsPrefabBacked = isPrefabBacked
            };

            for (var i = 0; i < node.Children.Count; i++)
            {
                var childNode = node.Children[i];
                var childGo = new GameObject(childNode.Name, typeof(RectTransform), typeof(CanvasRenderer));
                childGo.transform.SetParent(ResolveChildParent(built, childNode), false);
                var builtChild = BuildNodeTree(childGo, childNode, page, parentSize, false);
                ConfigureSemanticLayoutChildParticipation(node, builtChild);
                built.Children.Add(builtChild);
            }

            return built;
        }

        private static Transform ApplyNodeToGameObject(
            GameObject go,
            AIToUGUICompiledNode node,
            AIToUGUICompiledPage page,
            Vector2 parentSize,
            bool isRoot,
            out AIToUGUIElementSlots slots,
            out bool isPrefabBacked)
        {
            slots = null;
            isPrefabBacked = false;
            var rect = go.GetComponent<RectTransform>();
            InitializeRectTransform(rect, isRoot);
            if (TryConfigureCompositeElement(go, node, page, out var compositeChildHost, out slots))
            {
                isPrefabBacked = true;
                ConfigureExportMarker(go, node, true);
                ConfigureAssetBindingManifest(go, node);
                return compositeChildHost;
            }

            if (TryConfigurePrimitiveElement(go, node, page, out var primitiveChildHost, out slots))
            {
                isPrefabBacked = true;
                ConfigureExportMarker(go, node, true);
                ConfigureAssetBindingManifest(go, node);
                return primitiveChildHost;
            }

            ConfigureGraphic(go, node, page, isRoot);
            ConfigureLayout(rect, node, parentSize);
            var controlHost = ConfigureControl(go, node, page);
            var childHost = ConfigureContentMaskHost(go, node, controlHost, page);
            ConfigureExportMarker(go, node, false);
            ConfigureAssetBindingManifest(go, node);
            return childHost;
        }

        private static void ConfigureExportMarker(GameObject go, AIToUGUICompiledNode node, bool isPrefabBacked)
        {
            if (go == null || node == null)
            {
                return;
            }

            var marker = go.GetComponent<AIToUGUIExportNodeMarker>() ?? go.AddComponent<AIToUGUIExportNodeMarker>();
            marker.nodeName = string.IsNullOrWhiteSpace(node.Name) ? go.name : node.Name;
            marker.controlType = node.ControlType;
            marker.role = node.Role;
            marker.elementId = node.ElementId;
            marker.variantId = node.VariantId;
            marker.shapeId = node.ShapeId;
            marker.frameId = node.FrameId;
            marker.slotId = node.SlotId;
            marker.containerId = node.ContainerId;
            marker.templateId = node.TemplateId;
            marker.componentFamily = node.ComponentFamily;
            marker.componentVariant = node.ComponentVariant;
            marker.renderStrategy = node.RenderStrategy ?? AIToUGUIElementContractUtility.ProceduralRenderStrategyId;
            marker.isPrefabBacked = isPrefabBacked;
        }

        private static Transform ResolveChildParent(AIToUGUIBuiltNode parentBuiltNode, AIToUGUICompiledNode childNode)
        {
            if (parentBuiltNode == null)
            {
                return null;
            }

            var slots = parentBuiltNode.Slots;
            if (slots != null && childNode != null)
            {
                var slotTransform = GetSlotTransform(slots, childNode.SlotId, null);
                if (slotTransform != null)
                {
                    return slotTransform;
                }

                var containerTransform = GetSlotTransform(slots, childNode.ContainerId, null);
                if (containerTransform != null)
                {
                    return containerTransform;
                }
            }

            return parentBuiltNode.ChildHost != null
                ? parentBuiltNode.ChildHost
                : parentBuiltNode.GameObject != null ? parentBuiltNode.GameObject.transform : null;
        }

        private static void ConfigureAssetBindingManifest(GameObject go, AIToUGUICompiledNode node)
        {
            if (go == null || node == null)
            {
                return;
            }

            if ((node.AssetRefs == null || node.AssetRefs.Count == 0) &&
                (node.FidelityNotes == null || node.FidelityNotes.Count == 0) &&
                string.Equals(
                    node.RenderStrategy,
                    AIToUGUIElementContractUtility.ProceduralRenderStrategyId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(node.ComponentFamily))
            {
                var existing = go.GetComponent<AIToUGUIAssetBindingManifest>();
                if (existing != null)
                {
                    AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(existing);
                }

                return;
            }

            var manifest = go.GetComponent<AIToUGUIAssetBindingManifest>() ?? go.AddComponent<AIToUGUIAssetBindingManifest>();
            manifest.componentFamily = node.ComponentFamily ?? string.Empty;
            manifest.componentVariant = node.ComponentVariant ?? string.Empty;
            manifest.renderStrategy = node.RenderStrategy ?? AIToUGUIElementContractUtility.ProceduralRenderStrategyId;
            manifest.assetRefs.Clear();
            manifest.fidelityNotes.Clear();

            if (node.AssetRefs != null)
            {
                for (var i = 0; i < node.AssetRefs.Count; i++)
                {
                    var assetRef = node.AssetRefs[i];
                    if (assetRef == null || string.IsNullOrWhiteSpace(assetRef.assetId))
                    {
                        continue;
                    }

                    manifest.assetRefs.Add(new AIToUGUIAssetBindingEntry
                    {
                        assetId = assetRef.assetId ?? string.Empty,
                        assetType = assetRef.assetType.ToString(),
                        usage = assetRef.usage ?? string.Empty,
                        importMode = assetRef.importMode.ToString(),
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

            if (node.FidelityNotes != null)
            {
                manifest.fidelityNotes.AddRange(node.FidelityNotes);
            }
        }

        private static void InitializeRectTransform(RectTransform rect, bool isRoot)
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            if (isRoot)
            {
                StretchToParent(rect);
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void LayoutBuiltNode(AIToUGUIBuiltNode built, Vector2 size, bool isRoot, bool isAbsoluteChild)
        {
            if (built == null)
            {
                return;
            }

            if (isRoot)
            {
                StretchToParent(built.RectTransform);
                built.AbsoluteTopLeftInPage = Vector2.zero;
                built.HasAbsoluteTopLeft = true;
            }
            else if (!isAbsoluteChild)
            {
                built.RectTransform.sizeDelta = size;
            }

            var flowChildren = new List<AIToUGUIBuiltNode>();
            var absoluteChildren = new List<AIToUGUIBuiltNode>();
            for (var i = 0; i < built.Children.Count; i++)
            {
                var child = built.Children[i];
                if (IsAbsolutePosition(child.Node))
                {
                    absoluteChildren.Add(child);
                }
                else
                {
                    flowChildren.Add(child);
                }
            }

            var padding = ParseBox(GetStyle(built.Node, "padding"));
            var innerSize = new Vector2(
                Mathf.Max(0f, size.x - padding.left - padding.right),
                Mathf.Max(0f, size.y - padding.top - padding.bottom));

            var layoutMode = ResolveLayoutMode(built.Node);
            var supportsAuthoritativeFlowRects = layoutMode != AIToUGUILayoutMode.Grid &&
                                                 layoutMode != AIToUGUILayoutMode.Curve;
            var flowChildrenForLayout = flowChildren;

            // Track C hardening: only bypass the normal flow layout pass when the entire flow
            // sibling set is fully backed by measured locked rects. Mixed groups (some locked,
            // some suggested) previously produced a split-brain layout where locked children
            // stayed at snapshot positions while the remaining siblings were re-flowed from a
            // fresh origin, which can create overlap, overflow, and apparent runtime expansion.
            if (supportsAuthoritativeFlowRects &&
                CanBypassFlowLayoutWithMeasuredLocks(built, flowChildren))
            {
                for (var i = 0; i < flowChildren.Count; i++)
                {
                    TryApplyMeasuredLockedRect(built, flowChildren[i], size);
                }

                flowChildrenForLayout = s_emptyBuiltNodeList;
            }

            switch (layoutMode)
            {
                case AIToUGUILayoutMode.Flex:
                    LayoutFlexChildren(built, flowChildrenForLayout, size, innerSize, padding);
                    break;
                case AIToUGUILayoutMode.Grid:
                case AIToUGUILayoutMode.Curve:
                    LayoutSemanticChildren(built, flowChildrenForLayout, size);
                    break;
                default:
                    LayoutBlockChildren(built, flowChildrenForLayout, size, innerSize, padding);
                    break;
            }

            for (var i = 0; i < absoluteChildren.Count; i++)
            {
                var child = absoluteChildren[i];
                if (TryApplyMeasuredLockedRect(built, child, size))
                {
                    continue;
                }

                ResolveChildLayoutContext(built, child, size, out var layoutParentSize, out var layoutParentTopLeftInPage);
                var childSize = MeasureBuiltNode(child, layoutParentSize, false, false, false);
                ApplyAbsoluteRect(child.RectTransform, child.Node, layoutParentSize, childSize);
                child.HasAbsoluteTopLeft = built.HasAbsoluteTopLeft;
                if (child.HasAbsoluteTopLeft &&
                    child.RectTransform != null &&
                    child.RectTransform.parent is RectTransform childParentRect &&
                    TryResolveRectTopLeftInAncestorSpace(child.RectTransform, childParentRect, out var actualTopLeft))
                {
                    child.AbsoluteTopLeftInPage = layoutParentTopLeftInPage + actualTopLeft;
                }
                else
                {
                    child.AbsoluteTopLeftInPage = Vector2.zero;
                    child.HasAbsoluteTopLeft = false;
                }

                LayoutBuiltNode(child, childSize, false, true);
            }
        }

        private static void LayoutSemanticChildren(AIToUGUIBuiltNode parent, List<AIToUGUIBuiltNode> children, Vector2 parentSize)
        {
            if (children == null)
            {
                return;
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == null)
                {
                    continue;
                }

                var childSize = MeasureBuiltNode(child, parentSize, false, false, false);
                child.HasAbsoluteTopLeft = false;
                LayoutBuiltNode(child, childSize, false, false);
            }
        }

        private static bool CanBypassFlowLayoutWithMeasuredLocks(AIToUGUIBuiltNode parent, List<AIToUGUIBuiltNode> children)
        {
            if (parent == null || !parent.HasAbsoluteTopLeft || children == null || children.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child?.Node == null)
                {
                    return false;
                }

                var rect = child.Node.AbsoluteRect;
                if (!rect.IsLockedTo(child.Node.StabilityLevel) ||
                    rect.Width <= 0f ||
                    rect.Height <= 0f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Track C: if the child carries a snapshot-measured rect and is tagged stabilityLevel=locked,
        /// pin its RectTransform to that rect converted to parent-local coordinates. Returns true
        /// on success so the caller skips the normal flex/block/absolute placement for this child.
        /// The child's absoluteRect is authored in page-absolute pixels; the local offset is just
        /// the delta from parent's own AbsoluteTopLeftInPage (padding is already accounted for
        /// by the Python geometry resolver when placing flex/block children).
        /// </summary>
        private static bool TryApplyMeasuredLockedRect(AIToUGUIBuiltNode parent, AIToUGUIBuiltNode child, Vector2 parentSize)
        {
            if (child == null || child.Node == null || parent == null || !parent.HasAbsoluteTopLeft)
            {
                return false;
            }

            var rect = child.Node.AbsoluteRect;
            if (!rect.IsLockedTo(child.Node.StabilityLevel))
            {
                return false;
            }

            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return false;
            }

            var childSize = new Vector2(rect.Width, rect.Height);
            ResolveChildLayoutContext(parent, child, parentSize, out var layoutParentSize, out var layoutParentTopLeftInPage);
            var localX = rect.X - layoutParentTopLeftInPage.x;
            var localY = rect.Y - layoutParentTopLeftInPage.y;

            SetFlowRect(child.RectTransform, new Vector2(localX, localY), childSize, layoutParentSize);
            child.AbsoluteTopLeftInPage = new Vector2(rect.X, rect.Y);
            child.HasAbsoluteTopLeft = true;
            LayoutBuiltNode(child, childSize, false, false);
            return true;
        }

        private static void ResolveChildLayoutContext(
            AIToUGUIBuiltNode parent,
            AIToUGUIBuiltNode child,
            Vector2 fallbackParentSize,
            out Vector2 layoutParentSize,
            out Vector2 layoutParentTopLeftInPage)
        {
            layoutParentSize = fallbackParentSize;
            layoutParentTopLeftInPage = parent != null ? parent.AbsoluteTopLeftInPage : Vector2.zero;

            if (child?.RectTransform == null)
            {
                return;
            }

            var layoutParentRect = child.RectTransform.parent as RectTransform;
            if (layoutParentRect == null)
            {
                return;
            }

            var rectSize = layoutParentRect.rect.size;
            if (rectSize.x > 0.001f && rectSize.y > 0.001f)
            {
                layoutParentSize = rectSize;
            }

            if (parent == null ||
                !parent.HasAbsoluteTopLeft ||
                parent.RectTransform == null ||
                layoutParentRect == parent.RectTransform)
            {
                return;
            }

            if (TryResolveRectTopLeftInAncestorSpace(layoutParentRect, parent.RectTransform, out var topLeftInParent))
            {
                layoutParentTopLeftInPage = parent.AbsoluteTopLeftInPage + topLeftInParent;
            }
        }

        private static bool TryResolveRectTopLeftInAncestorSpace(RectTransform rect, RectTransform ancestor, out Vector2 topLeft)
        {
            topLeft = Vector2.zero;
            if (rect == null || ancestor == null)
            {
                return false;
            }

            var worldCorners = new Vector3[4];
            rect.GetWorldCorners(worldCorners);
            var topLeftInAncestorLocal = ancestor.InverseTransformPoint(worldCorners[1]);
            var ancestorTopLeftLocal = new Vector2(
                -ancestor.rect.width * ancestor.pivot.x,
                ancestor.rect.height * (1f - ancestor.pivot.y));

            topLeft = new Vector2(
                topLeftInAncestorLocal.x - ancestorTopLeftLocal.x,
                ancestorTopLeftLocal.y - topLeftInAncestorLocal.y);
            return true;
        }

        private static void RefreshProceduralAdapters(AIToUGUIBuiltNode built)
        {
            if (built == null || built.GameObject == null)
            {
                return;
            }

            var shapeAdapters = built.GameObject.GetComponentsInChildren<AIToUGUIShapeAdapter>(true);
            for (var i = 0; i < shapeAdapters.Length; i++)
            {
                if (shapeAdapters[i] != null)
                {
                    shapeAdapters[i].ApplyNow();
                }
            }

            var windinatorAdapters = built.GameObject.GetComponentsInChildren<AIToUGUIWindinatorShapeAdapter>(true);
            for (var i = 0; i < windinatorAdapters.Length; i++)
            {
                if (windinatorAdapters[i] != null)
                {
                    windinatorAdapters[i].ApplyNow();
                }
            }
        }

        private static void RefreshTextComponents(AIToUGUIBuiltNode built)
        {
            if (built == null)
            {
                return;
            }

            var text = FindPrimaryText(built.GameObject);
            if (text != null)
            {
                var sizeHint = text.rectTransform.rect.size;
                if (sizeHint.x <= 0f || sizeHint.y <= 0f)
                {
                    sizeHint = built.RectTransform != null ? built.RectTransform.rect.size : Vector2.zero;
                }

                ConfigureTextLayout(text, built.Node, sizeHint);
                text.ForceMeshUpdate();
            }

            for (var i = 0; i < built.Children.Count; i++)
            {
                RefreshTextComponents(built.Children[i]);
            }
        }

        private static Vector2 MeasureBuiltNode(AIToUGUIBuiltNode built, Vector2 availableSize, bool isRoot, bool stretchWidth, bool stretchHeight)
        {
            if (built == null)
            {
                return Vector2.zero;
            }

            if (isRoot)
            {
                return availableSize;
            }

            var node = built.Node;
            var padding = ParseBox(GetStyle(node, "padding"));
            var size = ResolveNodeSize(node, availableSize, new Vector2(-1f, -1f));
            var margins = ParseMargins(node);

            if (IsAbsolutePosition(node))
            {
                var left = ParseLength(GetStyle(node, "left"));
                var right = ParseLength(GetStyle(node, "right"));
                var top = ParseLength(GetStyle(node, "top"));
                var bottom = ParseLength(GetStyle(node, "bottom"));
                if (size.x < 0f && left.IsValid && right.IsValid)
                {
                    size.x = Mathf.Max(0f, availableSize.x - ResolveLength(left, availableSize.x, 0f) - ResolveLength(right, availableSize.x, 0f));
                }

                if (size.y < 0f && top.IsValid && bottom.IsValid)
                {
                    size.y = Mathf.Max(0f, availableSize.y - ResolveLength(top, availableSize.y, 0f) - ResolveLength(bottom, availableSize.y, 0f));
                }
            }

            if (stretchWidth && size.x < 0f)
            {
                size.x = Mathf.Max(0f, availableSize.x);
            }

            if (stretchHeight && size.y < 0f)
            {
                size.y = Mathf.Max(0f, availableSize.y);
            }

            var display = GetStyle(node, "display");
            var flowChildren = built.Children.Where(child => !IsAbsolutePosition(child.Node)).ToList();
            if (display == "flex" && flowChildren.Count > 0)
            {
                var direction = GetStyle(node, "flex-direction");
                var alignItems = GetStyle(node, "align-items");
                var contentAvailable = new Vector2(
                    Mathf.Max(0f, (size.x >= 0f ? size.x : availableSize.x) - padding.left - padding.right),
                    Mathf.Max(0f, (size.y >= 0f ? size.y : availableSize.y) - padding.top - padding.bottom));
                var gap = Mathf.Max(0f, ParseFloat(GetStyle(node, "gap"), 0f));
                var requiredWidth = 0f;
                var requiredHeight = 0f;

                for (var i = 0; i < flowChildren.Count; i++)
                {
                    var child = flowChildren[i];
                    var childMargins = ParseMargins(child.Node);
                    var childAvailable = direction == "row"
                        ? new Vector2(
                            contentAvailable.x,
                            Mathf.Max(0f, contentAvailable.y - ResolveEdge(childMargins.Top, contentAvailable.y) - ResolveEdge(childMargins.Bottom, contentAvailable.y)))
                        : new Vector2(
                            Mathf.Max(0f, contentAvailable.x - ResolveEdge(childMargins.Left, contentAvailable.x) - ResolveEdge(childMargins.Right, contentAvailable.x)),
                            contentAvailable.y);
                    var stretchCrossAxis = string.IsNullOrWhiteSpace(alignItems) || alignItems == "stretch";
                    var childStretchWidth = direction != "row" && stretchCrossAxis;
                    var childStretchHeight = false;
                    var childSize = MeasureBuiltNode(
                        child,
                        childAvailable,
                        false,
                        childStretchWidth,
                        childStretchHeight);

                    requiredWidth = direction == "row"
                        ? requiredWidth + childSize.x + ResolveEdge(childMargins.Left, contentAvailable.x) + ResolveEdge(childMargins.Right, contentAvailable.x) + (i > 0 ? gap : 0f)
                        : Mathf.Max(requiredWidth, childSize.x + ResolveEdge(childMargins.Left, contentAvailable.x) + ResolveEdge(childMargins.Right, contentAvailable.x));

                    requiredHeight = direction == "row"
                        ? Mathf.Max(requiredHeight, childSize.y + ResolveEdge(childMargins.Top, contentAvailable.y) + ResolveEdge(childMargins.Bottom, contentAvailable.y))
                        : requiredHeight + childSize.y + ResolveEdge(childMargins.Top, contentAvailable.y) + ResolveEdge(childMargins.Bottom, contentAvailable.y) + (i > 0 ? gap : 0f);
                }

                if (size.x < 0f)
                {
                    size.x = requiredWidth + padding.left + padding.right;
                }

                if (size.y < 0f)
                {
                    size.y = requiredHeight + padding.top + padding.bottom;
                }
            }
            else if (flowChildren.Count > 0)
            {
                var contentAvailableWidth = Mathf.Max(0f, (size.x >= 0f ? size.x : availableSize.x) - padding.left - padding.right);
                var requiredWidth = 0f;
                var requiredHeight = 0f;
                for (var i = 0; i < flowChildren.Count; i++)
                {
                    var child = flowChildren[i];
                    var childMargins = ParseMargins(child.Node);
                    var childAvailable = new Vector2(
                        Mathf.Max(0f, contentAvailableWidth - ResolveEdge(childMargins.Left, contentAvailableWidth) - ResolveEdge(childMargins.Right, contentAvailableWidth)),
                        availableSize.y);
                    var childSize = MeasureBuiltNode(child, childAvailable, false, !HasExplicitWidth(child.Node), false);
                    requiredWidth = Mathf.Max(requiredWidth, childSize.x + ResolveEdge(childMargins.Left, contentAvailableWidth) + ResolveEdge(childMargins.Right, contentAvailableWidth));
                    requiredHeight += childSize.y + ResolveEdge(childMargins.Top, availableSize.y) + ResolveEdge(childMargins.Bottom, availableSize.y);
                }

                if (size.x < 0f)
                {
                    size.x = requiredWidth + padding.left + padding.right;
                }

                if (size.y < 0f)
                {
                    size.y = requiredHeight + padding.top + padding.bottom;
                }
            }
            else
            {
                var contentSize = MeasureLeafContent(built, availableSize, stretchWidth);
                if (size.x < 0f)
                {
                    size.x = contentSize.x + padding.left + padding.right;
                }

                if (size.y < 0f)
                {
                    size.y = contentSize.y + padding.top + padding.bottom;
                }
            }

            size.x = Mathf.Max(0f, ClampLength(size.x, ParseLength(GetStyle(node, "min-width")), ParseLength(GetStyle(node, "max-width")), availableSize.x));
            size.y = Mathf.Max(0f, ClampLength(size.y, ParseLength(GetStyle(node, "min-height")), ParseLength(GetStyle(node, "max-height")), availableSize.y));
            return size;
        }

        private static Vector2 MeasureLeafContent(AIToUGUIBuiltNode built, Vector2 availableSize, bool stretchWidth)
        {
            var node = built.Node;
            var text = FindPrimaryText(built.GameObject);
            if (text == null)
            {
                return Vector2.zero;
            }

            var padding = ParseBox(GetStyle(node, "padding"));
            var maxWidth = ParseLength(GetStyle(node, "max-width"));
            var explicitWidth = ParseLength(GetStyle(node, "width"));
            var explicitHeight = ParseLength(GetStyle(node, "height"));
            var maxHeight = ParseLength(GetStyle(node, "max-height"));
            var widthConstraint = 4096f;
            if (explicitWidth.IsValid)
            {
                widthConstraint = Mathf.Max(0f, ResolveLength(explicitWidth, availableSize.x, 0f) - padding.left - padding.right);
            }
            else if (stretchWidth)
            {
                widthConstraint = Mathf.Max(0f, availableSize.x - padding.left - padding.right);
            }
            else if (maxWidth.IsValid)
            {
                widthConstraint = ResolveLength(maxWidth, availableSize.x, availableSize.x);
            }
            else if (availableSize.x > 0f)
            {
                widthConstraint = Mathf.Max(0f, availableSize.x - padding.left - padding.right);
            }

            var heightConstraint = 0f;
            if (explicitHeight.IsValid)
            {
                heightConstraint = Mathf.Max(0f, ResolveLength(explicitHeight, availableSize.y, 0f) - padding.top - padding.bottom);
            }
            else if (maxHeight.IsValid)
            {
                heightConstraint = Mathf.Max(0f, ResolveLength(maxHeight, availableSize.y, availableSize.y) - padding.top - padding.bottom);
            }
            else if (availableSize.y > 0f)
            {
                heightConstraint = Mathf.Max(0f, availableSize.y - padding.top - padding.bottom);
            }

            ConfigureTextLayout(text, node, new Vector2(widthConstraint, heightConstraint));
            text.ForceMeshUpdate();
            var preferred = text.GetPreferredValues(text.text, Mathf.Max(1f, widthConstraint), heightConstraint > 0f ? heightConstraint : 0f);
            var width = explicitWidth.IsValid
                ? ResolveLength(explicitWidth, availableSize.x, preferred.x)
                : stretchWidth
                    ? Mathf.Max(0f, availableSize.x - padding.left - padding.right)
                    : maxWidth.IsValid
                        ? Mathf.Min(preferred.x, ResolveLength(maxWidth, availableSize.x, preferred.x))
                        : preferred.x;
            if (widthConstraint > 0f && widthConstraint < 4096f)
            {
                width = Mathf.Min(width, widthConstraint);
            }

            var height = ClampMeasuredTextHeight(node, text.fontSize, preferred.y, heightConstraint);
            return new Vector2(width, height);
        }

        private static void LayoutFlexChildren(AIToUGUIBuiltNode parent, List<AIToUGUIBuiltNode> children, Vector2 parentSize, Vector2 innerSize, RectOffset padding)
        {
            if (children == null || children.Count == 0)
            {
                return;
            }

            var direction = GetStyle(parent.Node, "flex-direction");
            var justify = GetStyle(parent.Node, "justify-content");
            var align = GetStyle(parent.Node, "align-items");
            var gap = Mathf.Max(0f, ParseFloat(GetStyle(parent.Node, "gap"), 0f));

            if (direction == "row")
            {
                LayoutRowChildren(parent, children, parentSize, innerSize, padding, justify, align, gap);
                return;
            }

            LayoutColumnChildren(parent, children, parentSize, innerSize, padding, justify, align, gap);
        }

        private static void LayoutRowChildren(AIToUGUIBuiltNode parent, List<AIToUGUIBuiltNode> children, Vector2 parentSize, Vector2 innerSize, RectOffset padding, string justify, string align, float gap)
        {
            var childSizes = new Vector2[children.Count];
            var childMargins = new AIToUGUICssEdges[children.Count];
            var totalFixedWidth = 0f;
            var maxHeight = 0f;
            var firstAutoLeftIndex = -1;

            for (var i = 0; i < children.Count; i++)
            {
                childMargins[i] = ParseMargins(children[i].Node);
                var childAvailable = new Vector2(
                    innerSize.x,
                    Mathf.Max(0f, innerSize.y - ResolveEdge(childMargins[i].Top, innerSize.y) - ResolveEdge(childMargins[i].Bottom, innerSize.y)));
                childSizes[i] = MeasureBuiltNode(
                    children[i],
                    childAvailable,
                    false,
                    false,
                    false);
                totalFixedWidth += childSizes[i].x + ResolveEdge(childMargins[i].Left, innerSize.x) + ResolveEdge(childMargins[i].Right, innerSize.x);
                maxHeight = Mathf.Max(maxHeight, childSizes[i].y + ResolveEdge(childMargins[i].Top, innerSize.y) + ResolveEdge(childMargins[i].Bottom, innerSize.y));
                if (firstAutoLeftIndex < 0 && childMargins[i].Left.IsAuto)
                {
                    firstAutoLeftIndex = i;
                }
            }

            ApplyFlexShrink(children, childSizes, childMargins, innerSize.x, gap, true);
            totalFixedWidth = CalculateMainAxisFootprint(children, childSizes, childMargins, innerSize.x, true);

            float x = padding.left;
            var actualGap = gap;
            if (firstAutoLeftIndex < 0)
            {
                if (justify == "space-between" && children.Count > 1)
                {
                    actualGap = Mathf.Max(gap, (innerSize.x - totalFixedWidth) / (children.Count - 1));
                }
                else if (justify == "center")
                {
                    x += Mathf.Max(0f, (innerSize.x - (totalFixedWidth + gap * Mathf.Max(0, children.Count - 1))) * 0.5f);
                }
                else if (justify == "flex-end" || justify == "end")
                {
                    x += Mathf.Max(0f, innerSize.x - (totalFixedWidth + gap * Mathf.Max(0, children.Count - 1)));
                }
            }

            float rightCursor = padding.left + innerSize.x;
            if (firstAutoLeftIndex >= 0)
            {
                rightCursor = padding.left + innerSize.x;
                for (var i = children.Count - 1; i >= firstAutoLeftIndex; i--)
                {
                    rightCursor -= ResolveEdge(childMargins[i].Right, innerSize.x) + childSizes[i].x;
                    var y = ResolveCrossAxisPosition(align, padding.top, innerSize.y, childSizes[i].y, childMargins[i], false);
                    PlaceFlowChild(parent, children[i], new Vector2(rightCursor, y), childSizes[i], parentSize);
                    LayoutBuiltNode(children[i], childSizes[i], false, false);
                    rightCursor -= ResolveEdge(childMargins[i].Left, innerSize.x) + gap;
                }
            }

            var lastLeftIndex = firstAutoLeftIndex >= 0 ? firstAutoLeftIndex : children.Count;
            for (var i = 0; i < lastLeftIndex; i++)
            {
                x += ResolveEdge(childMargins[i].Left, innerSize.x);
                var y = ResolveCrossAxisPosition(align, padding.top, innerSize.y, childSizes[i].y, childMargins[i], false);
                PlaceFlowChild(parent, children[i], new Vector2(x, y), childSizes[i], parentSize);
                LayoutBuiltNode(children[i], childSizes[i], false, false);
                x += childSizes[i].x + ResolveEdge(childMargins[i].Right, innerSize.x);
                if (i < lastLeftIndex - 1)
                {
                    x += actualGap;
                }
            }
        }

        private static void LayoutColumnChildren(AIToUGUIBuiltNode parent, List<AIToUGUIBuiltNode> children, Vector2 parentSize, Vector2 innerSize, RectOffset padding, string justify, string align, float gap)
        {
            var childSizes = new Vector2[children.Count];
            var childMargins = new AIToUGUICssEdges[children.Count];
            var totalFixedHeight = 0f;
            var firstAutoTopIndex = -1;

            for (var i = 0; i < children.Count; i++)
            {
                childMargins[i] = ParseMargins(children[i].Node);
                var childAvailable = new Vector2(
                    Mathf.Max(0f, innerSize.x - ResolveEdge(childMargins[i].Left, innerSize.x) - ResolveEdge(childMargins[i].Right, innerSize.x)),
                    innerSize.y);
                childSizes[i] = MeasureBuiltNode(
                    children[i],
                    childAvailable,
                    false,
                    string.IsNullOrWhiteSpace(align) || align == "stretch",
                    false);
                totalFixedHeight += childSizes[i].y + ResolveEdge(childMargins[i].Top, innerSize.y) + ResolveEdge(childMargins[i].Bottom, innerSize.y);
                if (firstAutoTopIndex < 0 && childMargins[i].Top.IsAuto)
                {
                    firstAutoTopIndex = i;
                }
            }

            ApplyFlexShrink(children, childSizes, childMargins, innerSize.y, gap, false);
            totalFixedHeight = CalculateMainAxisFootprint(children, childSizes, childMargins, innerSize.y, false);

            float y = padding.top;
            var actualGap = gap;
            if (firstAutoTopIndex < 0)
            {
                if (justify == "space-between" && children.Count > 1)
                {
                    actualGap = Mathf.Max(gap, (innerSize.y - totalFixedHeight) / (children.Count - 1));
                }
                else if (justify == "center")
                {
                    y += Mathf.Max(0f, (innerSize.y - (totalFixedHeight + gap * Mathf.Max(0, children.Count - 1))) * 0.5f);
                }
                else if (justify == "flex-end" || justify == "end")
                {
                    y += Mathf.Max(0f, innerSize.y - (totalFixedHeight + gap * Mathf.Max(0, children.Count - 1)));
                }
            }

            float bottomCursor = padding.top + innerSize.y;
            if (firstAutoTopIndex >= 0)
            {
                bottomCursor = padding.top + innerSize.y;
                for (var i = children.Count - 1; i >= firstAutoTopIndex; i--)
                {
                    bottomCursor -= ResolveEdge(childMargins[i].Bottom, innerSize.y) + childSizes[i].y;
                    var x = ResolveCrossAxisPosition(align, padding.left, innerSize.x, childSizes[i].x, childMargins[i], true);
                    PlaceFlowChild(parent, children[i], new Vector2(x, bottomCursor), childSizes[i], parentSize);
                    LayoutBuiltNode(children[i], childSizes[i], false, false);
                    bottomCursor -= ResolveEdge(childMargins[i].Top, innerSize.y) + gap;
                }
            }

            var lastTopIndex = firstAutoTopIndex >= 0 ? firstAutoTopIndex : children.Count;
            for (var i = 0; i < lastTopIndex; i++)
            {
                y += ResolveEdge(childMargins[i].Top, innerSize.y);
                var x = ResolveCrossAxisPosition(align, padding.left, innerSize.x, childSizes[i].x, childMargins[i], true);
                PlaceFlowChild(parent, children[i], new Vector2(x, y), childSizes[i], parentSize);
                LayoutBuiltNode(children[i], childSizes[i], false, false);
                y += childSizes[i].y + ResolveEdge(childMargins[i].Bottom, innerSize.y);
                if (i < lastTopIndex - 1)
                {
                    y += actualGap;
                }
            }
        }

        private static void ApplyFlexShrink(List<AIToUGUIBuiltNode> children, Vector2[] childSizes, AIToUGUICssEdges[] childMargins, float availableMain, float gap, bool horizontal)
        {
            if (children == null || childSizes == null || childMargins == null || children.Count == 0 || availableMain <= 0f)
            {
                return;
            }

            var gapTotal = gap * Mathf.Max(0, children.Count - 1);
            var occupied = CalculateMainAxisFootprint(children, childSizes, childMargins, availableMain, horizontal) + gapTotal;
            var overflow = occupied - availableMain;
            if (overflow <= 0.01f)
            {
                return;
            }

            var weights = new float[children.Count];
            for (var pass = 0; pass < children.Count && overflow > 0.01f; pass++)
            {
                var totalWeight = 0f;
                for (var i = 0; i < children.Count; i++)
                {
                    var currentMain = horizontal ? childSizes[i].x : childSizes[i].y;
                    var minMain = ResolveFlexMinSize(children[i].Node, availableMain, horizontal);
                    if (currentMain <= minMain + 0.01f)
                    {
                        weights[i] = 0f;
                        continue;
                    }

                    var shrink = ResolveFlexShrink(children[i].Node);
                    weights[i] = Mathf.Max(0f, shrink) * Mathf.Max(1f, currentMain);
                    totalWeight += weights[i];
                }

                if (totalWeight <= 0.01f)
                {
                    break;
                }

                var consumed = 0f;
                for (var i = 0; i < children.Count; i++)
                {
                    if (weights[i] <= 0f)
                    {
                        continue;
                    }

                    var currentMain = horizontal ? childSizes[i].x : childSizes[i].y;
                    var minMain = ResolveFlexMinSize(children[i].Node, availableMain, horizontal);
                    var reducible = Mathf.Max(0f, currentMain - minMain);
                    if (reducible <= 0.01f)
                    {
                        continue;
                    }

                    var targetReduction = overflow * (weights[i] / totalWeight);
                    var appliedReduction = Mathf.Min(reducible, targetReduction);
                    if (appliedReduction <= 0.01f)
                    {
                        continue;
                    }

                    if (horizontal)
                    {
                        childSizes[i].x = Mathf.Max(minMain, childSizes[i].x - appliedReduction);
                    }
                    else
                    {
                        childSizes[i].y = Mathf.Max(minMain, childSizes[i].y - appliedReduction);
                    }

                    consumed += appliedReduction;
                }

                if (consumed <= 0.01f)
                {
                    break;
                }

                overflow -= consumed;
            }
        }

        private static float CalculateMainAxisFootprint(List<AIToUGUIBuiltNode> children, Vector2[] childSizes, AIToUGUICssEdges[] childMargins, float parentMainSize, bool horizontal)
        {
            float total = 0f;
            for (var i = 0; i < children.Count; i++)
            {
                total += horizontal
                    ? childSizes[i].x + ResolveEdge(childMargins[i].Left, parentMainSize) + ResolveEdge(childMargins[i].Right, parentMainSize)
                    : childSizes[i].y + ResolveEdge(childMargins[i].Top, parentMainSize) + ResolveEdge(childMargins[i].Bottom, parentMainSize);
            }

            return total;
        }

        private static float ResolveFlexShrink(AIToUGUICompiledNode node)
        {
            var explicitShrink = ParseFloat(GetStyle(node, "flex-shrink"), float.NaN);
            if (!float.IsNaN(explicitShrink))
            {
                return Mathf.Max(0f, explicitShrink);
            }

            var flex = GetStyle(node, "flex");
            if (string.Equals(flex, "none", StringComparison.OrdinalIgnoreCase))
            {
                return 0f;
            }

            return 1f;
        }

        private static float ResolveFlexMinSize(AIToUGUICompiledNode node, float parentMainSize, bool horizontal)
        {
            var min = ParseLength(GetStyle(node, horizontal ? "min-width" : "min-height"));
            return Mathf.Max(0f, ResolveLength(min, parentMainSize, 0f));
        }

        private static void LayoutBlockChildren(AIToUGUIBuiltNode parent, List<AIToUGUIBuiltNode> children, Vector2 parentSize, Vector2 innerSize, RectOffset padding)
        {
            if (children == null || children.Count == 0)
            {
                return;
            }

            float y = padding.top;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var margins = ParseMargins(child.Node);
                var childSize = MeasureBuiltNode(
                    child,
                    new Vector2(
                        Mathf.Max(0f, innerSize.x - ResolveEdge(margins.Left, innerSize.x) - ResolveEdge(margins.Right, innerSize.x)),
                        innerSize.y),
                    false,
                    !HasExplicitWidth(child.Node),
                    false);
                y += ResolveEdge(margins.Top, innerSize.y);
                float x = padding.left + ResolveEdge(margins.Left, innerSize.x);
                PlaceFlowChild(parent, child, new Vector2(x, y), childSize, parentSize);
                LayoutBuiltNode(child, childSize, false, false);
                y += childSize.y + ResolveEdge(margins.Bottom, innerSize.y);
            }
        }

        private static float ResolveCrossAxisPosition(string align, float startOffset, float innerSize, float childSize, AIToUGUICssEdges margins, bool horizontalAxis)
        {
            var leading = horizontalAxis ? ResolveEdge(margins.Left, innerSize) : ResolveEdge(margins.Top, innerSize);
            var trailing = horizontalAxis ? ResolveEdge(margins.Right, innerSize) : ResolveEdge(margins.Bottom, innerSize);
            if (align == "center")
            {
                return startOffset + Mathf.Max(0f, (innerSize - childSize) * 0.5f) + leading - trailing;
            }

            if (align == "flex-end" || align == "end")
            {
                return startOffset + Mathf.Max(0f, innerSize - childSize - trailing);
            }

            return startOffset + leading;
        }

        private static void SetFlowRect(RectTransform rect, Vector2 topLeftPosition, Vector2 size, Vector2 parentSize)
        {
            var safeSize = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
            var left = new AIToUGUICssLength(Mathf.Max(0f, topLeftPosition.x), AIToUGUILengthUnit.Pixel);
            var rightValue = Mathf.Max(0f, parentSize.x - topLeftPosition.x - safeSize.x);
            var right = new AIToUGUICssLength(rightValue, AIToUGUILengthUnit.Pixel);
            var top = new AIToUGUICssLength(Mathf.Max(0f, topLeftPosition.y), AIToUGUILengthUnit.Pixel);
            var bottomValue = Mathf.Max(0f, parentSize.y - topLeftPosition.y - safeSize.y);
            var bottom = new AIToUGUICssLength(bottomValue, AIToUGUILengthUnit.Pixel);

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            var horizontalMode = ResolveHorizontalAnchorMode(left, right, safeSize.x, parentSize.x);
            ApplyHorizontalAnchor(rect, horizontalMode, left, safeSize.x, parentSize.x);

            var verticalMode = ResolveVerticalAnchorMode(top, bottom, safeSize.y, parentSize.y);
            ApplyVerticalAnchor(rect, verticalMode, top, safeSize.y, parentSize.y);
        }

        /// <summary>
        /// Track C helper: position a flow child through SetFlowRect AND propagate the child's
        /// absolute top-left (in page-root coordinates) so any descendant that itself carries a
        /// snapshot-measured locked rect can correctly compute parent-local offsets. The
        /// topLeftPosition passed here already includes any parent padding.
        /// </summary>
        private static void PlaceFlowChild(AIToUGUIBuiltNode parent, AIToUGUIBuiltNode child, Vector2 topLeftPosition, Vector2 size, Vector2 parentSize)
        {
            SetFlowRect(child.RectTransform, topLeftPosition, size, parentSize);
            if (parent != null && parent.HasAbsoluteTopLeft)
            {
                child.AbsoluteTopLeftInPage = new Vector2(
                    parent.AbsoluteTopLeftInPage.x + topLeftPosition.x,
                    parent.AbsoluteTopLeftInPage.y + topLeftPosition.y);
                child.HasAbsoluteTopLeft = true;
            }
        }

        private static void ApplyAbsoluteRect(RectTransform rect, AIToUGUICompiledNode node, Vector2 parentSize, Vector2 size)
        {
            var left = ParseLength(GetStyle(node, "left"));
            var right = ParseLength(GetStyle(node, "right"));
            var top = ParseLength(GetStyle(node, "top"));
            var bottom = ParseLength(GetStyle(node, "bottom"));
            var stretchX = !ParseLength(GetStyle(node, "width")).IsValid && left.IsValid && right.IsValid;
            var stretchY = !ParseLength(GetStyle(node, "height")).IsValid && top.IsValid && bottom.IsValid;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            if (stretchX)
            {
                rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
                rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
                rect.pivot = new Vector2(0.5f, rect.pivot.y);
                rect.offsetMin = new Vector2(ResolveLength(left, parentSize.x, 0f), rect.offsetMin.y);
                rect.offsetMax = new Vector2(-ResolveLength(right, parentSize.x, 0f), rect.offsetMax.y);
            }
            else if (right.IsValid && !left.IsValid)
            {
                rect.anchorMin = new Vector2(1f, rect.anchorMin.y);
                rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
                rect.pivot = new Vector2(1f, rect.pivot.y);
                rect.sizeDelta = size;
                rect.anchoredPosition = new Vector2(-ResolveLength(right, parentSize.x, 0f), rect.anchoredPosition.y);
            }
            else
            {
                var horizontalMode = ResolveHorizontalAnchorMode(left, right, size.x, parentSize.x);
                ApplyHorizontalAnchor(rect, horizontalMode, left, size.x, parentSize.x);
            }

            if (stretchY)
            {
                rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
                rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
                rect.pivot = new Vector2(rect.pivot.x, 0.5f);
                rect.offsetMin = new Vector2(rect.offsetMin.x, ResolveLength(bottom, parentSize.y, 0f));
                rect.offsetMax = new Vector2(rect.offsetMax.x, -ResolveLength(top, parentSize.y, 0f));
            }
            else if (bottom.IsValid && !top.IsValid)
            {
                rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
                rect.anchorMax = new Vector2(rect.anchorMax.x, 0f);
                rect.pivot = new Vector2(rect.pivot.x, 0f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, size.y);
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, ResolveLength(bottom, parentSize.y, 0f));
            }
            else
            {
                var verticalMode = ResolveVerticalAnchorMode(top, bottom, size.y, parentSize.y);
                ApplyVerticalAnchor(rect, verticalMode, top, size.y, parentSize.y);
            }
        }

        private static AIToUGUICssEdges ParseMargins(AIToUGUICompiledNode node)
        {
            return ParseEdges(
                GetStyle(node, "margin"),
                GetStyle(node, "margin-left"),
                GetStyle(node, "margin-right"),
                GetStyle(node, "margin-top"),
                GetStyle(node, "margin-bottom"));
        }

        private static AIToUGUICssEdges ParseEdges(string shorthand, string leftOverride, string rightOverride, string topOverride, string bottomOverride)
        {
            var left = new AIToUGUICssLength(0f, AIToUGUILengthUnit.None);
            var right = new AIToUGUICssLength(0f, AIToUGUILengthUnit.None);
            var top = new AIToUGUICssLength(0f, AIToUGUILengthUnit.None);
            var bottom = new AIToUGUICssLength(0f, AIToUGUILengthUnit.None);

            if (!string.IsNullOrWhiteSpace(shorthand))
            {
                var parts = shorthand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var values = parts.Select(ParseLength).ToArray();
                switch (values.Length)
                {
                    case 1:
                        top = right = bottom = left = values[0];
                        break;
                    case 2:
                        top = bottom = values[0];
                        right = left = values[1];
                        break;
                    case 3:
                        top = values[0];
                        right = left = values[1];
                        bottom = values[2];
                        break;
                    default:
                        top = values[0];
                        right = values[1];
                        bottom = values[2];
                        left = values[3];
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(leftOverride))
            {
                left = ParseLength(leftOverride);
            }

            if (!string.IsNullOrWhiteSpace(rightOverride))
            {
                right = ParseLength(rightOverride);
            }

            if (!string.IsNullOrWhiteSpace(topOverride))
            {
                top = ParseLength(topOverride);
            }

            if (!string.IsNullOrWhiteSpace(bottomOverride))
            {
                bottom = ParseLength(bottomOverride);
            }

            return new AIToUGUICssEdges(left, right, top, bottom);
        }

        private static float ResolveEdge(AIToUGUICssLength edge, float parentLength)
        {
            return edge.IsAuto ? 0f : ResolveLength(edge, parentLength, 0f);
        }

        private static bool HasExplicitWidth(AIToUGUICompiledNode node)
        {
            return ParseLength(GetStyle(node, "width")).IsValid;
        }

        private static bool HasExplicitHeight(AIToUGUICompiledNode node)
        {
            return ParseLength(GetStyle(node, "height")).IsValid;
        }

        private static bool IsAbsolutePosition(AIToUGUICompiledNode node)
        {
            var position = GetStyle(node, "position");
            return position == "absolute" || position == "fixed";
        }

        private static TextMeshProUGUI FindPrimaryText(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            if (go.TryGetComponent<TextMeshProUGUI>(out var directText))
            {
                return directText;
            }

            var label = go.transform.Find($"{InternalNodePrefix}Label");
            if (label != null && label.TryGetComponent<TextMeshProUGUI>(out var labelText))
            {
                return labelText;
            }

            var primitiveRoot = go.transform.Find(InternalPrimitiveRootName);
            if (primitiveRoot != null)
            {
                var primitiveLabel = primitiveRoot.Find($"{InternalNodePrefix}Label");
                if (primitiveLabel != null && primitiveLabel.TryGetComponent<TextMeshProUGUI>(out var primitiveLabelText))
                {
                    return primitiveLabelText;
                }

                var primitiveText = primitiveRoot.GetComponentInChildren<TextMeshProUGUI>(true);
                if (primitiveText != null)
                {
                    return primitiveText;
                }
            }

            return null;
        }

        private static void ConfigureRectTransform(RectTransform rect, AIToUGUICompiledNode node, Vector2 parentSize, bool isRoot)
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            if (isRoot)
            {
                StretchToParent(rect);
                return;
            }

            var position = GetStyle(node, "position");
            if (position == "absolute" || position == "fixed")
            {
                ConfigureAbsoluteRect(rect, node, parentSize);
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;

            var size = ResolveNodeSize(node, parentSize, new Vector2(-1f, -1f));
            if (size.x >= 0f || size.y >= 0f)
            {
                rect.sizeDelta = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
            }
        }

        private static void ConfigureAbsoluteRect(RectTransform rect, AIToUGUICompiledNode node, Vector2 parentSize)
        {
            var width = ParseLength(GetStyle(node, "width"));
            var height = ParseLength(GetStyle(node, "height"));
            var left = ParseLength(GetStyle(node, "left"));
            var right = ParseLength(GetStyle(node, "right"));
            var top = ParseLength(GetStyle(node, "top"));
            var bottom = ParseLength(GetStyle(node, "bottom"));
            var size = ResolveNodeSize(node, parentSize, new Vector2(0f, 0f));

            var stretchX = !width.IsValid && left.IsValid && right.IsValid;
            var stretchY = !height.IsValid && top.IsValid && bottom.IsValid;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            if (stretchX)
            {
                rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
                rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
                rect.pivot = new Vector2(0.5f, rect.pivot.y);
                rect.offsetMin = new Vector2(ResolveLength(left, parentSize.x, 0f), rect.offsetMin.y);
                rect.offsetMax = new Vector2(-ResolveLength(right, parentSize.x, 0f), rect.offsetMax.y);
            }
            else if (right.IsValid && !left.IsValid)
            {
                rect.anchorMin = new Vector2(1f, rect.anchorMin.y);
                rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
                rect.pivot = new Vector2(1f, rect.pivot.y);
                rect.sizeDelta = new Vector2(Mathf.Max(0f, size.x), rect.sizeDelta.y);
                rect.anchoredPosition = new Vector2(-ResolveLength(right, parentSize.x, 0f), rect.anchoredPosition.y);
            }
            else
            {
                var horizontalMode = ResolveHorizontalAnchorMode(left, right, Mathf.Max(0f, size.x), parentSize.x);
                ApplyHorizontalAnchor(rect, horizontalMode, left, Mathf.Max(0f, size.x), parentSize.x);
            }

            if (stretchY)
            {
                rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
                rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
                rect.pivot = new Vector2(rect.pivot.x, 0.5f);
                rect.offsetMin = new Vector2(rect.offsetMin.x, ResolveLength(bottom, parentSize.y, 0f));
                rect.offsetMax = new Vector2(rect.offsetMax.x, -ResolveLength(top, parentSize.y, 0f));
            }
            else if (bottom.IsValid && !top.IsValid)
            {
                rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
                rect.anchorMax = new Vector2(rect.anchorMax.x, 0f);
                rect.pivot = new Vector2(rect.pivot.x, 0f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(0f, size.y));
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, ResolveLength(bottom, parentSize.y, 0f));
            }
            else
            {
                var verticalMode = ResolveVerticalAnchorMode(top, bottom, Mathf.Max(0f, size.y), parentSize.y);
                ApplyVerticalAnchor(rect, verticalMode, top, Mathf.Max(0f, size.y), parentSize.y);
            }
        }

        private enum AIToUGUIAnchorMode
        {
            Start,
            Center,
            End
        }

        private static AIToUGUIAnchorMode ResolveHorizontalAnchorMode(AIToUGUICssLength left, AIToUGUICssLength right, float width, float parentWidth)
        {
            if (right.IsValid && !left.IsValid)
            {
                return AIToUGUIAnchorMode.End;
            }

            var resolvedLeft = ResolveLength(left, parentWidth, 0f);
            var safeWidth = Mathf.Max(0f, width);
            var resolvedRight = right.IsValid
                ? ResolveLength(right, parentWidth, 0f)
                : Mathf.Max(0f, parentWidth - resolvedLeft - safeWidth);
            var center = resolvedLeft + safeWidth * 0.5f;
            var centerRatio = parentWidth > 0.001f ? center / parentWidth : 0.5f;
            var marginDelta = Mathf.Abs(resolvedLeft - resolvedRight);

            if (centerRatio >= 0.34f &&
                centerRatio <= 0.66f &&
                marginDelta <= Mathf.Max(48f, parentWidth * 0.18f))
            {
                return AIToUGUIAnchorMode.Center;
            }

            if (resolvedLeft <= Mathf.Max(48f, parentWidth * 0.06f))
            {
                return AIToUGUIAnchorMode.Start;
            }

            if (resolvedRight <= Mathf.Max(48f, parentWidth * 0.06f))
            {
                return AIToUGUIAnchorMode.End;
            }

            return resolvedRight < resolvedLeft ? AIToUGUIAnchorMode.End : AIToUGUIAnchorMode.Start;
        }

        private static AIToUGUIAnchorMode ResolveVerticalAnchorMode(AIToUGUICssLength top, AIToUGUICssLength bottom, float height, float parentHeight)
        {
            if (bottom.IsValid && !top.IsValid)
            {
                return AIToUGUIAnchorMode.End;
            }

            var resolvedTop = ResolveLength(top, parentHeight, 0f);
            var safeHeight = Mathf.Max(0f, height);
            var resolvedBottom = bottom.IsValid
                ? ResolveLength(bottom, parentHeight, 0f)
                : Mathf.Max(0f, parentHeight - resolvedTop - safeHeight);
            var center = resolvedTop + safeHeight * 0.5f;
            var centerRatio = parentHeight > 0.001f ? center / parentHeight : 0.5f;
            var marginDelta = Mathf.Abs(resolvedTop - resolvedBottom);

            if (centerRatio >= 0.34f &&
                centerRatio <= 0.66f &&
                marginDelta <= Mathf.Max(36f, parentHeight * 0.18f))
            {
                return AIToUGUIAnchorMode.Center;
            }

            if (resolvedTop <= Mathf.Max(36f, parentHeight * 0.06f))
            {
                return AIToUGUIAnchorMode.Start;
            }

            if (resolvedBottom <= Mathf.Max(36f, parentHeight * 0.06f))
            {
                return AIToUGUIAnchorMode.End;
            }

            return resolvedBottom < resolvedTop ? AIToUGUIAnchorMode.End : AIToUGUIAnchorMode.Start;
        }

        private static void ApplyHorizontalAnchor(RectTransform rect, AIToUGUIAnchorMode mode, AIToUGUICssLength left, float width, float parentWidth)
        {
            var resolvedLeft = ResolveLength(left, parentWidth, 0f);
            var safeWidth = Mathf.Max(0f, width);
            rect.sizeDelta = new Vector2(safeWidth, rect.sizeDelta.y);

            switch (mode)
            {
                case AIToUGUIAnchorMode.Center:
                    rect.anchorMin = new Vector2(0.5f, rect.anchorMin.y);
                    rect.anchorMax = new Vector2(0.5f, rect.anchorMax.y);
                    rect.pivot = new Vector2(0.5f, rect.pivot.y);
                    rect.anchoredPosition = new Vector2(resolvedLeft + safeWidth * 0.5f - parentWidth * 0.5f, rect.anchoredPosition.y);
                    break;
                case AIToUGUIAnchorMode.End:
                    rect.anchorMin = new Vector2(1f, rect.anchorMin.y);
                    rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
                    rect.pivot = new Vector2(1f, rect.pivot.y);
                    rect.anchoredPosition = new Vector2(-(parentWidth - resolvedLeft - safeWidth), rect.anchoredPosition.y);
                    break;
                default:
                    rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
                    rect.anchorMax = new Vector2(0f, rect.anchorMax.y);
                    rect.pivot = new Vector2(0f, rect.pivot.y);
                    rect.anchoredPosition = new Vector2(resolvedLeft, rect.anchoredPosition.y);
                    break;
            }
        }

        private static void ApplyVerticalAnchor(RectTransform rect, AIToUGUIAnchorMode mode, AIToUGUICssLength top, float height, float parentHeight)
        {
            var resolvedTop = ResolveLength(top, parentHeight, 0f);
            var safeHeight = Mathf.Max(0f, height);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, safeHeight);

            switch (mode)
            {
                case AIToUGUIAnchorMode.Center:
                    rect.anchorMin = new Vector2(rect.anchorMin.x, 0.5f);
                    rect.anchorMax = new Vector2(rect.anchorMax.x, 0.5f);
                    rect.pivot = new Vector2(rect.pivot.x, 0.5f);
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, parentHeight * 0.5f - (resolvedTop + safeHeight * 0.5f));
                    break;
                case AIToUGUIAnchorMode.End:
                    rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
                    rect.anchorMax = new Vector2(rect.anchorMax.x, 0f);
                    rect.pivot = new Vector2(rect.pivot.x, 0f);
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, parentHeight - resolvedTop - safeHeight);
                    break;
                default:
                    rect.anchorMin = new Vector2(rect.anchorMin.x, 1f);
                    rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
                    rect.pivot = new Vector2(rect.pivot.x, 1f);
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -resolvedTop);
                    break;
            }
        }

        private static void ConfigureLayout(RectTransform rect, AIToUGUICompiledNode node, Vector2 parentSize)
        {
            var layoutMode = ResolveSemanticLayoutMode(node);
            switch (layoutMode)
            {
                case AIToUGUILayoutMode.Flex:
                    ConfigureFlexalonFlexibleLayout(rect, node);
                    break;
                case AIToUGUILayoutMode.Grid:
                    ConfigureFlexalonGridLayout(rect, node);
                    break;
                case AIToUGUILayoutMode.Curve:
                    ConfigureFlexalonCurveLayout(rect, node, parentSize);
                    break;
                default:
                    ClearSemanticLayoutComponents(rect.gameObject);
                    break;
            }

            ApplyLayoutElement(rect.gameObject, node, parentSize);
        }

        private static void ConfigureSemanticLayoutChildParticipation(AIToUGUICompiledNode parentNode, AIToUGUIBuiltNode builtChild)
        {
            if (parentNode == null || builtChild == null || builtChild.GameObject == null)
            {
                return;
            }

            if (!IsSemanticLayoutMode(ResolveSemanticLayoutMode(parentNode)))
            {
                return;
            }

            var shouldSkipLayout = ShouldSkipSemanticLayoutChild(builtChild.Node);
            var flexalonObject = builtChild.GameObject.GetComponent<global::Flexalon.FlexalonObject>();
            if (shouldSkipLayout)
            {
                flexalonObject ??= GetOrAddFlexalonComponent<global::Flexalon.FlexalonObject>(builtChild.GameObject);
                flexalonObject.SkipLayout = true;
            }
            else if (flexalonObject != null && flexalonObject.SkipLayout)
            {
                flexalonObject.SkipLayout = false;
            }

            if (builtChild.GameObject.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                layoutElement.ignoreLayout = shouldSkipLayout;
            }
        }

        private static bool ShouldSkipSemanticLayoutChild(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            var explicitLayoutItem = GetAttribute(node, "data-ui-layout-item").Trim().ToLowerInvariant();
            if (explicitLayoutItem == "false" || explicitLayoutItem == "0" || explicitLayoutItem == "no")
            {
                return true;
            }

            if (explicitLayoutItem == "true" || explicitLayoutItem == "1" || explicitLayoutItem == "yes")
            {
                return false;
            }

            return IsAbsolutePosition(node);
        }

        private static bool IsSemanticLayoutMode(AIToUGUILayoutMode mode)
        {
            return mode == AIToUGUILayoutMode.Flex ||
                   mode == AIToUGUILayoutMode.Grid ||
                   mode == AIToUGUILayoutMode.Curve;
        }

        private static AIToUGUILayoutMode ResolveLayoutMode(AIToUGUICompiledNode node)
        {
            var explicitLayout = GetAttribute(node, "data-ui-layout").Trim().ToLowerInvariant();
            switch (explicitLayout)
            {
                case "flex":
                    return AIToUGUILayoutMode.Flex;
                case "grid":
                    return AIToUGUILayoutMode.Grid;
                case "curve":
                    return AIToUGUILayoutMode.Curve;
            }

            return ShouldInferSemanticFlexLayout(node)
                ? AIToUGUILayoutMode.Flex
                : AIToUGUILayoutMode.Block;
        }

        // Static HTML/CSS flex restoration is handled by the baker's own measure/layout pass.
        // Live semantic layout components are opt-in only via explicit data-ui-layout to avoid
        // Flexalon re-owning authored bundle geometry during editor preview/import.
        private static AIToUGUILayoutMode ResolveSemanticLayoutMode(AIToUGUICompiledNode node)
        {
            var explicitLayout = GetAttribute(node, "data-ui-layout").Trim().ToLowerInvariant();
            switch (explicitLayout)
            {
                case "flex":
                    return AIToUGUILayoutMode.Flex;
                case "grid":
                    return AIToUGUILayoutMode.Grid;
                case "curve":
                    return AIToUGUILayoutMode.Curve;
                default:
                    return AIToUGUILayoutMode.Block;
            }
        }

        private static bool ShouldInferSemanticFlexLayout(AIToUGUICompiledNode node)
        {
            if (!string.Equals(GetStyle(node, "display"), "flex", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (node?.Children == null)
            {
                return false;
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (child != null && !IsAbsolutePosition(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ConfigureFlexalonFlexibleLayout(RectTransform rect, AIToUGUICompiledNode node)
        {
            ClearSemanticLayoutComponents(rect.gameObject);

            var layout = GetOrAddFlexalonComponent<global::Flexalon.FlexalonFlexibleLayout>(rect.gameObject);

            var direction = ResolveFlexalonDirection(GetStyle(node, "flex-direction"));
            var justifyContent = GetStyle(node, "justify-content");
            var alignItems = GetStyle(node, "align-items");
            var flexWrap = GetStyle(node, "flex-wrap");
            var gap = Mathf.Max(0f, ParseFloat(GetStyle(node, "gap"), 0f));

            layout.Direction = direction;
            layout.Wrap = IsFlexWrapEnabled(flexWrap);
            layout.WrapDirection = ResolveFlexalonWrapDirection(direction, flexWrap);
            layout.GapType = ResolveFlexalonGapOptions(justifyContent);
            layout.Gap = gap;
            layout.WrapGapType = global::Flexalon.FlexalonFlexibleLayout.GapOptions.Fixed;
            layout.WrapGap = gap;

            layout.HorizontalAlign = global::Flexalon.Align.Center;
            layout.VerticalAlign = global::Flexalon.Align.Center;
            layout.DepthAlign = global::Flexalon.Align.Center;
            layout.HorizontalInnerAlign = global::Flexalon.Align.Center;
            layout.VerticalInnerAlign = global::Flexalon.Align.Center;
            layout.DepthInnerAlign = global::Flexalon.Align.Center;

            var mainAxis = global::Flexalon.Math.GetAxisFromDirection(direction);
            var mainAlign = ResolveFlexalonAlign(justifyContent);
            var crossAlign = ResolveFlexalonAlign(alignItems);

            if (mainAxis == global::Flexalon.Axis.X)
            {
                layout.HorizontalAlign = mainAlign;
                layout.VerticalInnerAlign = crossAlign;
            }
            else if (mainAxis == global::Flexalon.Axis.Y)
            {
                layout.VerticalAlign = mainAlign;
                layout.HorizontalInnerAlign = crossAlign;
            }
            else
            {
                layout.DepthAlign = mainAlign;
                layout.DepthInnerAlign = crossAlign;
            }
        }

        private static void ConfigureFlexalonGridLayout(RectTransform rect, AIToUGUICompiledNode node)
        {
            ClearSemanticLayoutComponents(rect.gameObject);

            var layout = GetOrAddFlexalonComponent<global::Flexalon.FlexalonGridLayout>(rect.gameObject);

            var columns = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(GetAttribute(node, "data-ui-grid-columns"), 0f)));
            var rows = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(GetAttribute(node, "data-ui-grid-rows"), 0f)));
            var layers = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(GetAttribute(node, "data-ui-grid-layers"), 1f)));
            var cellWidth = ParseFloat(GetAttribute(node, "data-ui-grid-cell-width"), 0f);
            var cellHeight = ParseFloat(GetAttribute(node, "data-ui-grid-cell-height"), 0f);
            var gapX = Mathf.Max(0f, ParseFloat(GetAttribute(node, "data-ui-grid-gap-x"), ParseFloat(GetStyle(node, "gap"), 0f)));
            var gapY = Mathf.Max(0f, ParseFloat(GetAttribute(node, "data-ui-grid-gap-y"), ParseFloat(GetStyle(node, "gap"), 0f)));

            layout.Columns = (uint)columns;
            layout.Rows = (uint)rows;
            layout.Layers = (uint)layers;
            layout.CellType = ResolveGridCellType(GetAttribute(node, "data-ui-grid-cell-type"));
            layout.ColumnDirection = ResolveNamedFlexalonDirection(GetAttribute(node, "data-ui-grid-column-direction"), global::Flexalon.Direction.PositiveX);
            layout.RowDirection = ResolveNamedFlexalonDirection(GetAttribute(node, "data-ui-grid-row-direction"), global::Flexalon.Direction.NegativeY);
            layout.HorizontalAlign = ResolveFlexalonAlign(GetAttribute(node, "data-ui-grid-align-x"));
            layout.VerticalAlign = ResolveFlexalonAlign(GetAttribute(node, "data-ui-grid-align-y"));
            layout.DepthAlign = global::Flexalon.Align.Center;
            layout.ColumnSpacing = gapX;
            layout.RowSpacing = gapY;
            layout.LayerSpacing = 0f;
            layout.ColumnSizeType = cellWidth > 0.001f
                ? global::Flexalon.FlexalonGridLayout.CellSizeTypes.Fixed
                : global::Flexalon.FlexalonGridLayout.CellSizeTypes.Fill;
            layout.RowSizeType = cellHeight > 0.001f
                ? global::Flexalon.FlexalonGridLayout.CellSizeTypes.Fixed
                : global::Flexalon.FlexalonGridLayout.CellSizeTypes.Fill;
            if (cellWidth > 0.001f)
            {
                layout.ColumnSize = cellWidth;
            }

            if (cellHeight > 0.001f)
            {
                layout.RowSize = cellHeight;
            }
        }

        private static void ConfigureFlexalonCurveLayout(RectTransform rect, AIToUGUICompiledNode node, Vector2 parentSize)
        {
            ClearSemanticLayoutComponents(rect.gameObject);
            var curveComponent = GetOrAddFlexalonComponent<global::Flexalon.FlexalonCurveLayout>(rect.gameObject);

            var rawPoints = GetAttribute(node, "data-ui-curve-points");
            var points = ParseCompiledCurvePoints(rawPoints);
            if (points.Length == 0)
            {
                return;
            }

            var curvePoints = new List<global::Flexalon.FlexalonCurveLayout.CurvePoint>(points.Length);

            var layoutSize = ResolveNodeSize(node, parentSize, new Vector2(-1f, -1f));
            if (layoutSize.x <= 0.001f || layoutSize.y <= 0.001f)
            {
                layoutSize = new Vector2(
                    node.AbsoluteRect.Width > 0.001f ? node.AbsoluteRect.Width : rect.rect.width,
                    node.AbsoluteRect.Height > 0.001f ? node.AbsoluteRect.Height : rect.rect.height);
            }

            for (var i = 0; i < points.Length; i++)
            {
                curvePoints.Add(new global::Flexalon.FlexalonCurveLayout.CurvePoint
                {
                    Position = new Vector3(points[i].x - layoutSize.x * 0.5f, layoutSize.y * 0.5f - points[i].y, 0f),
                    Tangent = new Vector3(points[i].tangentX, -points[i].tangentY, 0f),
                    TangentMode = ParseCurveTangentMode(points[i].tangentMode)
                });
            }

            RecordFlexalonComponent(curveComponent);
            SetReflectedValue(curveComponent, "_lockTangents", IsTruthyAttribute(GetAttribute(node, "data-ui-curve-lock-tangents")));
            SetReflectedValue(curveComponent, "_lockPositions", IsTruthyAttribute(GetAttribute(node, "data-ui-curve-lock-positions")));
            SetReflectedValue(curveComponent, "_spacingType", ParseCurveSpacingMode(GetAttribute(node, "data-ui-curve-spacing-mode")));
            SetReflectedValue(curveComponent, "_spacing", Mathf.Max(0f, ParseFloat(GetAttribute(node, "data-ui-curve-spacing"), 0f)));
            SetReflectedValue(curveComponent, "_startAt", ParseFloat(GetAttribute(node, "data-ui-curve-start-at"), 0f));
            SetReflectedValue(curveComponent, "_beforeStart", ParseCurveExtendBehavior(GetAttribute(node, "data-ui-curve-extend-before")));
            SetReflectedValue(curveComponent, "_afterEnd", ParseCurveExtendBehavior(GetAttribute(node, "data-ui-curve-extend-after")));
            SetReflectedValue(curveComponent, "_rotation", ResolveCurveRotationForUGUI(node));
            SetReflectedValue(curveComponent, "_points", curvePoints);
            SetReflectedValue(curveComponent, "_version", GetFlexalonComponentCurrentVersion());
            EditorUtility.SetDirty(curveComponent);
            curveComponent.MarkDirty();
        }

        private static global::Flexalon.FlexalonCurveLayout.SpacingOptions ParseCurveSpacingMode(string value)
        {
            switch (NormalizeEnumToken(value))
            {
                case "fixed":
                    return global::Flexalon.FlexalonCurveLayout.SpacingOptions.Fixed;
                case "evenlyconnected":
                    return global::Flexalon.FlexalonCurveLayout.SpacingOptions.EvenlyConnected;
                default:
                    return global::Flexalon.FlexalonCurveLayout.SpacingOptions.Evenly;
            }
        }

        private static global::Flexalon.FlexalonCurveLayout.ExtendBehavior ParseCurveExtendBehavior(string value)
        {
            switch (NormalizeEnumToken(value))
            {
                case "pingpong":
                    return global::Flexalon.FlexalonCurveLayout.ExtendBehavior.PingPong;
                case "extendline":
                    return global::Flexalon.FlexalonCurveLayout.ExtendBehavior.ExtendLine;
                case "repeat":
                    return global::Flexalon.FlexalonCurveLayout.ExtendBehavior.Repeat;
                case "repeatmirror":
                    return global::Flexalon.FlexalonCurveLayout.ExtendBehavior.RepeatMirror;
                default:
                    return global::Flexalon.FlexalonCurveLayout.ExtendBehavior.Stop;
            }
        }

        // Flexalon's In/Out/WithRoll/Forward/Backward rotation modes orient each
        // item along the curve in 3D (LookRotation toward/away from the curve
        // tangent). On a planar XY UI curve under a Canvas that resolves to a
        // 180-degree flip around Y or Z, i.e. mirrored / upside-down cards.
        // Those modes are meant for 3D objects on a track, not 2D cards that
        // must face the camera, and the browser preview ignores them entirely so
        // the mirror only shows up in Unity. We clamp to None for 2D UGUI baking
        // and log loudly so the authored intent is not silently swallowed.
        private static global::Flexalon.FlexalonCurveLayout.RotationOptions ResolveCurveRotationForUGUI(AIToUGUICompiledNode node)
        {
            var requested = ParseCurveRotationMode(GetAttribute(node, "data-ui-curve-rotation"));
            if (requested == global::Flexalon.FlexalonCurveLayout.RotationOptions.None)
            {
                return requested;
            }

            Debug.LogWarning(
                $"[AIToUGUI] Curve node '{node.Name}' requested 3D rotation mode '{requested}', which mirrors/flips "
                + "cards on a 2D UI curve. Clamped to None so cards stay upright and face the camera. "
                + "Use data-ui-curve-rotation=\"None\" for 2D UGUI curves to silence this.");
            return global::Flexalon.FlexalonCurveLayout.RotationOptions.None;
        }

        private static global::Flexalon.FlexalonCurveLayout.RotationOptions ParseCurveRotationMode(string value)
        {
            switch (NormalizeEnumToken(value))
            {
                case "in":
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.In;
                case "out":
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.Out;
                case "inwithroll":
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.InWithRoll;
                case "outwithroll":
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.OutWithRoll;
                case "forward":
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.Forward;
                case "backward":
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.Backward;
                default:
                    return global::Flexalon.FlexalonCurveLayout.RotationOptions.None;
            }
        }

        private static global::Flexalon.FlexalonCurveLayout.TangentMode ParseCurveTangentMode(string value)
        {
            switch (NormalizeEnumToken(value))
            {
                case "matchprevious":
                    return global::Flexalon.FlexalonCurveLayout.TangentMode.MatchPrevious;
                case "corner":
                    return global::Flexalon.FlexalonCurveLayout.TangentMode.Corner;
                case "smooth":
                    return global::Flexalon.FlexalonCurveLayout.TangentMode.Smooth;
                default:
                    return global::Flexalon.FlexalonCurveLayout.TangentMode.Manual;
            }
        }

        private static void ScheduleSemanticLayoutRefresh(GameObject root)
        {
            if (root == null || !HasSemanticLayout(root))
            {
                return;
            }

            var rootInstanceId = root.GetInstanceID();
            EditorApplication.delayCall += () =>
            {
                var previewRoot = EditorUtility.InstanceIDToObject(rootInstanceId) as GameObject;
                if (previewRoot == null || !previewRoot.activeInHierarchy || !HasSemanticLayout(previewRoot))
                {
                    return;
                }

                if (AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
                {
                    ScheduleSemanticLayoutRefresh(previewRoot);
                    return;
                }

                if (!RefreshSemanticLayoutNow(previewRoot))
                {
                    return;
                }
            };
        }

        private static bool HasSemanticLayout(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            var semanticLayouts = root.GetComponentsInChildren<global::Flexalon.FlexalonComponent>(true);
            for (var i = 0; i < semanticLayouts.Length; i++)
            {
                if (IsSemanticLayoutComponent(semanticLayouts[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RefreshSemanticLayoutNow(GameObject root)
        {
            if (root == null || !HasSemanticLayout(root) || AIToUGUIEditorUiMutationGuard.IsUnsafeToMutateUi())
            {
                return false;
            }

            var flexalon = global::Flexalon.Flexalon.GetOrCreate();
            if (flexalon == null)
            {
                return false;
            }

            Canvas.ForceUpdateCanvases();
            var semanticLayouts = root.GetComponentsInChildren<global::Flexalon.FlexalonComponent>(true);
            for (var i = 0; i < semanticLayouts.Length; i++)
            {
                var component = semanticLayouts[i];
                if (!IsSemanticLayoutComponent(component))
                {
                    continue;
                }

                component.MarkDirty();
                EditorUtility.SetDirty(component);
            }

            flexalon.ForceUpdate();
            Canvas.ForceUpdateCanvases();
            return true;
        }

        private static bool IsSemanticLayoutComponent(global::Flexalon.FlexalonComponent component)
        {
            if (component == null)
            {
                return false;
            }

            if (component is global::Flexalon.FlexalonFlexibleLayout ||
                component is global::Flexalon.FlexalonGridLayout ||
                component is global::Flexalon.FlexalonCurveLayout)
            {
                return true;
            }

            return false;
        }

        private static void CleanupOrphanedFlexalonSingleton()
        {
            if (HasAnySemanticLayoutInLoadedScenes())
            {
                return;
            }

            var flexalon = global::Flexalon.Flexalon.Get();
            if (flexalon == null || flexalon.gameObject == null)
            {
                return;
            }

            if (!string.Equals(flexalon.gameObject.name, "Flexalon", StringComparison.Ordinal))
            {
                return;
            }

            AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(flexalon.gameObject);
        }

        private static bool HasAnySemanticLayoutInLoadedScenes()
        {
#if UNITY_2023_1_OR_NEWER
            var components = UnityEngine.Object.FindObjectsByType<global::Flexalon.FlexalonComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var components = UnityEngine.Object.FindObjectsOfType<global::Flexalon.FlexalonComponent>(true);
#endif
            for (var i = 0; i < components.Length; i++)
            {
                if (IsSemanticLayoutComponent(components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static global::Flexalon.FlexalonFlexibleLayout.GapOptions ResolveFlexalonGapOptions(string justifyContent)
        {
            if (string.IsNullOrWhiteSpace(justifyContent))
            {
                return global::Flexalon.FlexalonFlexibleLayout.GapOptions.Fixed;
            }

            switch (justifyContent.Trim().ToLowerInvariant())
            {
                case "space-between":
                    return global::Flexalon.FlexalonFlexibleLayout.GapOptions.SpaceBetween;
                case "space-around":
                    return global::Flexalon.FlexalonFlexibleLayout.GapOptions.SpaceAround;
                case "space-evenly":
                    return global::Flexalon.FlexalonFlexibleLayout.GapOptions.SpaceEvenly;
                default:
                    return global::Flexalon.FlexalonFlexibleLayout.GapOptions.Fixed;
            }
        }

        private static global::Flexalon.Direction ResolveFlexalonDirection(string flexDirection)
        {
            if (string.IsNullOrWhiteSpace(flexDirection))
            {
                return global::Flexalon.Direction.PositiveX;
            }

            switch (flexDirection.Trim().ToLowerInvariant())
            {
                case "row-reverse":
                    return global::Flexalon.Direction.NegativeX;
                case "column":
                    return global::Flexalon.Direction.NegativeY;
                case "column-reverse":
                    return global::Flexalon.Direction.PositiveY;
                default:
                    return global::Flexalon.Direction.PositiveX;
            }
        }

        private static global::Flexalon.Direction ResolveNamedFlexalonDirection(string value, global::Flexalon.Direction fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            switch (NormalizeEnumToken(value))
            {
                case "positivex":
                    return global::Flexalon.Direction.PositiveX;
                case "negativex":
                    return global::Flexalon.Direction.NegativeX;
                case "positivey":
                    return global::Flexalon.Direction.PositiveY;
                case "negativey":
                    return global::Flexalon.Direction.NegativeY;
                case "positivez":
                    return global::Flexalon.Direction.PositiveZ;
                case "negativez":
                    return global::Flexalon.Direction.NegativeZ;
                default:
                    return fallback;
            }
        }

        private static bool IsFlexWrapEnabled(string flexWrap)
        {
            if (string.IsNullOrWhiteSpace(flexWrap))
            {
                return false;
            }

            return !string.Equals(flexWrap.Trim(), "nowrap", StringComparison.OrdinalIgnoreCase);
        }

        private static global::Flexalon.Direction ResolveFlexalonWrapDirection(global::Flexalon.Direction direction, string flexWrap)
        {
            if (!IsFlexWrapEnabled(flexWrap))
            {
                return global::Flexalon.Direction.NegativeY;
            }

            if (string.Equals(flexWrap.Trim(), "wrap-reverse", StringComparison.OrdinalIgnoreCase))
            {
                return global::Flexalon.Math.GetOppositeDirection(ResolveDefaultWrapDirection(direction));
            }

            return ResolveDefaultWrapDirection(direction);
        }

        private static global::Flexalon.Direction ResolveDefaultWrapDirection(global::Flexalon.Direction direction)
        {
            var axis = global::Flexalon.Math.GetAxisFromDirection(direction);
            if (axis == global::Flexalon.Axis.X)
            {
                return global::Flexalon.Direction.NegativeY;
            }

            if (axis == global::Flexalon.Axis.Y)
            {
                return global::Flexalon.Direction.PositiveX;
            }

            return global::Flexalon.Direction.NegativeY;
        }

        private static global::Flexalon.Align ResolveFlexalonAlign(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return global::Flexalon.Align.Start;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "center":
                    return global::Flexalon.Align.Center;
                case "flex-end":
                case "end":
                case "right":
                case "bottom":
                    return global::Flexalon.Align.End;
                case "space-between":
                case "space-around":
                case "space-evenly":
                case "stretch":
                case "flex-start":
                case "start":
                default:
                    return global::Flexalon.Align.Start;
            }
        }

        private static global::Flexalon.FlexalonGridLayout.CellTypes ResolveGridCellType(string value)
        {
            return NormalizeEnumToken(value) == "hexagonal"
                ? global::Flexalon.FlexalonGridLayout.CellTypes.Hexagonal
                : global::Flexalon.FlexalonGridLayout.CellTypes.Rectangle;
        }

        private static void ClearSemanticLayoutComponents(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            RemoveComponentIfPresent<HorizontalLayoutGroup>(target);
            RemoveComponentIfPresent<VerticalLayoutGroup>(target);
            RemoveComponentIfPresent<ContentSizeFitter>(target);
            RemoveComponentIfPresent<global::Flexalon.FlexalonFlexibleLayout>(target);
            RemoveComponentIfPresent<global::Flexalon.FlexalonGridLayout>(target);
            RemoveComponentIfPresent<global::Flexalon.FlexalonCurveLayout>(target);
        }

        private static CompiledCurvePointDto[] ParseCompiledCurvePoints(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<CompiledCurvePointDto>();
            }

            try
            {
                var points = JsonConvert.DeserializeObject<CompiledCurvePointDto[]>(raw);
                return points ?? Array.Empty<CompiledCurvePointDto>();
            }
            catch
            {
                return Array.Empty<CompiledCurvePointDto>();
            }
        }

        private static T GetOrAddFlexalonComponent<T>(GameObject target) where T : Component
        {
            if (target == null)
            {
                return null;
            }

            var existing = target.GetComponent<T>();
            if (existing != null)
            {
                return existing;
            }

            return global::Flexalon.Flexalon.AddComponent<T>(target);
        }

        private static void RecordFlexalonComponent(global::Flexalon.FlexalonComponent component)
        {
            if (component == null)
            {
                return;
            }

            Undo.RecordObject(component, "Configure Flexalon Layout");
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);

            global::Flexalon.Flexalon.RecordFrameChanges = true;
        }

        private static int GetFlexalonComponentCurrentVersion()
        {
            var versionField = typeof(global::Flexalon.FlexalonComponent)
                .GetField("_currentVersion", BindingFlags.Static | BindingFlags.NonPublic);
            if (versionField != null && versionField.GetValue(null) is int version)
            {
                return version;
            }

            return 4;
        }

        private static void SetReflectedValue(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return;
            }

            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, value);
                    return;
                }

                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }
        }

        private static void SetReflectedEnumValue(object target, string memberName, Type enumType, string rawValue, string fallbackName)
        {
            if (target == null || enumType == null)
            {
                return;
            }

            SetReflectedValue(target, memberName, ParseReflectedEnumValue(enumType, rawValue, fallbackName));
        }

        private static object ParseReflectedEnumValue(Type enumType, string rawValue, string fallbackName)
        {
            if (enumType == null)
            {
                return null;
            }

            var normalizedValue = NormalizeEnumToken(rawValue);
            var names = Enum.GetNames(enumType);
            for (var i = 0; i < names.Length; i++)
            {
                if (NormalizeEnumToken(names[i]) == normalizedValue)
                {
                    return Enum.Parse(enumType, names[i]);
                }
            }

            return Enum.Parse(enumType, fallbackName);
        }

        private static void SetReflectedMemberValue(Type type, ref object boxedStruct, string memberName, object value)
        {
            if (type == null || boxedStruct == null || string.IsNullOrWhiteSpace(memberName))
            {
                return;
            }

            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                property.SetValue(boxedStruct, value);
                return;
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(boxedStruct, value);
        }

        private static bool IsTruthyAttribute(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
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

        private static string NormalizeEnumToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value, "[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
        }

        private static void ApplyLayoutElement(GameObject go, AIToUGUICompiledNode node, Vector2 parentSize)
        {
            var layoutElement = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            var size = ResolveNodeSize(node, parentSize, new Vector2(-1f, -1f));
            var minWidth = ParseLength(GetStyle(node, "min-width"));
            var minHeight = ParseLength(GetStyle(node, "min-height"));

            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            layoutElement.minWidth = ResolveLength(minWidth, parentSize.x, -1f);
            layoutElement.minHeight = ResolveLength(minHeight, parentSize.y, -1f);
            layoutElement.flexibleWidth = ResolveFlexible(GetStyle(node, "flex"), GetStyle(node, "flex-grow"), ParseLength(GetStyle(node, "width")));
            layoutElement.flexibleHeight = ResolveFlexible(GetStyle(node, "flex"), GetStyle(node, "flex-grow"), ParseLength(GetStyle(node, "height")));
            layoutElement.ignoreLayout = GetStyle(node, "position") == "absolute";
        }

        private static void ConfigureGraphic(GameObject go, AIToUGUICompiledNode node, AIToUGUICompiledPage page, bool isRoot)
        {
            if (!HasImageLikeAssetRef(node))
            {
                RemoveInternalAssetVisual(go);
            }

            ConfigureGraphicTarget(go, node, page, isRoot);
            TryApplyImageAsset(go, node, page);
        }

        private static void ConfigureGraphicTarget(GameObject target, AIToUGUICompiledNode node, AIToUGUICompiledPage page, bool isRoot)
        {
            if (target == null)
            {
                return;
            }

            if (IsTransparentOverlayButton(node))
            {
                CleanupProceduralShapeArtifacts(target);
                CleanupDashedBorder(target);
                var image = EnsurePlainImageGraphic(target);
                if (image != null)
                {
                    image.sprite = null;
                    image.overrideSprite = null;
                    image.type = Image.Type.Simple;
                    image.color = new Color(1f, 1f, 1f, 0.001f);
                    image.raycastTarget = true;
                }

                return;
            }

            if (HasImageLikeAssetRef(node))
            {
                CleanupDashedBorder(target);
                RemoveComponentIfPresent<AIToUGUIShapeAdapter>(target);
                RemoveComponentIfPresent<AIToUGUIWindinatorShapeAdapter>(target);
                RemoveComponentIfPresent<AIToUGUIDashedBorderAdapter>(target);
                return;
            }

            if (!NeedsShape(node, isRoot, page.Theme))
            {
                return;
            }

            if (ShouldUseRouteLineAdapter(node))
            {
                var lineColor = ExtractFillColor(node, null, page.Theme, isRoot);
                var lineAdapter = target.GetComponent<AIToUGUIRouteLineAdapter>() ?? target.AddComponent<AIToUGUIRouteLineAdapter>();
                lineAdapter.Configure(lineColor);
                return;
            }

            RemoveComponentIfPresent<AIToUGUIRouteLineAdapter>(target);
            RemoveComponentIfPresent<LineGraphic>(target);

            var preset = ResolveVisualPreset(node, page.Theme);
            var fillColor = ExtractFillColor(node, preset, page.Theme, isRoot);
            var gradientColor = ExtractGradientColor(node, preset);
            var direction = ExtractGradientDirection(node, preset);
            var radius = ExtractCornerRadius(node, preset, page.Theme, node.ControlType);
            var outlineWidth = ExtractOutlineWidth(node, preset, page.Theme);
            var outlineColor = ExtractOutlineColor(node, preset, page.Theme);
            var shadowSize = ExtractShadowSize(node, preset, page.Theme);
            var shadowBlur = ExtractShadowBlur(node, preset, page.Theme);
            var shadowColor = ExtractShadowColor(node, preset, page.Theme);
            var glowEnabled = ExtractGlowEnabled(node, preset);
            var glowColor = ExtractGlowColor(node, preset, page.Theme);
            var glowBlur = ExtractGlowBlur(node, preset, page.Theme);
            var glowIntensity = ExtractGlowIntensity(node, preset, page.Theme);
            var useGradient = HasGradient(node, preset);
            var backend = ResolveShapeRenderBackend(node, out var windinatorKind, out var cornerRadii, out var shapeAmount);
            var isDashedBorder = string.Equals(ResolveBorderStyle(node), "dashed", StringComparison.OrdinalIgnoreCase);
            var dashedOutlineWidth = outlineWidth;

            if (isDashedBorder)
            {
                if (!HasExplicitBackgroundAuthoring(node) &&
                    string.IsNullOrWhiteSpace(node.Role) &&
                    node.ControlType != AIToUGUIControlType.Button &&
                    node.ControlType != AIToUGUIControlType.Input &&
                    node.ControlType != AIToUGUIControlType.Toggle &&
                    node.ControlType != AIToUGUIControlType.Slider &&
                    node.ControlType != AIToUGUIControlType.Dropdown)
                {
                    fillColor = Color.clear;
                    useGradient = false;
                    gradientColor = Color.clear;
                }

                outlineWidth = 0f;
            }

            if (backend == AIToUGUIShapeRenderBackend.WindinatorLite)
            {
                var adapter = target.GetComponent<AIToUGUIWindinatorShapeAdapter>() ?? target.AddComponent<AIToUGUIWindinatorShapeAdapter>();
                adapter.Configure(
                    windinatorKind,
                    fillColor.a > 0.001f,
                    fillColor,
                    useGradient,
                    gradientColor,
                    direction,
                    radius,
                    cornerRadii,
                    shapeAmount,
                    outlineWidth,
                    outlineColor,
                    shadowSize,
                    shadowBlur,
                    shadowColor,
                    glowEnabled,
                    glowColor,
                    glowBlur,
                    glowIntensity);
            }
            else
            {
                var adapter = target.GetComponent<AIToUGUIShapeAdapter>() ?? target.AddComponent<AIToUGUIShapeAdapter>();
                adapter.Configure(
                    fillColor.a > 0.001f,
                    fillColor,
                    useGradient,
                    gradientColor,
                    direction,
                    radius,
                    ResolveUseMaxRoundness(node, preset),
                    outlineWidth,
                    outlineColor,
                    shadowSize,
                    shadowBlur,
                    shadowColor,
                    glowEnabled,
                    glowColor,
                    glowBlur,
                    glowIntensity);
            }

            if (isDashedBorder)
            {
                ConfigureDashedBorder(target, node, radius, outlineColor, dashedOutlineWidth);
            }
            else
            {
                CleanupDashedBorder(target);
            }

            if (target.TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = node.ControlType == AIToUGUIControlType.Button ||
                                        node.ControlType == AIToUGUIControlType.Input ||
                                        node.ControlType == AIToUGUIControlType.Scrollbar ||
                                        node.ControlType == AIToUGUIControlType.Toggle ||
                                        node.ControlType == AIToUGUIControlType.Slider ||
                                        node.ControlType == AIToUGUIControlType.Dropdown;
            }
        }

        private static Transform ConfigureContentMaskHost(GameObject go, AIToUGUICompiledNode node, Transform defaultChildHost, AIToUGUICompiledPage page)
        {
            if (go == null)
            {
                return defaultChildHost;
            }

            RemoveComponentIfPresent<RectMask2D>(go);

            if (page != null && !page.UseOverflowMaskHosts)
            {
                RemoveInternalContentMaskHost(go);
                return defaultChildHost;
            }

            if (!ShouldUseContentMaskHost(node, defaultChildHost, go.transform))
            {
                RemoveInternalContentMaskHost(go);
                return defaultChildHost;
            }

            var host = EnsureInternalContentMaskHost(go);
            return host != null ? host.transform : defaultChildHost;
        }

        private static bool ShouldUseContentMaskHost(AIToUGUICompiledNode node, Transform defaultChildHost, Transform nodeTransform)
        {
            if (node == null ||
                node.ControlType != AIToUGUIControlType.Div ||
                defaultChildHost == null ||
                defaultChildHost != nodeTransform)
            {
                return false;
            }

            var overflow = GetStyle(node, "overflow");
            var overflowX = GetStyle(node, "overflow-x");
            var overflowY = GetStyle(node, "overflow-y");
            return IsClippingOverflow(overflow) ||
                   IsClippingOverflow(overflowX) ||
                   IsClippingOverflow(overflowY);
        }

        private static bool IsClippingOverflow(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized == "hidden" ||
                   normalized == "auto" ||
                   normalized == "scroll";
        }

        private static GameObject EnsureInternalContentMaskHost(GameObject go)
        {
            var existing = go.transform.Find(InternalContentMaskName);
            GameObject host;
            if (existing != null)
            {
                host = existing.gameObject;
            }
            else
            {
                host = CreateStretchChild(go, InternalContentMaskName);
            }

            var rect = host.GetComponent<RectTransform>();
            StretchToParent(rect);

            if (host.GetComponent<RectMask2D>() == null)
            {
                host.AddComponent<RectMask2D>();
            }

            MoveExistingInternalVisualContentIntoMaskHost(go.transform, host.transform);
            host.transform.SetAsLastSibling();
            return host;
        }

        private static void MoveExistingInternalVisualContentIntoMaskHost(Transform root, Transform host)
        {
            if (root == null || host == null)
            {
                return;
            }

            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null ||
                    child == host ||
                    !child.name.StartsWith(InternalNodePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                child.SetParent(host, true);
            }
        }

        private static void RemoveInternalContentMaskHost(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var existing = go.transform.Find(InternalContentMaskName);
            if (existing != null)
            {
                RemoveInternalContentMaskHost(existing);
            }
        }

        private static Transform ConfigureControl(GameObject go, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            Transform childHost = go.transform;
            switch (node.ControlType)
            {
                case AIToUGUIControlType.Text:
                    CreateText(go, node, page.Theme, false);
                    break;
                case AIToUGUIControlType.Button:
                    CreateButton(go, node, page);
                    break;
                case AIToUGUIControlType.Input:
                    childHost = CreateInput(go, node, page.Theme);
                    break;
                case AIToUGUIControlType.Scroll:
                    childHost = CreateScroll(go, node);
                    break;
                case AIToUGUIControlType.Scrollbar:
                    CreateScrollbar(go, node);
                    break;
                case AIToUGUIControlType.Toggle:
                    CreateToggle(go, node, page.Theme);
                    break;
                case AIToUGUIControlType.Slider:
                    CreateSlider(go, node);
                    break;
                case AIToUGUIControlType.Dropdown:
                    CreateDropdown(go, node, page.Theme);
                    break;
                case AIToUGUIControlType.Image:
                    CreateImage(go);
                    break;
                case AIToUGUIControlType.Progress:
                    CreateProgress(go, node, page.Theme);
                    break;
                case AIToUGUIControlType.Div:
                    if (!string.IsNullOrWhiteSpace(node.Text))
                    {
                        CreateText(go, node, page.Theme, true);
                    }
                    break;
            }

            TryCreateIconPlaceholder(go, node, page.Theme);
            return childHost;
        }

        private static bool TryConfigureCompositeElement(
            GameObject go,
            AIToUGUICompiledNode node,
            AIToUGUICompiledPage page,
            out Transform childHost,
            out AIToUGUIElementSlots slots)
        {
            childHost = go != null ? go.transform : null;
            slots = null;
            if (go == null ||
                node == null ||
                page == null ||
                !ShouldUseCompositeTemplate(node))
            {
                return false;
            }

            var template = ResolveCompositeTemplate(node, page);
            if (template == null ||
                template.backingMode != AIToUGUIElementBackingMode.PrefabBacked ||
                template.prefab == null)
            {
                return false;
            }

            ApplyTemplateDefaults(node, template);
            var compositeRoot = EnsurePrefabBackedRoot(go, template, InternalCompositeRootName);
            if (compositeRoot == null)
            {
                AddMessage(page.Errors, $"Failed to instantiate composite prefab '{template.prefab.name}' for node '{node.Name}'.");
                return false;
            }

            slots = compositeRoot.GetComponent<AIToUGUIElementSlots>();
            ConfigureCompositeTemplate(compositeRoot, slots, node, page);
            TryConfigurePrimitiveIconSlot(compositeRoot, slots, node, page.Theme);
            childHost = ResolveCompositeChildHost(compositeRoot.transform, slots);
            if (childHost == null)
            {
                childHost = go.transform;
            }

            return true;
        }

        private static bool TryConfigurePrimitiveElement(
            GameObject go,
            AIToUGUICompiledNode node,
            AIToUGUICompiledPage page,
            out Transform childHost,
            out AIToUGUIElementSlots slots)
        {
            childHost = go != null ? go.transform : null;
            slots = null;
            if (go == null ||
                node == null ||
                page == null ||
                !AIToUGUIElementContractUtility.IsPrimitiveElement(node.ElementId) ||
                RequestsWindinatorPrimitiveOverride(node))
            {
                return false;
            }

            var template = page.ElementLibrary != null
                ? page.ElementLibrary.ResolveTemplate(node.ElementId, node.VariantId)
                : null;
            if (template == null || template.backingMode != AIToUGUIElementBackingMode.PrefabBacked || template.prefab == null)
            {
                return false;
            }

            ApplyTemplateDefaults(node, template);
            PromotePrimitiveTextChild(node);
            var primitiveRoot = EnsurePrimitiveRoot(go, template);
            if (primitiveRoot == null)
            {
                AddMessage(page.Errors, $"Failed to instantiate primitive prefab '{template.prefab.name}' for node '{node.Name}'.");
                return false;
            }

            slots = primitiveRoot.GetComponent<AIToUGUIElementSlots>();
            switch (node.ElementId)
            {
                case "button":
                    ConfigurePrimitiveButton(primitiveRoot, slots, node, page);
                    break;
                case "input":
                    ConfigurePrimitiveInput(primitiveRoot, slots, node, page);
                    break;
                case "toggle":
                    ConfigurePrimitiveToggle(primitiveRoot, slots, node, page);
                    break;
                case "slider":
                    ConfigurePrimitiveSlider(primitiveRoot, slots, node, page);
                    break;
                case "dropdown":
                    ConfigurePrimitiveDropdown(primitiveRoot, slots, node, page);
                    break;
                case "scrollbar":
                    ConfigurePrimitiveScrollbar(primitiveRoot, slots, node, page);
                    break;
                case "scrollview":
                    childHost = ConfigurePrimitiveScrollView(primitiveRoot, slots, node, page);
                    break;
                case "image":
                    ConfigurePrimitiveImage(primitiveRoot, slots, node, page);
                    break;
                case "progress":
                    ConfigurePrimitiveProgress(primitiveRoot, slots, node, page);
                    break;
            }

            TryConfigurePrimitiveIconSlot(primitiveRoot, slots, node, page.Theme);
            if (childHost == null)
            {
                childHost = go.transform;
            }

            return true;
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

        private static bool IsTransparentOverlayButton(AIToUGUICompiledNode node)
        {
            if (node == null || node.ControlType != AIToUGUIControlType.Button)
            {
                return false;
            }

            if (HasClass(node, "button-overlay"))
            {
                return true;
            }

            if (HasTransparentBackgroundAuthoring(node) && !HasVisibleButtonChromeAuthoring(node))
            {
                return true;
            }

            return false;
        }

        private static bool HasTransparentBackgroundAuthoring(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (TryResolveBackgroundPrimary(node, out var color))
            {
                return color.a <= 0.001f;
            }

            var background = GetStyle(node, "background");
            if (!string.IsNullOrWhiteSpace(background))
            {
                var normalized = background.Trim().ToLowerInvariant();
                if (normalized == "transparent" ||
                    normalized == "none" ||
                    normalized.Contains("rgba(255,255,255,0)") ||
                    normalized.Contains("rgba(0,0,0,0)"))
                {
                    return true;
                }
            }

            var backgroundColor = GetStyle(node, "background-color");
            if (!string.IsNullOrWhiteSpace(backgroundColor) &&
                TryParseColor(backgroundColor, out color))
            {
                return color.a <= 0.001f;
            }

            return false;
        }

        private static bool HasVisibleButtonChromeAuthoring(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (TryResolveBackgroundSecondary(node, out var secondaryColor) && secondaryColor.a > 0.001f)
            {
                return true;
            }

            if (TryResolveBorderColor(node, out var borderColor) && borderColor.a > 0.001f)
            {
                return true;
            }

            if (ExtractOutlineWidth(node, null, null) > 0.001f)
            {
                return true;
            }

            if (TryResolvePrimaryShadowLayer(node, out var shadowLayer, out _))
            {
                if (shadowLayer.Color.a > 0.001f &&
                    (Mathf.Abs(shadowLayer.Offset.x) > 0.001f ||
                     Mathf.Abs(shadowLayer.Offset.y) > 0.001f ||
                     shadowLayer.Blur > 0.001f))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject EnsurePrimitiveRoot(GameObject host, AIToUGUIElementTemplate template)
        {
            return EnsurePrefabBackedRoot(host, template, InternalPrimitiveRootName);
        }

        private static GameObject EnsurePrefabBackedRoot(GameObject host, AIToUGUIElementTemplate template, string internalRootName)
        {
            if (host == null || template?.prefab == null)
            {
                return null;
            }

            var existing = host.transform.Find(internalRootName);
            if (existing != null)
            {
                StretchToParent(existing as RectTransform ?? existing.GetComponent<RectTransform>());
                return existing.gameObject;
            }

            var instance = PrefabUtility.InstantiatePrefab(template.prefab) as GameObject;
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(template.prefab);
            }

            if (instance == null)
            {
                return null;
            }

            instance.name = internalRootName;
            instance.transform.SetParent(host.transform, false);
            var rect = instance.GetComponent<RectTransform>() ?? instance.AddComponent<RectTransform>();
            StretchToParent(rect);
            return instance;
        }

        private static AIToUGUIElementTemplate ResolveCompositeTemplate(
            AIToUGUICompiledNode node,
            AIToUGUICompiledPage page)
        {
            if (node == null ||
                !ShouldUseCompositeTemplate(node))
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

            var isCompositeTemplate = template != null &&
                                      (AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(template.componentFamily) ||
                                       AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(template.elementId));
            return isCompositeTemplate && template.backingMode == AIToUGUIElementBackingMode.PrefabBacked
                ? template
                : null;
        }

        private static bool ShouldUseCompositeTemplate(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.HasExplicitTemplateId && !string.IsNullOrWhiteSpace(node.TemplateId))
            {
                return true;
            }

            if (node.HasExplicitCompositeElement)
            {
                var compositeId = string.IsNullOrWhiteSpace(node.ComponentFamily)
                    ? node.ElementId
                    : node.ComponentFamily;
                return AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(compositeId);
            }

            return node.HasExplicitComponentFamily &&
                   AIToUGUIElementContractUtility.IsKnownCompositeComponentFamily(node.ComponentFamily);
        }

        private static void ApplyTemplateDefaults(AIToUGUICompiledNode node, AIToUGUIElementTemplate template)
        {
            if (node == null || template == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.Role))
            {
                node.Role = template.defaultRole ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(node.MotionId))
            {
                node.MotionId = template.motionPresetId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(node.MotionId))
                {
                    node.Styles["-ai-motion"] = node.MotionId;
                }
            }

            if (string.IsNullOrWhiteSpace(GetStyle(node, "-ai-preset")) &&
                !string.IsNullOrWhiteSpace(template.visualPresetId))
            {
                node.Styles["-ai-preset"] = template.visualPresetId;
            }

            if (string.IsNullOrWhiteSpace(node.ComponentFamily) &&
                !string.IsNullOrWhiteSpace(template.componentFamily))
            {
                node.ComponentFamily = AIToUGUIElementContractUtility.NormalizeComponentFamily(template.componentFamily);
            }

            if (!string.IsNullOrWhiteSpace(node.ComponentFamily) &&
                string.IsNullOrWhiteSpace(node.ComponentVariant) &&
                !string.IsNullOrWhiteSpace(template.componentVariant))
            {
                node.ComponentVariant = AIToUGUIElementContractUtility.NormalizeComponentVariantId(template.componentVariant);
            }

            if (string.IsNullOrWhiteSpace(node.RenderStrategy) ||
                string.Equals(node.RenderStrategy, AIToUGUIElementContractUtility.ProceduralRenderStrategyId, StringComparison.OrdinalIgnoreCase))
            {
                node.RenderStrategy = AIToUGUIElementContractUtility.NormalizeRenderStrategy(template.defaultRenderStrategy.ToString());
            }
        }

        private static void ConfigureCompositeTemplate(GameObject compositeRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (compositeRoot == null || node == null || page == null)
            {
                return;
            }

            var graphicTarget = GetSlotTransform(slots, "Graphic", compositeRoot.transform.Find("__ai_Graphic"));
            if (graphicTarget == null)
            {
                graphicTarget = GetSlotTransform(slots, "Background", compositeRoot.transform.Find("__ai_Background"));
            }

            ConfigureGraphicTarget((graphicTarget != null ? graphicTarget.gameObject : compositeRoot), node, page, false);
            TryApplyImageAsset(compositeRoot, node, page, graphicTarget != null ? graphicTarget.gameObject : compositeRoot);

            if (node.ControlType == AIToUGUIControlType.Button)
            {
                var button = compositeRoot.GetComponent<Button>() ?? compositeRoot.AddComponent<Button>();
                if (graphicTarget != null && graphicTarget.TryGetComponent<Graphic>(out var graphic))
                {
                    graphic.raycastTarget = true;
                    button.targetGraphic = graphic;
                }

                ConfigureSelectable(button, page.Theme);
            }

            TryConfigureCompositeTextSlots(slots, node, page.Theme);
            TryConfigureCompositeBadgeSlot(slots, node, page.Theme);
        }

        private static Transform ResolveCompositeChildHost(Transform fallback, AIToUGUIElementSlots slots)
        {
            var contentSlot = GetSlotTransform(slots, "Content", null);
            if (contentSlot != null)
            {
                return contentSlot;
            }

            var headerSlot = GetSlotTransform(slots, "Header", null);
            if (headerSlot != null)
            {
                return headerSlot;
            }

            return fallback;
        }

        private static void TryConfigureCompositeTextSlots(AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (slots == null || node == null || string.IsNullOrWhiteSpace(node.Text))
            {
                return;
            }

            if (!HasChildTargetSlot(node, "Label") && TryApplyTextToSlot(slots, "Label", node, theme, node.Text))
            {
                return;
            }

            if (!HasChildTargetSlot(node, "Title") && TryApplyTextToSlot(slots, "Title", node, theme, node.Text))
            {
                return;
            }

            if (!HasChildTargetSlot(node, "PrimaryText"))
            {
                TryApplyTextToSlot(slots, "PrimaryText", node, theme, node.Text);
            }
        }

        private static void TryConfigureCompositeBadgeSlot(AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (slots == null || node == null || HasChildTargetSlot(node, "Badge"))
            {
                return;
            }

            var badgeValue = GetStyle(node, "-ai-value");
            if (string.IsNullOrWhiteSpace(badgeValue))
            {
                badgeValue = GetAttribute(node, "data-ui-value");
            }

            if (string.IsNullOrWhiteSpace(badgeValue))
            {
                return;
            }

            TryApplyTextToSlot(slots, "Badge", node, theme, badgeValue);
        }

        private static bool TryApplyTextToSlot(
            AIToUGUIElementSlots slots,
            string slotId,
            AIToUGUICompiledNode node,
            AIToUGUIThemeDefinition theme,
            string textValue)
        {
            var slot = GetSlotTransform(slots, slotId, null);
            if (slot == null)
            {
                return false;
            }

            var text = slot.GetComponent<TextMeshProUGUI>() ?? slot.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyNodeText(text, node, theme, textValue);
            return true;
        }

        private static bool HasChildTargetSlot(AIToUGUICompiledNode node, string slotId)
        {
            if (node?.Children == null || string.IsNullOrWhiteSpace(slotId))
            {
                return false;
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (child != null &&
                    (string.Equals(child.SlotId, slotId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(child.ContainerId, slotId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ConfigurePrimitiveButton(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            var isOverlayButton = IsTransparentOverlayButton(node);
            if (!isOverlayButton)
            {
                ConfigureGraphicTarget(primitiveRoot, node, page, false);
                TryApplyImageAsset(primitiveRoot, node, page, primitiveRoot);
            }
            else
            {
                CleanupProceduralShapeArtifacts(primitiveRoot);
                var image = EnsurePlainImageGraphic(primitiveRoot);
                if (image != null)
                {
                    image.sprite = null;
                    image.overrideSprite = null;
                    image.type = Image.Type.Simple;
                    image.color = new Color(1f, 1f, 1f, 0.001f);
                    image.raycastTarget = true;
                }
            }

            var button = primitiveRoot.GetComponent<Button>() ?? primitiveRoot.AddComponent<Button>();
            if (primitiveRoot.TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = true;
                button.targetGraphic = graphic;
            }

            ConfigureSelectable(button, page.Theme);
            var label = GetSlotText(slots, "Label", primitiveRoot.transform.Find("__ai_Label"));
            if (isOverlayButton)
            {
                if (label != null)
                {
                    label.text = string.Empty;
                    label.color = new Color(1f, 1f, 1f, 0f);
                    label.raycastTarget = false;
                }
            }
            else
            {
                ApplyNodeText(label, node, page.Theme, node.Text);
            }
        }

        private static void ConfigurePrimitiveInput(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            ConfigureGraphicTarget(primitiveRoot, node, page, false);
            TryApplyImageAsset(primitiveRoot, node, page, primitiveRoot);
            var input = primitiveRoot.GetComponent<TMP_InputField>() ?? primitiveRoot.AddComponent<TMP_InputField>();
            if (primitiveRoot.TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = true;
                input.targetGraphic = graphic;
            }

            ConfigureSelectable(input, page.Theme);
            var placeholder = GetSlotText(slots, "Placeholder", primitiveRoot.transform.Find("__ai_TextArea/__ai_Placeholder"));
            ApplyNodeText(placeholder, node, page.Theme, string.IsNullOrWhiteSpace(node.Text) ? "Input" : node.Text);
            if (placeholder != null)
            {
                placeholder.color = ExtractTextColor(node, page.Theme) * new Color(1f, 1f, 1f, 0.55f);
            }

            var text = GetSlotText(slots, "Text", primitiveRoot.transform.Find("__ai_TextArea/__ai_Text"));
            ApplyNodeText(text, node, page.Theme, string.Empty);
            if (text != null)
            {
                text.text = string.Empty;
            }

            var viewport = GetSlotTransform(slots, "Text", primitiveRoot.transform.Find("__ai_TextArea/__ai_Text"))?.parent as RectTransform
                ?? primitiveRoot.transform.Find("__ai_TextArea") as RectTransform;
            if (viewport != null)
            {
                input.textViewport = viewport;
            }

            if (placeholder != null)
            {
                input.placeholder = placeholder;
            }

            if (text != null)
            {
                input.textComponent = text;
            }
        }

        private static void ConfigurePrimitiveToggle(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            var toggle = primitiveRoot.GetComponent<Toggle>() ?? primitiveRoot.AddComponent<Toggle>();
            ConfigureSelectable(toggle, page.Theme);

            var background = GetSlotGameObject(slots, "Background", primitiveRoot.transform.Find("__ai_Background"));
            var checkmark = GetSlotGameObject(slots, "Checkmark", primitiveRoot.transform.Find("__ai_Background/__ai_Checkmark"));
            ConfigureSecondaryShape(background, page.Theme != null ? page.Theme.panelFill : new Color(0.18f, 0.2f, 0.24f, 1f), 6f, 1f, page.Theme != null ? page.Theme.outlineColor : new Color(1f, 1f, 1f, 0.12f));
            ConfigureSecondaryShape(checkmark, page.Theme != null ? page.Theme.accentColor : Color.white, 4f);

            if (background != null)
            {
                var graphic = background.GetComponent<Graphic>();
                if (graphic != null)
                {
                    toggle.targetGraphic = graphic;
                }
            }

            if (checkmark != null)
            {
                var graphic = checkmark.GetComponent<Graphic>();
                if (graphic != null)
                {
                    toggle.graphic = graphic;
                }
            }

            var label = GetSlotText(slots, "Label", primitiveRoot.transform.Find("__ai_Label"));
            ApplyNodeText(label, node, page.Theme, node.Text);
        }

        private static void ConfigurePrimitiveSlider(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            var slider = primitiveRoot.GetComponent<Slider>() ?? primitiveRoot.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(ParseFloat(GetStyle(node, "-ai-value"), 0.5f));

            var track = GetSlotGameObject(slots, "Track", primitiveRoot.transform.Find("__ai_Track"));
            var fill = GetSlotGameObject(slots, "Fill", primitiveRoot.transform.Find("__ai_FillArea/__ai_Fill"));
            var handle = GetSlotGameObject(slots, "Handle", primitiveRoot.transform.Find("__ai_HandleArea/__ai_Handle"));
            var preset = ResolveVisualPreset(node, page.Theme);
            var radius = Mathf.Max(4f, ExtractCornerRadius(node, preset, page.Theme, node.ControlType) * 0.45f);
            var baseFill = ExtractFillColor(node, preset, page.Theme, false);
            var accent = page.Theme != null ? page.Theme.accentColor : new Color(0.92f, 0.77f, 0.34f, 1f);
            ConfigureSecondaryShape(track, Color.Lerp(baseFill, Color.black, 0.25f), radius);
            ConfigureSecondaryShape(fill, accent, radius);
            ConfigureSecondaryShape(handle, Color.white, radius + 2f);

            if (fill != null)
            {
                slider.fillRect = fill.GetComponent<RectTransform>();
            }

            if (handle != null)
            {
                slider.handleRect = handle.GetComponent<RectTransform>();
                slider.targetGraphic = handle.GetComponent<Graphic>();
            }
        }

        private static void ConfigurePrimitiveDropdown(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            ConfigureGraphicTarget(primitiveRoot, node, page, false);
            TryApplyImageAsset(primitiveRoot, node, page, primitiveRoot);
            var dropdown = primitiveRoot.GetComponent<TMP_Dropdown>() ?? primitiveRoot.AddComponent<TMP_Dropdown>();
            ConfigureSelectable(dropdown, page.Theme);
            if (primitiveRoot.TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = true;
                dropdown.targetGraphic = graphic;
            }

            var caption = GetSlotText(slots, "CaptionText", primitiveRoot.transform.Find("__ai_Label"));
            ApplyNodeText(caption, node, page.Theme, string.IsNullOrWhiteSpace(node.Text) ? "Option" : node.Text);
            dropdown.captionText = caption;

            var arrow = GetSlotText(slots, "Arrow", primitiveRoot.transform.Find("__ai_Arrow"));
            if (arrow != null)
            {
                ApplyFont(arrow, node, page.Theme);
                arrow.text = "▼";
                arrow.alignment = TextAlignmentOptions.Center;
                arrow.color = ExtractTextColor(node, page.Theme);
            }

            var templateRoot = GetSlotTransform(slots, "TemplateRoot", primitiveRoot.transform.Find("__ai_TemplateRoot"));
            var itemLabel = GetSlotText(slots, "ItemLabel", primitiveRoot.transform.Find("__ai_TemplateRoot/__ai_Viewport/__ai_Content/__ai_Item/__ai_ItemLabel"));
            if (templateRoot != null)
            {
                dropdown.template = templateRoot;
            }

            if (itemLabel != null)
            {
                ApplyNodeText(itemLabel, node, page.Theme, "Option");
                dropdown.itemText = itemLabel;
            }
        }

        private static void ConfigurePrimitiveScrollbar(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            var scrollbar = primitiveRoot.GetComponent<Scrollbar>() ?? primitiveRoot.AddComponent<Scrollbar>();
            var track = GetSlotGameObject(slots, "Track", primitiveRoot.transform.Find("__ai_Track"));
            var handle = GetSlotGameObject(slots, "Handle", primitiveRoot.transform.Find("__ai_SlidingArea/__ai_Handle"));
            var preset = ResolveVisualPreset(node, page.Theme);
            var radius = Mathf.Max(4f, ExtractCornerRadius(node, preset, page.Theme, node.ControlType) * 0.4f);
            var baseFill = ExtractFillColor(node, preset, page.Theme, false);
            ConfigureSecondaryShape(track, Color.Lerp(baseFill, Color.black, 0.22f), radius);
            ConfigureSecondaryShape(handle, baseFill, radius);
            if (handle != null)
            {
                scrollbar.handleRect = handle.GetComponent<RectTransform>();
                scrollbar.targetGraphic = handle.GetComponent<Graphic>();
            }
        }

        private static Transform ConfigurePrimitiveScrollView(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return null;
            }

            var scrollRect = primitiveRoot.GetComponent<ScrollRect>() ?? primitiveRoot.AddComponent<ScrollRect>();
            var viewport = GetSlotTransform(slots, "Viewport", primitiveRoot.transform.Find("__ai_Viewport"));
            var content = GetSlotTransform(slots, "Content", primitiveRoot.transform.Find("__ai_Viewport/__ai_Content"));
            var verticalScrollbar = GetSlotComponent<Scrollbar>(slots, "VerticalScrollbar", primitiveRoot.transform.Find("__ai_VerticalScrollbar"));
            var horizontalScrollbar = GetSlotComponent<Scrollbar>(slots, "HorizontalScrollbar", primitiveRoot.transform.Find("__ai_HorizontalScrollbar"));

            if (viewport != null)
            {
                scrollRect.viewport = viewport;
            }

            if (content != null)
            {
                scrollRect.content = content;
            }

            if (verticalScrollbar != null)
            {
                scrollRect.verticalScrollbar = verticalScrollbar;
            }

            if (horizontalScrollbar != null)
            {
                scrollRect.horizontalScrollbar = horizontalScrollbar;
            }

            ConfigurePrimitiveScrollbar(verticalScrollbar != null ? verticalScrollbar.gameObject : null, null, node, page);
            ConfigurePrimitiveScrollbar(horizontalScrollbar != null ? horizontalScrollbar.gameObject : null, null, node, page);
            return content != null ? content : primitiveRoot.transform;
        }

        private static void ConfigurePrimitiveImage(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            ConfigureGraphicTarget(primitiveRoot, node, page, false);
            TryApplyImageAsset(primitiveRoot, node, page);
            if (primitiveRoot.TryGetComponent<Graphic>(out var graphic))
            {
                graphic.raycastTarget = false;
            }

            if (HasImageLikeAssetRef(node) &&
                primitiveRoot.TryGetComponent<Image>(out var image) &&
                image != null)
            {
                image.color = ResolveImageTintColor(node, ResolvePrimaryImageAssetRef(node));
            }
        }

        private static void PrepareSvgSpriteAssets(IReadOnlyList<AIToUGUICompiledPage> pages)
        {
            if (pages == null)
            {
                return;
            }

            for (var i = 0; i < pages.Count; i++)
            {
                PrepareSvgSpriteAssets(pages[i]);
            }
        }

        private static void PrepareSvgSpriteAssets(AIToUGUICompiledPage page)
        {
            var settings = new AIToUGUISvgSpriteImportSettings
            {
                targetResolution = EditorPrefs.GetInt(SvgResolutionEditorPrefsKey, 512)
            };
            var result = AIToUGUI.Editor.AIToUGUISvgSpriteAssetUtility.PrepareAssetsForCompiledPage(page, false, settings);
            if (result.preparedCount > 0 || result.convertedCount > 0 || result.warnings.Count > 0 || result.errors.Count > 0)
            {
                AIToUGUI.Editor.AIToUGUISvgSpriteAssetUtility.LogResult(result, "SVG sprite preparation");
            }
        }

        private static void ConfigurePrimitiveProgress(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (primitiveRoot == null)
            {
                return;
            }

            var track = GetSlotGameObject(slots, "Track", primitiveRoot.transform.Find("__ai_Track"));
            var fill = GetSlotGameObject(slots, "Fill", primitiveRoot.transform.Find("__ai_Track/__ai_Fill"));
            var label = GetSlotText(slots, "Label", primitiveRoot.transform.Find("__ai_Label"));
            var preset = ResolveVisualPreset(node, page.Theme);
            var radius = Mathf.Max(4f, ExtractCornerRadius(node, preset, page.Theme, node.ControlType) * 0.5f);
            var baseFill = ExtractFillColor(node, preset, page.Theme, false);
            var accent = page.Theme != null ? page.Theme.accentColor : new Color(0.92f, 0.77f, 0.34f, 1f);
            ConfigureSecondaryShape(track, Color.Lerp(baseFill, Color.black, 0.22f), radius);
            ConfigureSecondaryShape(fill, accent, radius);
            var fillRect = fill != null ? fill.GetComponent<RectTransform>() : null;
            if (fillRect != null)
            {
                fillRect.anchorMin = new Vector2(0f, 0f);
                fillRect.anchorMax = new Vector2(Mathf.Clamp01(ParseFloat(GetStyle(node, "-ai-value"), 0.5f)), 1f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
            }

            if (label != null)
            {
                ApplyNodeText(label, node, page.Theme, node.Text);
            }
        }

        private static void TryConfigurePrimitiveIconSlot(GameObject primitiveRoot, AIToUGUIElementSlots slots, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (primitiveRoot == null || node == null)
            {
                return;
            }

            var iconRoot = GetSlotTransform(slots, "Icon", primitiveRoot.transform.Find("__ai_Icon"));
            if (iconRoot == null)
            {
                return;
            }

            var iconId = GetStyle(node, "-ai-icon");
            var iconText = iconRoot.GetComponent<TextMeshProUGUI>();
            if (string.IsNullOrWhiteSpace(iconId))
            {
                if (iconText != null)
                {
                    iconText.text = string.Empty;
                }

                return;
            }

            if (iconText == null)
            {
                iconText = iconRoot.gameObject.AddComponent<TextMeshProUGUI>();
            }

            ApplyFont(iconText, node, theme);
            iconText.text = ResolveIconLabel(iconId);
            iconText.color = theme != null ? theme.accentColor : Color.white;
            iconText.fontSize = Mathf.Max(18f, ParseFloat(GetStyle(node, "-ai-icon-size"), 24f));
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.raycastTarget = false;
        }

        private static void TryApplyImageAsset(GameObject target, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            TryApplyImageAsset(target, node, page, null);
        }

        private static void TryApplyImageAsset(GameObject host, AIToUGUICompiledNode node, AIToUGUICompiledPage page, GameObject explicitTarget)
        {
            if (host == null || node == null)
            {
                return;
            }

            var imageAssetRef = ResolvePrimaryImageAssetRef(node);
            if (imageAssetRef == null)
            {
                return;
            }

            var logicalPath = ResolveAssetLogicalPath(imageAssetRef);
            if (string.IsNullOrWhiteSpace(logicalPath))
            {
                return;
            }

            var sprite = LoadSpriteAssetForCompiledPage(imageAssetRef, logicalPath, page);
            if (sprite == null)
            {
                return;
            }

            var target = ResolveImageAssetTarget(host, node, explicitTarget);
            if (target == null)
            {
                return;
            }

            CleanupProceduralShapeArtifacts(target);
            if (!ReferenceEquals(host, target))
            {
                CleanupProceduralShapeArtifacts(host);
            }

            var image = EnsurePlainImageGraphic(target);
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.overrideSprite = null;
            image.color = ResolveImageTintColor(node, imageAssetRef);
            image.type = ResolveImageType(imageAssetRef.importMode);
            image.preserveAspect = image.type == Image.Type.Simple;
            image.pixelsPerUnitMultiplier = imageAssetRef.pixelsPerUnit > 0f ? imageAssetRef.pixelsPerUnit / 100f : 1f;
            image.raycastTarget = false;

            if (image.type == Image.Type.Simple && imageAssetRef.preferredWidth > 0f && imageAssetRef.preferredHeight > 0f)
            {
                var rectTransform = target.GetComponent<RectTransform>();
                if (rectTransform != null && rectTransform.sizeDelta == Vector2.zero)
                {
                    rectTransform.sizeDelta = new Vector2(imageAssetRef.preferredWidth, imageAssetRef.preferredHeight);
                }
            }
        }

        private static GameObject ResolveImageAssetTarget(GameObject host, AIToUGUICompiledNode node, GameObject explicitTarget)
        {
            if (explicitTarget != null)
            {
                return explicitTarget;
            }

            return ShouldUseDedicatedImageAssetVisual(node)
                ? EnsureInternalAssetVisual(host)
                : host;
        }

        private static bool ShouldUseDedicatedImageAssetVisual(AIToUGUICompiledNode node)
        {
            return false;
        }

        private static GameObject EnsureInternalAssetVisual(GameObject host)
        {
            if (host == null)
            {
                return null;
            }

            var existing = FindDescendantByName(host.transform, InternalAssetVisualName);
            var visual = existing != null ? existing.gameObject : CreateStretchChild(host, InternalAssetVisualName);
            if (visual == null)
            {
                return null;
            }

            if (visual.TryGetComponent<RectTransform>(out var rect))
            {
                StretchToParent(rect);
            }

            visual.transform.SetAsFirstSibling();
            return visual;
        }

        private static void RemoveInternalAssetVisual(GameObject host)
        {
            if (host == null)
            {
                return;
            }

            var visual = FindDescendantByName(host.transform, InternalAssetVisualName);
            if (visual != null)
            {
                AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(visual.gameObject);
            }
        }

        private static Transform FindDescendantByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                var nested = FindDescendantByName(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void CleanupProceduralShapeArtifacts(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            CleanupDashedBorder(target);
            RemoveComponentIfPresent<AIToUGUIShapeAdapter>(target);
            RemoveComponentIfPresent<AIToUGUIWindinatorShapeAdapter>(target);
            RemoveComponentIfPresent<AIToUGUIDashedBorderAdapter>(target);
            RemoveComponentIfPresent<AIToUGUIRouteLineAdapter>(target);
            RemoveComponentIfPresent<LineGraphic>(target);
        }

        private static Image EnsurePlainImageGraphic(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            Image resolved = null;
            var graphics = target.GetComponents<Graphic>();
            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                var isPlainImage = graphic is Image && !(graphic is global::DTT.UI.ProceduralUI.RoundedImage);
                if (isPlainImage)
                {
                    if (resolved == null)
                    {
                        resolved = (Image)graphic;
                        continue;
                    }
                }

                AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(graphic);
            }

            return resolved ?? target.AddComponent<Image>();
        }

        private static Sprite LoadSpriteAssetForCompiledPage(AIToUGUIAssetReference assetRef, string logicalPath, AIToUGUICompiledPage page)
        {
            if (string.IsNullOrWhiteSpace(logicalPath))
            {
                return null;
            }

            if (AIToUGUI.Editor.AIToUGUISvgSpriteAssetUtility.TryResolveSpriteAssetPath(page, assetRef, out var spriteAssetPath))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteAssetPath);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            if (!string.IsNullOrWhiteSpace(logicalPath))
            {
                var fallbackAssetPath = string.IsNullOrWhiteSpace(logicalPath)
                    ? string.Empty
                    : logicalPath.Replace("\\", "/").TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(fallbackAssetPath))
                {
                    var fallbackSprite = AssetDatabase.LoadAssetAtPath<Sprite>(fallbackAssetPath);
                    if (fallbackSprite != null)
                    {
                        return fallbackSprite;
                    }
                }
            }

            return null;
        }

        private static AIToUGUIAssetReference ResolvePrimaryImageAssetRef(AIToUGUICompiledNode node)
        {
            if (node?.AssetRefs == null)
            {
                return null;
            }

            for (var i = 0; i < node.AssetRefs.Count; i++)
            {
                var assetRef = node.AssetRefs[i];
                if (assetRef == null)
                {
                    continue;
                }

                switch (assetRef.assetType)
                {
                    case AIToUGUIAssetType.Icon:
                    case AIToUGUIAssetType.Ornament:
                    case AIToUGUIAssetType.Frame:
                    case AIToUGUIAssetType.Background:
                    case AIToUGUIAssetType.Snapshot:
                        return assetRef;
                }
            }

            return null;
        }

        private static bool HasImageLikeAssetRef(AIToUGUICompiledNode node)
        {
            return ResolvePrimaryImageAssetRef(node) != null;
        }

        private static string ResolveAssetLogicalPath(AIToUGUIAssetReference assetRef)
        {
            if (assetRef == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(assetRef.logicalAssetPath))
            {
                return assetRef.logicalAssetPath.Trim();
            }

            if (!string.IsNullOrWhiteSpace(assetRef.source))
            {
                return assetRef.source.Trim();
            }

            return assetRef.assetId ?? string.Empty;
        }

        private static Image.Type ResolveImageType(AIToUGUIAssetImportMode importMode)
        {
            return importMode switch
            {
                AIToUGUIAssetImportMode.NineSlice => Image.Type.Sliced,
                AIToUGUIAssetImportMode.Tile => Image.Type.Tiled,
                _ => Image.Type.Simple
            };
        }

        private static Color ResolveImageTintColor(AIToUGUICompiledNode node, AIToUGUIAssetReference assetRef)
        {
            var tintPolicy = assetRef != null ? assetRef.tintPolicy : string.Empty;
            if (string.Equals(tintPolicy, "none", StringComparison.OrdinalIgnoreCase))
            {
                return Color.white;
            }

            if (!string.IsNullOrWhiteSpace(GetStyle(node, "color")) &&
                TryParseColor(GetStyle(node, "color"), out var textColor))
            {
                return textColor;
            }

            if (!string.IsNullOrWhiteSpace(GetStyle(node, "background-color")) &&
                TryParseColor(GetStyle(node, "background-color"), out var backgroundColor) &&
                backgroundColor.a > 0.001f)
            {
                return backgroundColor;
            }

            return Color.white;
        }

        private static void ApplyNodeText(TextMeshProUGUI text, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme, string fallbackText)
        {
            if (text == null || node == null)
            {
                return;
            }

            ApplyFont(text, node, theme);
            text.text = fallbackText ?? string.Empty;
            text.color = ExtractTextColor(node, theme);
            text.fontSize = Mathf.Max(12f, ParseFloat(GetStyle(node, "font-size"), 24f));
            text.alignment = ResolveTextAlignment(node);
            text.raycastTarget = false;
            ConfigureTextLayout(text, node, ResolveInitialTextAvailableSize(node));
            ApplyTextStyling(text, node);
        }

        private static void ConfigureSecondaryShape(GameObject target, Color fillColor, float radius, float outlineWidth = 0f, Color? outlineColor = null)
        {
            if (target == null)
            {
                return;
            }

            var adapter = target.GetComponent<AIToUGUIShapeAdapter>() ?? target.AddComponent<AIToUGUIShapeAdapter>();
            adapter.Configure(
                fillColor.a > 0.001f,
                fillColor,
                false,
                Color.clear,
                AIToUGUIGradientDirection.None,
                radius,
                false,
                outlineWidth,
                outlineColor ?? Color.clear,
                0f,
                0f,
                Color.clear,
                false,
                Color.clear,
                0f,
                1f);
        }

        private static RectTransform GetSlotTransform(AIToUGUIElementSlots slots, string slotId, Transform fallback)
        {
            var slotTransform = slots != null ? slots.GetSlotTransform(slotId) : null;
            if (slotTransform != null)
            {
                return slotTransform;
            }

            return fallback as RectTransform ?? fallback?.GetComponent<RectTransform>();
        }

        private static GameObject GetSlotGameObject(AIToUGUIElementSlots slots, string slotId, Transform fallback)
        {
            return GetSlotTransform(slots, slotId, fallback)?.gameObject;
        }

        private static TextMeshProUGUI GetSlotText(AIToUGUIElementSlots slots, string slotId, Transform fallback)
        {
            var text = slots != null ? slots.GetPrimaryComponent<TextMeshProUGUI>(slotId) : null;
            if (text != null)
            {
                return text;
            }

            return fallback != null ? (fallback.GetComponent<TextMeshProUGUI>() ?? fallback.GetComponentInChildren<TextMeshProUGUI>(true)) : null;
        }

        private static T GetSlotComponent<T>(AIToUGUIElementSlots slots, string slotId, Transform fallback) where T : Component
        {
            var component = slots != null ? slots.GetPrimaryComponent<T>(slotId) : null;
            if (component != null)
            {
                return component;
            }

            return fallback != null ? (fallback.GetComponent<T>() ?? fallback.GetComponentInChildren<T>(true)) : null;
        }

        private static void CreateText(GameObject go, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme, bool createChildLabel)
        {
            if (go == null || node == null)
            {
                return;
            }

            var target = ResolveTextTarget(go, node, createChildLabel);
            if (target == null)
            {
                return;
            }

            var usesDedicatedLabel = !ReferenceEquals(target, go);
            if (usesDedicatedLabel && target.TryGetComponent<RectTransform>(out var targetRect))
            {
                ApplyContentPadding(targetRect, node);
            }

            var text = target.GetComponent<TextMeshProUGUI>() ?? target.AddComponent<TextMeshProUGUI>();
            if (text == null)
            {
                return;
            }

            ApplyFont(text, node, theme);
            text.text = node.Text ?? string.Empty;
            text.color = ExtractTextColor(node, theme);
            text.fontSize = Mathf.Max(12f, ParseFloat(GetStyle(node, "font-size"), 24f));
            text.alignment = ResolveTextAlignment(node);
            text.raycastTarget = false;
            ConfigureTextLayout(text, node, ResolveInitialTextAvailableSize(node));
            ApplyTextStyling(text, node);
        }

        private static GameObject ResolveTextTarget(GameObject go, AIToUGUICompiledNode node, bool createChildLabel)
        {
            if (go == null)
            {
                return null;
            }

            if (!createChildLabel && !HasConflictingGraphic(go))
            {
                return go;
            }

            var labelName = $"{InternalNodePrefix}Label";
            var existing = go.transform.Find(labelName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            return CreateStretchChild(go, labelName);
        }

        private static bool HasConflictingGraphic(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            if (go.GetComponent<TextMeshProUGUI>() != null)
            {
                return false;
            }

            return go.GetComponent<Graphic>() != null;
        }

        private static void CreateButton(GameObject go, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            var graphic = ResolveInteractiveGraphicTarget(go);
            if (graphic != null)
            {
                graphic.raycastTarget = true;
                button.targetGraphic = graphic;
            }

            ConfigureSelectable(button, page.Theme);

            if (IsTransparentOverlayButton(node))
            {
                var overlayGraphic = go.GetComponent<Graphic>();
                if (overlayGraphic == null)
                {
                    overlayGraphic = EnsurePlainImageGraphic(go);
                    if (overlayGraphic != null)
                    {
                        overlayGraphic.color = new Color(1f, 1f, 1f, 0.001f);
                    }
                }

                if (overlayGraphic != null)
                {
                    overlayGraphic.raycastTarget = true;
                    button.targetGraphic = overlayGraphic;
                }

                return;
            }

            var label = CreateStretchChild(go, $"{InternalNodePrefix}Label");
            if (label.TryGetComponent<RectTransform>(out var labelRect))
            {
                ApplyContentPadding(labelRect, node);
            }

            CreateText(label, node, page.Theme, false);
        }

        private static Transform CreateInput(GameObject go, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            var input = go.GetComponent<TMP_InputField>() ?? go.AddComponent<TMP_InputField>();
            var graphic = ResolveInteractiveGraphicTarget(go);
            if (graphic != null)
            {
                graphic.raycastTarget = true;
                input.targetGraphic = graphic;
            }

            var viewport = CreateStretchChild(go, $"{InternalNodePrefix}TextArea", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 8f), new Vector2(-12f, -8f));
            viewport.AddComponent<RectMask2D>();

            var placeholderGo = CreateStretchChild(viewport, $"{InternalNodePrefix}Placeholder");
            var textGo = CreateStretchChild(viewport, $"{InternalNodePrefix}Text");

            var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(placeholder, node, theme);
            placeholder.text = string.IsNullOrWhiteSpace(node.Text) ? "Input" : node.Text;
            placeholder.color = ExtractTextColor(node, theme) * new Color(1f, 1f, 1f, 0.55f);
            placeholder.fontSize = Mathf.Max(12f, ParseFloat(GetStyle(node, "font-size"), 22f));
            placeholder.alignment = ResolveTextAlignment(node);
            placeholder.raycastTarget = false;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(text, node, theme);
            text.text = string.Empty;
            text.color = ExtractTextColor(node, theme);
            text.fontSize = placeholder.fontSize;
            text.alignment = placeholder.alignment;
            text.raycastTarget = false;

            input.textViewport = viewport.GetComponent<RectTransform>();
            input.placeholder = placeholder;
            input.textComponent = text;
            ConfigureSelectable(input, theme);
            return viewport.transform;
        }

        private static Transform CreateScroll(GameObject go, AIToUGUICompiledNode node)
        {
            var scrollRect = go.GetComponent<ScrollRect>() ?? go.AddComponent<ScrollRect>();
            var viewport = CreateStretchChild(go, $"{InternalNodePrefix}Viewport");
            viewport.AddComponent<RectMask2D>();
            var viewportImage = viewport.GetComponent<Image>() ?? viewport.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);

            var content = CreateStretchChild(viewport, "Content");
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = ResolveNodeSize(node, new Vector2(640f, 360f), new Vector2(640f, 360f));

            var direction = GetStyle(node, "-ai-direction");
            if (direction == "v" || direction == "vertical")
            {
                var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.AddComponent<VerticalLayoutGroup>();
                layout.spacing = Mathf.Max(0f, ParseFloat(GetStyle(node, "gap"), 0f));
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.UpperLeft;

                var fitter = content.GetComponent<ContentSizeFitter>() ?? content.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scrollRect.horizontal = false;
                scrollRect.vertical = true;
            }
            else if (direction == "h" || direction == "horizontal")
            {
                var layout = content.GetComponent<HorizontalLayoutGroup>() ?? content.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = Mathf.Max(0f, ParseFloat(GetStyle(node, "gap"), 0f));
                layout.childControlWidth = false;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = true;
                layout.childAlignment = TextAnchor.UpperLeft;

                var fitter = content.GetComponent<ContentSizeFitter>() ?? content.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

                scrollRect.horizontal = true;
                scrollRect.vertical = false;
            }
            else
            {
                scrollRect.horizontal = true;
                scrollRect.vertical = true;
            }

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            return content.transform;
        }

        private static void CreateScrollbar(GameObject go, AIToUGUICompiledNode node)
        {
            var scrollbar = go.GetComponent<Scrollbar>() ?? go.AddComponent<Scrollbar>();
            var track = CreateStretchChild(go, $"{InternalNodePrefix}Track");
            var trackAdapter = track.GetComponent<AIToUGUIShapeAdapter>() ?? track.AddComponent<AIToUGUIShapeAdapter>();
            trackAdapter.Configure(true, new Color(0.16f, 0.19f, 0.24f, 1f), false, Color.clear, AIToUGUIGradientDirection.None, 8f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 1f);

            var slidingArea = CreateStretchChild(go, $"{InternalNodePrefix}SlidingArea", new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = CreateAnchoredChild(slidingArea, $"{InternalNodePrefix}Handle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 48f), Vector2.zero);
            var handleAdapter = handle.GetComponent<AIToUGUIShapeAdapter>() ?? handle.AddComponent<AIToUGUIShapeAdapter>();
            handleAdapter.Configure(true, Color.white, false, Color.clear, AIToUGUIGradientDirection.None, 8f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 1f);

            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handle.GetComponent<Graphic>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
        }

        private static void CreateToggle(GameObject go, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            var toggle = go.GetComponent<Toggle>() ?? go.AddComponent<Toggle>();
            ConfigureSelectable(toggle, theme);

            var box = CreateChild(go, $"{InternalNodePrefix}Background");
            var boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.sizeDelta = new Vector2(26f, 26f);
            boxRect.anchoredPosition = Vector2.zero;

            var outlineColor = theme != null ? theme.outlineColor : new Color(1f, 1f, 1f, 0.12f);
            var boxAdapter = box.AddComponent<AIToUGUIShapeAdapter>();
            boxAdapter.Configure(true, new Color(0.18f, 0.2f, 0.24f, 1f), false, Color.clear, AIToUGUIGradientDirection.None, 6f, false, 1f, outlineColor, 0f, 0f, Color.clear, false, Color.clear, 0f, 0f);

            var check = CreateStretchChild(box, $"{InternalNodePrefix}Checkmark", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(4f, 4f), new Vector2(-4f, -4f));
            var checkAdapter = check.AddComponent<AIToUGUIShapeAdapter>();
            checkAdapter.Configure(true, theme != null ? theme.accentColor : Color.white, false, Color.clear, AIToUGUIGradientDirection.None, 4f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 0f);

            var label = CreateStretchChild(go, $"{InternalNodePrefix}Label");
            label.GetComponent<RectTransform>().offsetMin = new Vector2(38f, 0f);
            CreateText(label, node, theme, false);

            toggle.targetGraphic = box.GetComponent<Graphic>();
            toggle.graphic = check.GetComponent<Graphic>();
        }

        private static void CreateSlider(GameObject go, AIToUGUICompiledNode node)
        {
            var slider = go.GetComponent<Slider>() ?? go.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(ParseFloat(GetStyle(node, "-ai-value"), 0.5f));

            var background = CreateStretchChild(go, $"{InternalNodePrefix}Background", new Vector2(0f, 0.35f), new Vector2(1f, 0.65f));
            background.AddComponent<AIToUGUIShapeAdapter>().Configure(true, new Color(0.16f, 0.19f, 0.24f, 1f), false, Color.clear, AIToUGUIGradientDirection.None, 8f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 0f);

            var fillArea = CreateStretchChild(go, $"{InternalNodePrefix}FillArea", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(8f, 0f), new Vector2(-16f, 0f));
            var fill = CreateStretchChild(fillArea, "Fill");
            fill.AddComponent<AIToUGUIShapeAdapter>().Configure(true, new Color(0.92f, 0.77f, 0.34f, 1f), false, Color.clear, AIToUGUIGradientDirection.None, 8f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 0f);

            var handleArea = CreateStretchChild(go, $"{InternalNodePrefix}HandleSlideArea", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
            var handle = CreateChild(handleArea, "Handle");
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.sizeDelta = new Vector2(20f, 0f);
            handle.AddComponent<AIToUGUIShapeAdapter>().Configure(true, Color.white, false, Color.clear, AIToUGUIGradientDirection.None, 10f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 0f);

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Graphic>();
        }

        private static void CreateDropdown(GameObject go, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            var dropdown = go.GetComponent<TMP_Dropdown>() ?? go.AddComponent<TMP_Dropdown>();
            var graphic = ResolveInteractiveGraphicTarget(go);
            if (graphic != null)
            {
                graphic.raycastTarget = true;
                dropdown.targetGraphic = graphic;
            }

            var label = CreateStretchChild(go, $"{InternalNodePrefix}Label", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 8f), new Vector2(-40f, -8f));
            CreateText(label, node, theme, false);
            dropdown.captionText = label.GetComponent<TextMeshProUGUI>();

            var arrow = CreateChild(go, $"{InternalNodePrefix}Arrow");
            var arrowRect = arrow.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1f, 0.5f);
            arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.sizeDelta = new Vector2(16f, 16f);
            arrowRect.anchoredPosition = new Vector2(-16f, 0f);
            var arrowText = arrow.AddComponent<TextMeshProUGUI>();
            ApplyFont(arrowText, node, theme);
            arrowText.text = "▼";
            arrowText.color = ExtractTextColor(node, theme);
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.fontSize = 18f;
            arrowText.raycastTarget = false;

            dropdown.options = new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("Option A"),
                new TMP_Dropdown.OptionData("Option B")
            };

            ConfigureSelectable(dropdown, theme);
        }

        private static void CreateImage(GameObject go)
        {
            var image = EnsurePlainImageGraphic(go);
            if (image == null)
            {
                return;
            }

            image.color = new Color(1f, 1f, 1f, 0.08f);
            image.raycastTarget = false;
        }

        private static Graphic ResolveInteractiveGraphicTarget(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            if (target.TryGetComponent<Graphic>(out var directGraphic))
            {
                return directGraphic;
            }

            var assetVisual = FindDescendantByName(target.transform, InternalAssetVisualName);
            return assetVisual != null ? assetVisual.GetComponent<Graphic>() : null;
        }

        private static void CreateProgress(GameObject go, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            var track = CreateStretchChild(go, $"{InternalNodePrefix}Track");
            var trackAdapter = track.GetComponent<AIToUGUIShapeAdapter>() ?? track.AddComponent<AIToUGUIShapeAdapter>();
            trackAdapter.Configure(true, new Color(0.16f, 0.19f, 0.24f, 1f), false, Color.clear, AIToUGUIGradientDirection.None, 8f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 1f);

            var fill = CreateStretchChild(track, $"{InternalNodePrefix}Fill");
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(ParseFloat(GetStyle(node, "-ai-value"), 0.5f)), 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillAdapter = fill.GetComponent<AIToUGUIShapeAdapter>() ?? fill.AddComponent<AIToUGUIShapeAdapter>();
            fillAdapter.Configure(true, theme != null ? theme.accentColor : Color.white, false, Color.clear, AIToUGUIGradientDirection.None, 8f, false, 0f, Color.clear, 0f, 0f, Color.clear, false, Color.clear, 0f, 1f);

            if (!string.IsNullOrWhiteSpace(node.Text))
            {
                var label = CreateStretchChild(go, $"{InternalNodePrefix}Label");
                CreateText(label, node, theme, false);
            }
        }

        private static void TryCreateIconPlaceholder(GameObject go, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            var iconId = GetStyle(node, "-ai-icon");
            if (string.IsNullOrWhiteSpace(iconId) || go.transform.Find($"{InternalNodePrefix}Icon") != null)
            {
                return;
            }

            var icon = CreateStretchChild(go, $"{InternalNodePrefix}Icon");
            var iconText = icon.AddComponent<TextMeshProUGUI>();
            ApplyFont(iconText, node, theme);
            iconText.text = ResolveIconLabel(iconId);
            iconText.color = theme != null ? theme.accentColor : Color.white;
            iconText.fontSize = Mathf.Max(18f, ParseFloat(GetStyle(node, "-ai-icon-size"), 24f));
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.raycastTarget = false;
        }

        private static string ResolveIconLabel(string iconId)
        {
            if (string.IsNullOrWhiteSpace(iconId))
            {
                return "◆";
            }

            var normalized = iconId.Contains(":") ? iconId.Substring(iconId.IndexOf(':') + 1) : iconId;
            var parts = normalized.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "◆";
            }

            if (parts.Length == 1)
            {
                return parts[0].Substring(0, 1).ToUpperInvariant();
            }

            return string.Concat(parts.Take(2).Select(part => part.Substring(0, 1).ToUpperInvariant()));
        }

        private static void ApplyFont(TextMeshProUGUI text, AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (text == null)
            {
                return;
            }

            var fontAsset = ResolveFont(node, theme);
            if (fontAsset == null)
            {
                fontAsset = ResolveFallbackFontAsset();
            }

            if (fontAsset == null)
            {
                return;
            }

            text.font = fontAsset;
            text.UpdateFontAsset();
        }

        private static void ConfigureSelectable(Selectable selectable, AIToUGUIThemeDefinition theme)
        {
            if (selectable == null || theme == null)
            {
                return;
            }

            selectable.transition = Selectable.Transition.ColorTint;
            var colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.96f);
            colors.pressedColor = Color.Lerp(theme.accentColor, Color.white, 0.2f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            selectable.colors = colors;
        }

        private static void AttachMotion(GameObject go, AIToUGUICompiledNode node, AIToUGUICompiledPage page, bool isRoot)
        {
            if (go == null || node == null || page == null)
            {
                return;
            }

            TryAttachTitleTypewriter(go, node);

            var motionTarget = ResolveMotionTarget(go, node);
            var loopPreset = ResolveLoopMotionPreset(node, page.Theme);
            if (HasClass(node, "ai-selectable-card"))
            {
                TryAttachSelectableCard(motionTarget, node, page);
                AttachLoopMotion(motionTarget, node, loopPreset);
                return;
            }

            var preset = ResolveMotionPreset(node, page.Theme, isRoot);
            var requiresAnimationBinder = preset != null || node.ControlType == AIToUGUIControlType.Button || isRoot;
            if (!requiresAnimationBinder && loopPreset == null)
            {
                return;
            }

            if (requiresAnimationBinder)
            {
                var binder = motionTarget.GetComponent<AIToUGUIAnimationBinder>() ?? motionTarget.AddComponent<AIToUGUIAnimationBinder>();
                if (preset != null)
                {
                    binder.ApplyPreset(preset);
                }

                var listenToPointerEvents = node.ControlType == AIToUGUIControlType.Button;
                binder.SetListenToPointerEvents(listenToPointerEvents);

                if (listenToPointerEvents)
                {
                    binder.ApplyRecommendedButtonFeedback();
                }

                if (motionTarget.TryGetComponent<Graphic>(out var motionGraphic))
                {
                    motionGraphic.raycastTarget = AIToUGUIInteractionUtility.ShouldGraphicReceiveRaycasts(binder);
                }

                TryAttachMotionBridge(motionTarget);
            }

            AttachLoopMotion(motionTarget, node, loopPreset);
        }

        private static GameObject ResolveMotionTarget(GameObject go, AIToUGUICompiledNode node)
        {
            if (go == null)
            {
                return null;
            }

            if (node != null && AIToUGUIElementContractUtility.IsPrimitiveElement(node.ElementId))
            {
                var primitiveRoot = go.transform.Find(InternalPrimitiveRootName);
                if (primitiveRoot != null)
                {
                    return primitiveRoot.gameObject;
                }
            }

            return go;
        }

        private static void TryAttachMotionBridge(GameObject target)
        {
            if (target == null ||
                target.GetComponent<AIToUGUIAnimationBinder>() == null ||
                target.GetComponent<BaseElement>() == null)
            {
                return;
            }

            if (target.GetComponent<global::AIToUGUIBaseElementMotionBridge>() == null)
            {
                target.AddComponent<global::AIToUGUIBaseElementMotionBridge>();
            }
        }

        private static void TryAttachTitleTypewriter(GameObject go, AIToUGUICompiledNode node)
        {
            if (go == null || !HasClass(node, "ai-title-typewriter"))
            {
                return;
            }

            var text = FindPrimaryText(go);
            if (text == null)
            {
                return;
            }

            var typewriter = text.GetComponent<AIToUGUITextTypewriter>() ?? text.gameObject.AddComponent<AIToUGUITextTypewriter>();
            typewriter.Configure(
                ParseFloat(GetAttribute(node, "data-ai-typewriter-speed"), 30f),
                ParseFloat(GetAttribute(node, "data-ai-typewriter-delay"), 0.18f),
                ParseFloat(GetAttribute(node, "data-ai-typewriter-pause"), 0.08f));
        }

        private static void TryAttachSelectableCard(GameObject motionTarget, AIToUGUICompiledNode node, AIToUGUICompiledPage page)
        {
            if (motionTarget == null || node == null || page == null)
            {
                return;
            }

            var existingBinder = motionTarget.GetComponent<AIToUGUIAnimationBinder>();
            if (existingBinder != null)
            {
                UnityEngine.Object.DestroyImmediate(existingBinder);
            }

            var existingBridge = motionTarget.GetComponent<global::AIToUGUIBaseElementMotionBridge>();
            if (existingBridge != null)
            {
                UnityEngine.Object.DestroyImmediate(existingBridge);
            }

            var selectableCard = motionTarget.GetComponent<AIToUGUISelectableCard>() ?? motionTarget.AddComponent<AIToUGUISelectableCard>();
            var selectGroup = GetAttribute(node, "data-ai-select-group");
            if (string.IsNullOrWhiteSpace(selectGroup))
            {
                selectGroup = $"{page.PageId}/selectable-card";
            }

            var selectedAttribute = GetAttribute(node, "data-ai-selected");
            var startSelected = string.Equals(selectedAttribute, "true", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(selectedAttribute, "1", StringComparison.OrdinalIgnoreCase);
            var accentColor = new Color(1f, 0.82f, 0.36f, 1f);
            if (page.Theme != null)
            {
                accentColor = page.Theme.accentColor.grayscale > 0.85f
                    ? page.Theme.outlineColor
                    : Color.Lerp(page.Theme.outlineColor, page.Theme.accentColor, 0.35f);
            }
            selectableCard.Configure(selectGroup, accentColor, startSelected);
            selectableCard.ApplyRecommendedProfile();

            if (motionTarget.TryGetComponent<Graphic>(out var cardGraphic))
            {
                cardGraphic.raycastTarget = AIToUGUIInteractionUtility.ShouldGraphicReceiveRaycasts(selectableCard);
            }
        }

        private static void TryAttachPanelComponent(GameObject root, AIToUGUICompiledPage page)
        {
            if (root == null || page == null || !page.AttachPanelComponent || string.IsNullOrWhiteSpace(page.PanelComponentTypeName))
            {
                return;
            }

            var type = FindPanelType(page.PanelComponentTypeName);
            if (type == null || !typeof(BasePanel).IsAssignableFrom(type))
            {
                page.Warnings.Add($"未找到可挂载的面板类型 {page.PanelComponentTypeName}。");
                return;
            }

            if (root.GetComponent(type) == null)
            {
                root.AddComponent(type);
            }
        }

        private static Type FindPanelType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(candidate => candidate != null).ToArray();
                }

                var type = types.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(candidate.FullName, typeName, StringComparison.Ordinal));
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        internal static string BuildPrefabPath(AIToUGUISiteDefinition site, AIToUGUICompiledPage page)
        {
            var siteId = site != null
                ? (string.IsNullOrWhiteSpace(site.siteId) ? "Site" : site.siteId)
                : page != null ? page.SiteId : "Site";
            var folder = ResolvePrefabOutputFolder(siteId, site != null ? site.prefabOutputRoot : page?.PrefabOutputRoot);
            return $"{folder}/{page.PrefabName}.prefab";
        }

        internal static string BuildMetadataPath(AIToUGUISiteDefinition site, AIToUGUICompiledPage page)
        {
            var siteId = site != null
                ? (string.IsNullOrWhiteSpace(site.siteId) ? "Site" : site.siteId)
                : page != null ? page.SiteId : "Site";
            var folder = ResolveMetadataOutputFolder(siteId, site != null ? site.metadataOutputRoot : page?.MetadataOutputRoot);
            var fileName = string.IsNullOrWhiteSpace(page.PageId) ? page.PrefabName : page.PageId;
            return $"{folder}/{fileName}.asset";
        }

        internal static string BuildPrefabPath(AIToUGUICompiledPage page)
        {
            return BuildPrefabPath((AIToUGUISiteDefinition)null, page);
        }

        internal static string BuildMetadataPath(AIToUGUICompiledPage page)
        {
            return BuildMetadataPath((AIToUGUISiteDefinition)null, page);
        }

        private static string ResolvePrefabOutputFolder(string siteId, string folder)
        {
            var resolved = AIToUGUIGeneratedAssetPaths.ResolvePrefabOutputRoot(siteId, folder);
            return AIToUGUIGeneratedAssetPaths.NormalizeAssetFolder(resolved);
        }

        private static string ResolveMetadataOutputFolder(string siteId, string folder)
        {
            var resolved = AIToUGUIGeneratedAssetPaths.ResolveMetadataOutputRoot(siteId, folder);
            return AIToUGUIGeneratedAssetPaths.NormalizeAssetFolder(resolved);
        }

        private static void TryMigrateLegacyManagedOutputFolders(AIToUGUICompiledPage page)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.SiteId))
            {
                return;
            }

            var prefabFolder = ResolvePrefabOutputFolder(page.SiteId, page.PrefabOutputRoot);
            var metadataFolder = ResolveMetadataOutputFolder(page.SiteId, page.MetadataOutputRoot);
            TryMigrateLegacyGeneratedFolder(page.SiteId, prefabFolder, AIToUGUIGeneratedAssetPaths.GetLegacyPrefabFolder(page.SiteId));
            TryMigrateLegacyGeneratedFolder(page.SiteId, metadataFolder, AIToUGUIGeneratedAssetPaths.GetLegacyMetadataFolder(page.SiteId));
        }

        private static void TryMigrateLegacyGeneratedFolder(string siteId, string targetFolder, string legacyFolder)
        {
            if (string.IsNullOrWhiteSpace(siteId) ||
                string.IsNullOrWhiteSpace(targetFolder) ||
                !AIToUGUIGeneratedAssetPaths.IsPackageFolder(siteId, targetFolder) ||
                AssetDatabase.IsValidFolder(targetFolder) ||
                !AssetDatabase.IsValidFolder(legacyFolder))
            {
                return;
            }

            var packageRoot = AIToUGUIGeneratedAssetPaths.GetPackageRoot(siteId);
            EnsureFolder(packageRoot);
            AssetDatabase.MoveAsset(legacyFolder, targetFolder);
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

        private static AIToUGUIBakeMetadata UpdateMetadata(AIToUGUICompiledPage page, string prefabPath, string metadataPath, AIToUGUIBuiltNode builtRoot)
        {
            return UpdateMetadata(page, prefabPath, metadataPath, CollectExportedNodes(builtRoot));
        }

        private static AIToUGUIBakeMetadata UpdateMetadata(
            AIToUGUICompiledPage page,
            string prefabPath,
            string metadataPath,
            List<AIToUGUIBakeExportedNodeInfo> exportedNodes)
        {
            var metadata = AssetDatabase.LoadAssetAtPath<AIToUGUIBakeMetadata>(metadataPath);
            if (metadata == null)
            {
                metadata = ScriptableObject.CreateInstance<AIToUGUIBakeMetadata>();
                AssetDatabase.CreateAsset(metadata, metadataPath);
            }

            metadata.siteId = page.SiteId;
            metadata.pageId = page.PageId;
            metadata.runtimePageId = page.RuntimePageId;
            metadata.themeId = page.Theme != null ? page.Theme.themeId : string.Empty;
            metadata.bundleJsonAssetPath = page.SourceBundle != null && page.SourceBundle.bundleJson != null
                ? AssetDatabase.GetAssetPath(page.SourceBundle.bundleJson)
                : page.SourceBundleJsonAssetPath;
            metadata.manifestAssetPath = page.SourceSite != null && page.SourceSite.manifestAsset != null
                ? AssetDatabase.GetAssetPath(page.SourceSite.manifestAsset)
                : string.Empty;
            metadata.htmlAssetPath = page.SourcePage != null && page.SourcePage.htmlAsset != null
                ? AssetDatabase.GetAssetPath(page.SourcePage.htmlAsset)
                : string.Empty;
            metadata.sourceRelativePath = page.SourcePage != null ? page.SourcePage.sourceRelativePath : string.Empty;
            metadata.prefabAssetPath = prefabPath;
            metadata.resourceLogicalPath = page.LogicalPath;
            metadata.attachPanelComponent = page.AttachPanelComponent;
            metadata.panelComponentTypeName = page.PanelComponentTypeName;
            metadata.targetLayer = page.TargetLayer;
            metadata.generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            metadata.designResolution = page.DesignResolution;
            metadata.errors = page.Errors.Distinct().ToList();
            metadata.warnings = page.Warnings.Distinct().ToList();
            metadata.exportedNodes = exportedNodes ?? new List<AIToUGUIBakeExportedNodeInfo>();
            EditorUtility.SetDirty(metadata);
            return metadata;
        }

        private static void BindMetadataToPageRoot(GameObject root, AIToUGUICompiledPage page, AIToUGUIBakeMetadata metadata)
        {
            if (root == null || page == null || !root.TryGetComponent<AIToUGUIPageRoot>(out var pageRoot))
            {
                return;
            }

            pageRoot.siteId = page.SiteId;
            pageRoot.pageId = page.PageId;
            pageRoot.runtimePageId = page.RuntimePageId;
            pageRoot.resourceLogicalPath = page.LogicalPath;
            pageRoot.targetLayer = page.TargetLayer;
            pageRoot.BindRuntimeMetadata(page.RuntimePageId, metadata);
        }

        private static void ValidateCompiledPageForBake(AIToUGUICompiledPage page, GameObject root, AIToUGUIBuiltNode builtRoot)
        {
            if (page == null)
            {
                return;
            }

            ValidateRuntimePageId(page);
            ValidateRuntimePageIdConflicts(page);

            if (root == null || builtRoot == null)
            {
                AddMessage(page.Errors, "页面构建结果为空，无法执行烘焙校验。");
                return;
            }

            ValidateExportedNodeNames(page, builtRoot);
            ValidateSemanticIdentifiers(page, root.transform);

            if (page.AttachPanelComponent && !string.IsNullOrWhiteSpace(page.PanelComponentTypeName))
            {
                ValidatePanelBindings(page, root.transform);
            }

            DeduplicateMessages(page.Errors);
            DeduplicateMessages(page.Warnings);
        }

        private static void ValidateCompiledPreviewForBake(AIToUGUICompiledPage page, Transform root)
        {
            if (page == null)
            {
                return;
            }

            ValidateRuntimePageId(page);
            ValidateRuntimePageIdConflicts(page);

            if (root == null)
            {
                AddMessage(page.Errors, "当前预览页为空，无法导出。");
                return;
            }

            ValidateExportedNodeNamesFromHierarchy(page, root);
            ValidateSemanticIdentifiers(page, root);

            if (page.AttachPanelComponent && !string.IsNullOrWhiteSpace(page.PanelComponentTypeName))
            {
                ValidatePanelBindings(page, root);
            }

            DeduplicateMessages(page.Errors);
            DeduplicateMessages(page.Warnings);
        }

        private static void ValidateExportedNodeNames(AIToUGUICompiledPage page, AIToUGUIBuiltNode builtRoot)
        {
            if (page == null || builtRoot == null)
            {
                return;
            }

            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            ValidateExportedNodeNamesRecursive(page, builtRoot, seen);
        }

        private static void ValidateExportedNodeNamesRecursive(
            AIToUGUICompiledPage page,
            AIToUGUIBuiltNode builtNode,
            Dictionary<string, string> seen)
        {
            if (page == null || builtNode == null || builtNode.Node == null)
            {
                return;
            }

            var nodeName = builtNode.Node.Name;
            if (!string.IsNullOrWhiteSpace(nodeName))
            {
                if (nodeName.StartsWith(InternalNodePrefix, StringComparison.Ordinal))
                {
                    AddMessage(page.Errors, $"Exported node '{nodeName}' uses reserved prefix '{InternalNodePrefix}'.");
                }

                var path = BuildNodePath(builtNode);
                if (seen.TryGetValue(nodeName, out var existingPath))
                {
                    AddMessage(page.Errors, $"Duplicate exported node name '{nodeName}' found at '{existingPath}' and '{path}'.");
                }
                else
                {
                    seen.Add(nodeName, path);
                }
            }

            for (var i = 0; i < builtNode.Children.Count; i++)
            {
                ValidateExportedNodeNamesRecursive(page, builtNode.Children[i], seen);
            }
        }

        private static void ValidateExportedNodeNamesFromHierarchy(AIToUGUICompiledPage page, Transform root)
        {
            if (page == null || root == null)
            {
                return;
            }

            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var markers = root.GetComponentsInChildren<AIToUGUIExportNodeMarker>(true);
            if (markers != null && markers.Length > 0)
            {
                for (var i = 0; i < markers.Length; i++)
                {
                    var marker = markers[i];
                    if (marker == null)
                    {
                        continue;
                    }

                    var current = marker.transform;
                    var nodeName = current.name;
                    if (string.IsNullOrWhiteSpace(nodeName))
                    {
                        continue;
                    }

                    var path = AnimationUtility.CalculateTransformPath(current, root);
                    path = string.IsNullOrWhiteSpace(path) ? root.name : $"{root.name}/{path}";
                    if (nodeName.StartsWith(InternalNodePrefix, StringComparison.Ordinal))
                    {
                        AddMessage(page.Errors, $"Exported node '{nodeName}' uses reserved prefix '{InternalNodePrefix}'.");
                        continue;
                    }

                    if (seen.TryGetValue(nodeName, out var existingMarkedPath))
                    {
                        AddMessage(page.Errors, $"Duplicate exported node name '{nodeName}' found at '{existingMarkedPath}' and '{path}'.");
                    }
                    else
                    {
                        seen.Add(nodeName, path);
                    }
                }

                return;
            }

            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                var nodeName = current.name;
                var isInternalGenerated = nodeName.StartsWith(InternalNodePrefix, StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(nodeName) && !isInternalGenerated)
                {
                    var path = AnimationUtility.CalculateTransformPath(current, root);
                    path = string.IsNullOrWhiteSpace(path) ? root.name : $"{root.name}/{path}";
                    if (seen.TryGetValue(nodeName, out var existingPath))
                    {
                        AddMessage(page.Errors, $"Duplicate exported node name '{nodeName}' found at '{existingPath}' and '{path}'.");
                    }
                    else
                    {
                        seen.Add(nodeName, path);
                    }
                }

                for (var i = current.childCount - 1; i >= 0; i--)
                {
                    stack.Push(current.GetChild(i));
                }
            }
        }

        private static string BuildNodePath(AIToUGUIBuiltNode builtNode)
        {
            if (builtNode?.GameObject == null)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var current = builtNode.GameObject.transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        internal static bool ValidateRuntimePageIds(IReadOnlyList<AIToUGUICompiledPage> pages, List<string> errors)
        {
            if (pages == null || errors == null)
            {
                return true;
            }

            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var success = true;
            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                if (page == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(page.RuntimePageId))
                {
                    errors.Add($"页面 {page.PageId} 缺少 runtimePageId。");
                    success = false;
                    continue;
                }

                if (seen.TryGetValue(page.RuntimePageId, out var existingPageId))
                {
                    errors.Add($"runtimePageId '{page.RuntimePageId}' 同时被页面 {existingPageId} 和 {page.PageId} 使用。");
                    success = false;
                    continue;
                }

                seen.Add(page.RuntimePageId, page.PageId);
            }

            return success;
        }

        private static void ValidateRuntimePageId(AIToUGUICompiledPage page)
        {
            if (page == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(page.RuntimePageId))
            {
                AddMessage(page.Errors, $"页面 {page.PageId} 缺少 runtimePageId。");
            }
        }

        private static void ValidateRuntimePageIdConflicts(AIToUGUICompiledPage page)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.RuntimePageId))
            {
                return;
            }

            if (page.SourceBundle != null && page.SourceBundle.pages != null)
            {
                var duplicateCount = page.SourceBundle.pages.Count(candidate =>
                    candidate != null && string.Equals(candidate.runtimePageId, page.RuntimePageId, StringComparison.Ordinal));
                if (duplicateCount > 1)
                {
                    AddMessage(page.Errors, $"runtimePageId '{page.RuntimePageId}' 在当前 bundle 配置中重复。");
                }
            }

            var registry = LoadOrCreateRuntimeRegistry();
            if (registry == null || registry.pages == null)
            {
                return;
            }

            for (var i = 0; i < registry.pages.Count; i++)
            {
                var entry = registry.pages[i];
                if (entry == null || !string.Equals(entry.runtimePageId, page.RuntimePageId, StringComparison.Ordinal))
                {
                    continue;
                }

                var samePage = string.Equals(entry.siteId, page.SiteId, StringComparison.Ordinal) &&
                               string.Equals(entry.pageId, page.PageId, StringComparison.Ordinal);
                if (!samePage)
                {
                    AddMessage(page.Errors, $"runtimePageId '{page.RuntimePageId}' 已被 {entry.siteId}/{entry.pageId} 占用。");
                }
            }
        }

        private static void ValidatePanelBindings(AIToUGUICompiledPage page, Transform root)
        {
            if (page == null || !page.AttachPanelComponent || string.IsNullOrWhiteSpace(page.PanelComponentTypeName))
            {
                return;
            }

            var type = FindPanelType(page.PanelComponentTypeName);
            if (type == null || !typeof(BasePanel).IsAssignableFrom(type))
            {
                AddMessage(page.Errors, $"未找到可用于校验的面板类型 {page.PanelComponentTypeName}。");
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = type.GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var attr = field.GetCustomAttribute<BindFieldAttribute>(true);
                if (attr == null || !typeof(Component).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                if (!UIAutoBinder.TryResolveComponent(root, attr.BindName, field.FieldType, out _))
                {
                    AddMessage(page.Errors, $"[BindField] {type.Name}.{field.Name} <- '{attr.BindName}' ({field.FieldType.Name}) 绑定失败。");
                }
            }

            var methods = type.GetMethods(flags);
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                var attrs = method.GetCustomAttributes<BindActionAttribute>(true);
                foreach (var attr in attrs)
                {
                    if (!ValidateActionBinding(root, attr))
                    {
                        AddMessage(page.Errors, $"[BindAction] {type.Name}.{method.Name} <- '{attr.BindField}' ({attr.BehaviourType}) 绑定失败。");
                    }
                }
            }
        }

        private static bool ValidateActionBinding(Transform root, BindActionAttribute attr)
        {
            if (root == null || attr == null)
            {
                return false;
            }

            var candidateTypes = UIAutoBinder.GetBehaviourComponentTypes(attr.BehaviourType);
            for (var i = 0; i < candidateTypes.Count; i++)
            {
                if (UIAutoBinder.TryResolveComponent(root, attr.BindField, candidateTypes[i], out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateSemanticIdentifiers(AIToUGUICompiledPage page, Transform root)
        {
            if (page == null || root == null)
            {
                return;
            }

            var slotIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var containerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var templateIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var markers = root.GetComponentsInChildren<AIToUGUIExportNodeMarker>(true);
            if (markers == null)
            {
                return;
            }

            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                ValidateSemanticIdentifier(page, root, marker.transform, "data-ui-slot", marker.slotId, slotIds);
                ValidateSemanticIdentifier(page, root, marker.transform, "data-ui-container", marker.containerId, containerIds);
                ValidateSemanticIdentifier(page, root, marker.transform, "data-ui-template", marker.templateId, templateIds);
            }
        }

        private static void ValidateSemanticIdentifier(
            AIToUGUICompiledPage page,
            Transform root,
            Transform current,
            string attributeName,
            string attributeValue,
            Dictionary<string, string> seen)
        {
            if (page == null ||
                root == null ||
                current == null ||
                seen == null ||
                string.IsNullOrWhiteSpace(attributeName) ||
                string.IsNullOrWhiteSpace(attributeValue))
            {
                return;
            }

            var path = AnimationUtility.CalculateTransformPath(current, root);
            path = string.IsNullOrWhiteSpace(path) ? root.name : $"{root.name}/{path}";
            if (seen.TryGetValue(attributeValue, out var existingPath))
            {
                AddMessage(page.Errors, $"Duplicate {attributeName} '{attributeValue}' found at '{existingPath}' and '{path}'.");
                return;
            }

            seen.Add(attributeValue, path);
        }

        private static void TryGeneratePanelViewScript(
            AIToUGUICompiledPage page,
            IReadOnlyList<AIToUGUIBakeExportedNodeInfo> exportedNodes)
        {
            if (page == null ||
                !page.AttachPanelComponent ||
                string.IsNullOrWhiteSpace(page.PanelComponentTypeName))
            {
                return;
            }

            var panelType = FindPanelType(page.PanelComponentTypeName);
            if (panelType != null && !typeof(AIToUGUIViewPanel).IsAssignableFrom(panelType))
            {
                return;
            }

            if (!TryResolvePanelScriptPath(page, panelType, out var scriptPath, out var panelNamespace))
            {
                AddMessage(page.Errors, $"Panel view generation failed: unable to locate script asset for {page.PanelComponentTypeName}.");
                return;
            }

            if (!DeclaresPartialPanelClass(scriptPath, page.PanelComponentTypeName))
            {
                AddMessage(page.Errors, $"Panel view generation failed: {page.PanelComponentTypeName} must be declared as a partial class before bake can emit {page.PanelComponentTypeName}.View.cs.");
                return;
            }

            var fieldSpecs = BuildPanelViewFieldSpecs(exportedNodes, page.Errors);
            if (page.Errors.Count > 0)
            {
                return;
            }

            var viewScriptPath = BuildPanelViewScriptPath(scriptPath, page.PanelComponentTypeName);
            var contents = RenderPanelViewScript(panelNamespace, page.PanelComponentTypeName, fieldSpecs);
            if (!WritePanelViewScriptIfChanged(viewScriptPath, contents))
            {
                return;
            }

            AssetDatabase.ImportAsset(viewScriptPath, ImportAssetOptions.ForceUpdate);
        }

        private static List<AIToUGUIViewFieldSpec> BuildPanelViewFieldSpecs(
            IReadOnlyList<AIToUGUIBakeExportedNodeInfo> exportedNodes,
            List<string> errors)
        {
            var specs = new List<AIToUGUIViewFieldSpec>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            if (exportedNodes == null)
            {
                return specs;
            }

            for (var i = 0; i < exportedNodes.Count; i++)
            {
                var node = exportedNodes[i];
                if (node == null || node.isInternalGenerated || string.IsNullOrWhiteSpace(node.nodeName))
                {
                    continue;
                }

                var fieldName = BuildPanelViewFieldName(node.nodeName);
                if (!fieldNames.Add(fieldName))
                {
                    errors?.Add($"Panel view generation failed: exported nodes produce duplicate field name '{fieldName}'.");
                    continue;
                }

                specs.Add(new AIToUGUIViewFieldSpec
                {
                    NodeName = node.nodeName,
                    FieldName = fieldName,
                    ComponentTypeName = ResolvePreferredViewComponentType(node)
                });
            }

            return specs;
        }

        private static string ResolvePreferredViewComponentType(AIToUGUIBakeExportedNodeInfo node)
        {
            if (node != null &&
                (!string.IsNullOrWhiteSpace(node.slotId) ||
                 !string.IsNullOrWhiteSpace(node.containerId) ||
                 !string.IsNullOrWhiteSpace(node.templateId)))
            {
                return typeof(RectTransform).FullName;
            }

            if (ContainsComponentType(node, typeof(Button).FullName))
            {
                return typeof(Button).FullName;
            }

            if (ContainsComponentType(node, typeof(Toggle).FullName))
            {
                return typeof(Toggle).FullName;
            }

            if (ContainsComponentType(node, typeof(Slider).FullName))
            {
                return typeof(Slider).FullName;
            }

            if (ContainsComponentType(node, typeof(Scrollbar).FullName))
            {
                return typeof(Scrollbar).FullName;
            }

            if (ContainsComponentType(node, typeof(TMP_Dropdown).FullName))
            {
                return typeof(TMP_Dropdown).FullName;
            }

            if (ContainsComponentType(node, typeof(TMP_InputField).FullName))
            {
                return typeof(TMP_InputField).FullName;
            }

            if (ContainsComponentType(node, typeof(ScrollRect).FullName))
            {
                return typeof(ScrollRect).FullName;
            }

            if (ContainsComponentType(node, typeof(TextMeshProUGUI).FullName))
            {
                return typeof(TextMeshProUGUI).FullName;
            }

            if (ContainsComponentType(node, typeof(Image).FullName))
            {
                return typeof(Image).FullName;
            }

            if (ContainsComponentType(node, typeof(RawImage).FullName))
            {
                return typeof(RawImage).FullName;
            }

            return typeof(RectTransform).FullName;
        }

        private static bool ContainsComponentType(AIToUGUIBakeExportedNodeInfo node, string fullTypeName)
        {
            if (node == null || string.IsNullOrWhiteSpace(fullTypeName))
            {
                return false;
            }

            var accessibleTypes = node.accessibleComponentTypes != null && node.accessibleComponentTypes.Count > 0
                ? node.accessibleComponentTypes
                : node.componentTypes;
            if (accessibleTypes == null)
            {
                return false;
            }

            for (var i = 0; i < accessibleTypes.Count; i++)
            {
                if (string.Equals(accessibleTypes[i], fullTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildPanelViewFieldName(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return "_node";
            }

            var parts = ViewFieldNamePartRegex.Matches(nodeName);
            if (parts.Count == 0)
            {
                return "_node";
            }

            var builder = new StringBuilder(32);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i].Value;
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (builder.Length == 0)
                {
                    builder.Append(char.ToLowerInvariant(part[0]));
                    if (part.Length > 1)
                    {
                        builder.Append(part.Substring(1));
                    }
                }
                else
                {
                    builder.Append(char.ToUpperInvariant(part[0]));
                    if (part.Length > 1)
                    {
                        builder.Append(part.Substring(1));
                    }
                }
            }

            return builder.Length == 0 ? "_node" : $"_{builder}";
        }

        private static bool TryFindPanelScriptPath(Type panelType, out string scriptPath)
        {
            scriptPath = null;
            if (panelType == null)
            {
                return false;
            }

            var guids = AssetDatabase.FindAssets($"{panelType.Name} t:MonoScript");
            for (var i = 0; i < guids.Length; i++)
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(candidatePath);
                if (script != null && script.GetClass() == panelType)
                {
                    scriptPath = candidatePath.Replace("\\", "/");
                    return true;
                }
            }

            return false;
        }

        private static bool DeclaresPartialPanelClass(string scriptPath, string typeName)
        {
            if (string.IsNullOrWhiteSpace(scriptPath) ||
                string.IsNullOrWhiteSpace(typeName) ||
                !File.Exists(scriptPath))
            {
                return false;
            }

            var source = File.ReadAllText(scriptPath);
            var regex = new Regex($"\\bpartial\\s+class\\s+{Regex.Escape(typeName)}\\b", RegexOptions.Compiled);
            return regex.IsMatch(source);
        }

        private static string BuildPanelViewScriptPath(string scriptPath, string typeName)
        {
            var folder = Path.GetDirectoryName(scriptPath)?.Replace("\\", "/") ?? "Assets";
            return $"{folder}/{typeName}.View.cs";
        }

        private static bool TryResolvePanelScriptPath(
            AIToUGUICompiledPage page,
            Type panelType,
            out string scriptPath,
            out string panelNamespace)
        {
            scriptPath = null;
            panelNamespace = panelType != null ? panelType.Namespace : string.Empty;

            if (panelType != null && TryFindPanelScriptPath(panelType, out scriptPath))
            {
                return true;
            }

            if (panelType != null)
            {
                return false;
            }
            return false;
        }

        private static string RenderPanelViewScript(
            string panelNamespace,
            string panelTypeName,
            IReadOnlyList<AIToUGUIViewFieldSpec> fieldSpecs)
        {
            var indent = string.IsNullOrWhiteSpace(panelNamespace) ? string.Empty : "    ";
            var builder = new StringBuilder(512);
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(panelNamespace))
            {
                builder.Append("namespace ").Append(panelNamespace).AppendLine();
                builder.AppendLine("{");
            }

            builder.Append(indent).Append("public partial class ").Append(panelTypeName).AppendLine();
            builder.Append(indent).AppendLine("{");
            for (var i = 0; i < fieldSpecs.Count; i++)
            {
                var spec = fieldSpecs[i];
                builder.Append(indent)
                    .Append("    [UnityEngine.SerializeField] private ")
                    .Append(spec.ComponentTypeName)
                    .Append(' ')
                    .Append(spec.FieldName)
                    .AppendLine(";");
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("    protected override void BindViewComponents(AIToUGUINodeMap nodeMap)");
            builder.Append(indent).AppendLine("    {");
            for (var i = 0; i < fieldSpecs.Count; i++)
            {
                var spec = fieldSpecs[i];
                builder.Append(indent)
                    .Append("        ")
                    .Append(spec.FieldName)
                    .Append(" = nodeMap.Get<")
                    .Append(spec.ComponentTypeName)
                    .Append(">(\"")
                    .Append(spec.NodeName.Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .AppendLine("\");");
            }

            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");

            if (!string.IsNullOrWhiteSpace(panelNamespace))
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static bool WritePanelViewScriptIfChanged(string scriptPath, string contents)
        {
            var normalizedPath = scriptPath.Replace("\\", "/");
            var legacyPath = normalizedPath.EndsWith(".View.cs", StringComparison.Ordinal)
                ? normalizedPath.Substring(0, normalizedPath.Length - ".View.cs".Length) + ".View.g.cs"
                : null;

            if (!string.IsNullOrWhiteSpace(legacyPath) && File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
                var legacyMetaPath = legacyPath + ".meta";
                if (File.Exists(legacyMetaPath))
                {
                    File.Delete(legacyMetaPath);
                }
            }

            if (File.Exists(normalizedPath))
            {
                var existing = File.ReadAllText(normalizedPath);
                if (string.Equals(existing, contents, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            File.WriteAllText(normalizedPath, contents, Encoding.UTF8);
            return true;
        }

        private static GameObject FindPreviewRoot(AIToUGUIPreviewMount mount, string siteId, string pageId)
        {
            if (mount == null)
            {
                return null;
            }

            var previewInstances = mount.GetComponentsInChildren<AIToUGUIPreviewInstance>(true);
            for (var i = 0; i < previewInstances.Length; i++)
            {
                var instance = previewInstances[i];
                if (instance == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(instance.pageId) &&
                    string.Equals(instance.pageId, pageId, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(siteId) || string.Equals(instance.siteId, siteId, StringComparison.Ordinal)))
                {
                    return instance.gameObject;
                }

                if (instance.TryGetComponent<AIToUGUIPageRoot>(out var pageRoot) &&
                    string.Equals(pageRoot.pageId, pageId, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(siteId) || string.Equals(pageRoot.siteId, siteId, StringComparison.Ordinal)))
                {
                    return instance.gameObject;
                }
            }

            var pageRoots = mount.GetComponentsInChildren<AIToUGUIPageRoot>(true);
            for (var i = 0; i < pageRoots.Length; i++)
            {
                var pageRoot = pageRoots[i];
                if (pageRoot == null)
                {
                    continue;
                }

                if (string.Equals(pageRoot.pageId, pageId, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(siteId) || string.Equals(pageRoot.siteId, siteId, StringComparison.Ordinal)))
                {
                    return pageRoot.gameObject;
                }
            }

            return null;
        }

        private static List<AIToUGUIBakeExportedNodeInfo> CollectExportedNodes(AIToUGUIBuiltNode builtRoot)
        {
            var exportedNodes = new List<AIToUGUIBakeExportedNodeInfo>();
            if (builtRoot == null)
            {
                return exportedNodes;
            }

            var visitedInternal = new HashSet<Transform>();
            CollectExportedNodesRecursive(builtRoot, exportedNodes, visitedInternal);
            return exportedNodes;
        }

        private static List<AIToUGUIBakeExportedNodeInfo> CollectExportedNodesFromHierarchy(GameObject root)
        {
            var exportedNodes = new List<AIToUGUIBakeExportedNodeInfo>();
            if (root == null)
            {
                return exportedNodes;
            }

            var markers = root.GetComponentsInChildren<AIToUGUIExportNodeMarker>(true);
            if (markers != null && markers.Length > 0)
            {
                for (var i = 0; i < markers.Length; i++)
                {
                    var marker = markers[i];
                    if (marker == null)
                    {
                        continue;
                    }

                    exportedNodes.Add(new AIToUGUIBakeExportedNodeInfo
                    {
                        nodeName = string.IsNullOrWhiteSpace(marker.nodeName) ? marker.transform.name : marker.nodeName,
                        controlType = marker.controlType == AIToUGUIControlType.Auto
                            ? ResolveHierarchyControlType(marker.transform)
                            : marker.controlType,
                        role = marker.role ?? string.Empty,
                        elementId = marker.elementId ?? string.Empty,
                        variantId = marker.variantId ?? string.Empty,
                        shapeId = marker.shapeId ?? string.Empty,
                        frameId = marker.frameId ?? string.Empty,
                        slotId = marker.slotId ?? string.Empty,
                        containerId = marker.containerId ?? string.Empty,
                        templateId = marker.templateId ?? string.Empty,
                        componentFamily = marker.componentFamily ?? string.Empty,
                        componentVariant = marker.componentVariant ?? string.Empty,
                        renderStrategy = marker.renderStrategy ?? AIToUGUIElementContractUtility.ProceduralRenderStrategyId,
                        isPrefabBacked = marker.isPrefabBacked,
                        isInternalGenerated = false,
                        assetRefs = ExtractAssetRefs(marker.transform),
                        fidelityNotes = ExtractFidelityNotes(marker.transform),
                        componentTypes = CollectDirectComponentTypes(marker.transform),
                        accessibleComponentTypes = CollectBindingComponentTypesFromHierarchy(marker.transform)
                    });
                }

                return exportedNodes;
            }

            var stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                exportedNodes.Add(new AIToUGUIBakeExportedNodeInfo
                {
                    nodeName = current.name,
                    controlType = ResolveHierarchyControlType(current),
                    role = string.Empty,
                    elementId = string.Empty,
                    variantId = string.Empty,
                    shapeId = string.Empty,
                    frameId = string.Empty,
                    slotId = string.Empty,
                    containerId = string.Empty,
                    templateId = string.Empty,
                    componentFamily = string.Empty,
                    componentVariant = string.Empty,
                    renderStrategy = AIToUGUIElementContractUtility.ProceduralRenderStrategyId,
                    isPrefabBacked = false,
                    isInternalGenerated = current.name.StartsWith(InternalNodePrefix, StringComparison.Ordinal),
                    assetRefs = ExtractAssetRefs(current),
                    fidelityNotes = ExtractFidelityNotes(current),
                    componentTypes = CollectDirectComponentTypes(current),
                    accessibleComponentTypes = CollectBindingComponentTypesFromHierarchy(current)
                });

                for (var i = current.childCount - 1; i >= 0; i--)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            return exportedNodes;
        }

        private static bool HasExportMarkers(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            var markers = root.GetComponentsInChildren<AIToUGUIExportNodeMarker>(true);
            return markers != null && markers.Length > 0;
        }

        private static void CollectExportedNodesRecursive(
            AIToUGUIBuiltNode builtNode,
            List<AIToUGUIBakeExportedNodeInfo> exportedNodes,
            HashSet<Transform> visitedInternal)
        {
            if (builtNode == null || builtNode.Node == null || builtNode.GameObject == null)
            {
                return;
            }

            exportedNodes.Add(new AIToUGUIBakeExportedNodeInfo
            {
                nodeName = builtNode.Node.Name,
                controlType = builtNode.Node.ControlType,
                role = builtNode.Node.Role,
                elementId = builtNode.Node.ElementId,
                variantId = builtNode.Node.VariantId,
                shapeId = builtNode.Node.ShapeId,
                frameId = builtNode.Node.FrameId,
                slotId = builtNode.Node.SlotId,
                containerId = builtNode.Node.ContainerId,
                templateId = builtNode.Node.TemplateId,
                componentFamily = builtNode.Node.ComponentFamily,
                componentVariant = builtNode.Node.ComponentVariant,
                renderStrategy = builtNode.Node.RenderStrategy ?? AIToUGUIElementContractUtility.ProceduralRenderStrategyId,
                isPrefabBacked = builtNode.IsPrefabBacked,
                isInternalGenerated = false,
                assetRefs = CloneAssetRefs(builtNode.Node.AssetRefs),
                fidelityNotes = new List<string>(builtNode.Node.FidelityNotes),
                componentTypes = CollectDirectComponentTypes(builtNode.GameObject.transform),
                accessibleComponentTypes = CollectBindingComponentTypes(builtNode)
            });

            CollectInternalGeneratedNodes(builtNode.GameObject.transform, exportedNodes, visitedInternal);

            for (var i = 0; i < builtNode.Children.Count; i++)
            {
                CollectExportedNodesRecursive(builtNode.Children[i], exportedNodes, visitedInternal);
            }
        }

        private static void CollectInternalGeneratedNodes(
            Transform root,
            List<AIToUGUIBakeExportedNodeInfo> exportedNodes,
            HashSet<Transform> visitedInternal)
        {
            if (root == null)
            {
                return;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (child.name.StartsWith(InternalNodePrefix, StringComparison.Ordinal) && visitedInternal.Add(child))
                {
                    exportedNodes.Add(new AIToUGUIBakeExportedNodeInfo
                    {
                        nodeName = child.name,
                        controlType = AIToUGUIControlType.Div,
                        role = string.Empty,
                        elementId = string.Empty,
                        variantId = string.Empty,
                        shapeId = string.Empty,
                        frameId = string.Empty,
                        slotId = string.Empty,
                        containerId = string.Empty,
                        templateId = string.Empty,
                        componentFamily = string.Empty,
                        componentVariant = string.Empty,
                        renderStrategy = AIToUGUIElementContractUtility.ProceduralRenderStrategyId,
                        isPrefabBacked = false,
                        isInternalGenerated = true,
                        assetRefs = ExtractAssetRefs(child),
                        fidelityNotes = ExtractFidelityNotes(child),
                        componentTypes = CollectDirectComponentTypes(child),
                        accessibleComponentTypes = CollectDirectComponentTypes(child)
                    });
                }

                CollectInternalGeneratedNodes(child, exportedNodes, visitedInternal);
            }
        }

        private static List<string> CollectBindingComponentTypes(AIToUGUIBuiltNode builtNode)
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            if (builtNode == null || builtNode.GameObject == null)
            {
                return types.ToList();
            }

            var blockedRoots = new HashSet<Transform>(builtNode.Children.Where(child => child?.GameObject != null).Select(child => child.GameObject.transform));
            CollectBindingComponentTypesRecursive(builtNode.GameObject.transform, blockedRoots, types, true);
            return types.OrderBy(value => value, StringComparer.Ordinal).ToList();
        }

        private static List<string> CollectBindingComponentTypesFromHierarchy(Transform root)
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            if (root == null)
            {
                return types.ToList();
            }

            var blockedRoots = new HashSet<Transform>();
            var markers = root.GetComponentsInChildren<AIToUGUIExportNodeMarker>(true);
            if (markers != null)
            {
                for (var i = 0; i < markers.Length; i++)
                {
                    var marker = markers[i];
                    if (marker != null && marker.transform != root)
                    {
                        blockedRoots.Add(marker.transform);
                    }
                }
            }

            CollectBindingComponentTypesRecursive(root, blockedRoots, types, true);
            return types.OrderBy(value => value, StringComparer.Ordinal).ToList();
        }

        private static void CollectBindingComponentTypesRecursive(
            Transform root,
            HashSet<Transform> blockedRoots,
            HashSet<string> types,
            bool includeChildren)
        {
            if (root == null || types == null)
            {
                return;
            }

            var components = root.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform)
                {
                    continue;
                }

                types.Add(component.GetType().FullName);
            }

            if (!includeChildren)
            {
                return;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null || blockedRoots.Contains(child) || !child.name.StartsWith(InternalNodePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                CollectBindingComponentTypesRecursive(child, blockedRoots, types, true);
            }
        }

        private static List<string> CollectDirectComponentTypes(Transform root)
        {
            var types = new List<string>();
            if (root == null)
            {
                return types;
            }

            var components = root.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform)
                {
                    continue;
                }

                types.Add(component.GetType().FullName);
            }

            types.Sort(StringComparer.Ordinal);
            return types;
        }

        private static List<AIToUGUIAssetReference> CloneAssetRefs(IReadOnlyList<AIToUGUIAssetReference> assetRefs)
        {
            var cloned = new List<AIToUGUIAssetReference>();
            if (assetRefs == null)
            {
                return cloned;
            }

            for (var i = 0; i < assetRefs.Count; i++)
            {
                var assetRef = assetRefs[i];
                if (assetRef == null)
                {
                    continue;
                }

                cloned.Add(new AIToUGUIAssetReference
                {
                    assetId = assetRef.assetId ?? string.Empty,
                    assetType = assetRef.assetType,
                    usage = assetRef.usage ?? string.Empty,
                    importMode = assetRef.importMode,
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

            return cloned;
        }

        private static List<AIToUGUIAssetReference> ExtractAssetRefs(Transform root)
        {
            if (root == null || !root.TryGetComponent<AIToUGUIAssetBindingManifest>(out var manifest))
            {
                return new List<AIToUGUIAssetReference>();
            }

            var assetRefs = new List<AIToUGUIAssetReference>();
            if (manifest.assetRefs == null)
            {
                return assetRefs;
            }

            for (var i = 0; i < manifest.assetRefs.Count; i++)
            {
                var assetRef = manifest.assetRefs[i];
                if (assetRef == null || string.IsNullOrWhiteSpace(assetRef.assetId))
                {
                    continue;
                }

                assetRefs.Add(new AIToUGUIAssetReference
                {
                    assetId = assetRef.assetId ?? string.Empty,
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

            return assetRefs;
        }

        private static List<string> ExtractFidelityNotes(Transform root)
        {
            if (root == null || !root.TryGetComponent<AIToUGUIAssetBindingManifest>(out var manifest) || manifest.fidelityNotes == null)
            {
                return new List<string>();
            }

            return new List<string>(manifest.fidelityNotes.Where(note => !string.IsNullOrWhiteSpace(note)));
        }

        private static AIToUGUIControlType ResolveHierarchyControlType(Transform root)
        {
            if (root == null)
            {
                return AIToUGUIControlType.Div;
            }

            if (root.GetComponent<Button>() != null)
            {
                return AIToUGUIControlType.Button;
            }

            if (root.GetComponent<Toggle>() != null)
            {
                return AIToUGUIControlType.Toggle;
            }

            if (root.GetComponent<Slider>() != null)
            {
                return AIToUGUIControlType.Slider;
            }

            if (root.GetComponent<Scrollbar>() != null)
            {
                return AIToUGUIControlType.Scroll;
            }

            if (root.GetComponent<TMP_Dropdown>() != null)
            {
                return AIToUGUIControlType.Dropdown;
            }

            if (root.GetComponent<TMP_InputField>() != null)
            {
                return AIToUGUIControlType.Input;
            }

            if (root.GetComponent<TextMeshProUGUI>() != null)
            {
                return AIToUGUIControlType.Text;
            }

            if (root.GetComponent<Image>() != null || root.GetComponent<RawImage>() != null)
            {
                return AIToUGUIControlType.Image;
            }

            return AIToUGUIControlType.Div;
        }

        private static void UpsertRuntimeRegistry(AIToUGUICompiledPage page, string metadataPath)
        {
            var registry = LoadOrCreateRuntimeRegistry();
            if (registry == null)
            {
                Debug.LogWarning("[AIToUGUI] Runtime registry asset is missing, skipped runtime page registration.");
                return;
            }

            if (registry.pages == null)
            {
                registry.pages = new List<AIToUGUIRuntimePageEntry>();
            }

            var entry = registry.FindPage(page.RuntimePageId);
            if (entry == null)
            {
                entry = new AIToUGUIRuntimePageEntry();
                registry.pages.Add(entry);
            }

            entry.runtimePageId = page.RuntimePageId;
            entry.siteId = page.SiteId;
            entry.pageId = page.PageId;
            entry.prefabLogicalPath = page.LogicalPath;
            entry.prefabName = page.PrefabName;
            entry.targetLayer = page.TargetLayer;
            entry.panelComponentTypeName = page.PanelComponentTypeName;
            entry.metadataAssetPath = metadataPath;
            entry.metadataAsset = AssetDatabase.LoadAssetAtPath<AIToUGUIBakeMetadata>(metadataPath);

            EditorUtility.SetDirty(registry);
        }

        private static void CleanupExistingPrefabMissingScripts(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath) || !File.Exists(prefabPath))
            {
                return;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                return;
            }

            try
            {
                var removedTransient = RemoveTransientGeneratedObjectsRecursive(prefabRoot) > 0;
                var removedMissingScripts = RemoveMissingScriptsRecursive(prefabRoot);
                if (!removedTransient && !removedMissingScripts)
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static bool RemoveMissingScriptsRecursive(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            var removedAny = false;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var gameObject = transforms[i].gameObject;
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) <= 0)
                {
                    continue;
                }

                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                removedAny = true;
            }

            return removedAny;
        }

        private static bool PreparePrefabRootForSave(GameObject root, AIToUGUICompiledPage page)
        {
            if (root == null)
            {
                return false;
            }

            RemoveTransientGeneratedObjectsRecursive(root);
            ApplyOptionalExportPruning(root, page);
            RemoveMissingScriptsRecursive(root);

            var missingScriptCount = CountMissingScriptsRecursive(root);
            if (missingScriptCount > 0)
            {
                AddMessage(page?.Errors, $"导出前仍检测到 {missingScriptCount} 个缺失脚本，已中止保存。请重新预览后再导出。");
                return false;
            }

            RefreshSemanticLayoutNow(root);
            return true;
        }

        private static void ApplyOptionalExportPruning(GameObject root, AIToUGUICompiledPage page)
        {
            if (root == null)
            {
                return;
            }

            if (page == null || !page.UseOverflowMaskHosts)
            {
                RemoveInternalContentMaskHostsRecursive(root.transform);
            }

            if (page != null && !page.KeepAssetBindingManifests)
            {
                RemoveComponentsRecursive<AIToUGUIAssetBindingManifest>(root.transform);
            }

            if (page != null && !page.KeepExportNodeMarkers)
            {
                RemoveComponentsRecursive<AIToUGUIExportNodeMarker>(root.transform);
            }
        }

        private static void RemoveComponentsRecursive<T>(Transform root) where T : Component
        {
            if (root == null)
            {
                return;
            }

            var components = root.GetComponentsInChildren<T>(true);
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null)
                {
                    AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
                }
            }
        }

        private static void RemoveInternalContentMaskHostsRecursive(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var hosts = new List<Transform>();
            CollectInternalContentMaskHosts(root, hosts);
            for (var i = 0; i < hosts.Count; i++)
            {
                RemoveInternalContentMaskHost(hosts[i]);
            }
        }

        private static void CollectInternalContentMaskHosts(Transform root, List<Transform> results)
        {
            if (root == null || results == null)
            {
                return;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                CollectInternalContentMaskHosts(child, results);
                if (string.Equals(child.name, InternalContentMaskName, StringComparison.Ordinal))
                {
                    results.Add(child);
                }
            }
        }

        private static void RemoveInternalContentMaskHost(Transform host)
        {
            if (host == null)
            {
                return;
            }

            var parent = host.parent;
            if (parent == null)
            {
                return;
            }

            var siblingIndex = host.GetSiblingIndex();
            while (host.childCount > 0)
            {
                var child = host.GetChild(0);
                child.SetParent(parent, false);
                child.SetSiblingIndex(siblingIndex++);
            }

            AIToUGUIEditorUiMutationGuard.DestroyGameObjectSafely(host.gameObject);
        }

        private static int RemoveTransientGeneratedObjectsRecursive(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var toDestroy = new List<GameObject>();
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var current = transforms[i];
                if (current == null || current.gameObject == root)
                {
                    continue;
                }

                if (IsTransientGeneratedObject(current.gameObject))
                {
                    toDestroy.Add(current.gameObject);
                }
            }

            for (var i = 0; i < toDestroy.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(toDestroy[i]);
            }

            return toDestroy.Count;
        }

        private static bool IsTransientGeneratedObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            const HideFlags transientFlags =
                HideFlags.HideInHierarchy |
                HideFlags.DontSaveInEditor |
                HideFlags.NotEditable |
                HideFlags.DontSaveInBuild |
                HideFlags.DontUnloadUnusedAsset |
                HideFlags.HideAndDontSave;

            if ((gameObject.hideFlags & transientFlags) != 0)
            {
                return true;
            }

            var components = gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (component is Transform)
                {
                    continue;
                }

                if ((component.hideFlags & transientFlags) != 0)
                {
                    return true;
                }

                var fullName = component.GetType().FullName;
                if (string.Equals(fullName, "LeTai.TrueShadow.ShadowRenderer", StringComparison.Ordinal) ||
                    string.Equals(fullName, "LeTai.TrueShadow.ShadowSorter", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountMissingScriptsRecursive(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            var total = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                total += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[i].gameObject);
            }

            return total;
        }

        private static AIToUGUIRuntimeRegistry LoadOrCreateRuntimeRegistry()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Config");

            var registry = AssetDatabase.LoadAssetAtPath<AIToUGUIRuntimeRegistry>(RuntimeRegistryAssetPath);
            if (registry != null)
            {
                return registry;
            }

            registry = ScriptableObject.CreateInstance<AIToUGUIRuntimeRegistry>();
            AssetDatabase.CreateAsset(registry, RuntimeRegistryAssetPath);
            return registry;
        }

        private static void AddMessage(List<string> messages, string message)
        {
            if (messages == null || string.IsNullOrWhiteSpace(message) || messages.Contains(message))
            {
                return;
            }

            messages.Add(message);
        }

        private static void DeduplicateMessages(List<string> messages)
        {
            if (messages == null || messages.Count <= 1)
            {
                return;
            }

            var ordered = messages
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct()
                .ToList();
            messages.Clear();
            messages.AddRange(ordered);
        }

        private static void UpsertPrefabToResourceConfig(string prefabPath, string logicalPath)
        {
            var config = AssetDatabase.LoadAssetAtPath<ResourceConfig>(ResourceConfigAssetPath);
            if (config == null)
            {
                Debug.LogWarning("[AIToUGUI] ResourceConfig.asset 不存在，已跳过资源注册。");
                return;
            }

            if (!config.UpsertAssetByEditorPath(prefabPath, logicalPath, AssetLoadMode.EditorAsset))
            {
                Debug.LogWarning($"[AIToUGUI] 资源注册失败: {prefabPath}");
                return;
            }

            EditorUtility.SetDirty(config);
        }

        private static Vector2 ResolveNodeSize(AIToUGUICompiledNode node, Vector2 parentSize, Vector2 defaultSize)
        {
            var width = ParseLength(GetStyle(node, "width"));
            var height = ParseLength(GetStyle(node, "height"));
            var minWidth = ParseLength(GetStyle(node, "min-width"));
            var minHeight = ParseLength(GetStyle(node, "min-height"));
            var maxWidth = ParseLength(GetStyle(node, "max-width"));
            var maxHeight = ParseLength(GetStyle(node, "max-height"));

            var resolvedWidth = ResolveLength(width, parentSize.x, defaultSize.x);
            var resolvedHeight = ResolveLength(height, parentSize.y, defaultSize.y);

            if (!width.IsValid && maxWidth.IsValid)
            {
                resolvedWidth = Mathf.Min(parentSize.x, ResolveLength(maxWidth, parentSize.x, parentSize.x));
            }

            if (!height.IsValid && maxHeight.IsValid)
            {
                resolvedHeight = Mathf.Min(parentSize.y, ResolveLength(maxHeight, parentSize.y, parentSize.y));
            }

            resolvedWidth = ClampLength(resolvedWidth, minWidth, maxWidth, parentSize.x);
            resolvedHeight = ClampLength(resolvedHeight, minHeight, maxHeight, parentSize.y);
            return new Vector2(resolvedWidth, resolvedHeight);
        }

        private static float ClampLength(float value, AIToUGUICssLength min, AIToUGUICssLength max, float parentLength)
        {
            if (value < 0f && !min.IsValid)
            {
                return value;
            }

            if (min.IsValid)
            {
                value = Mathf.Max(value, ResolveLength(min, parentLength, value));
            }

            if (max.IsValid)
            {
                value = Mathf.Min(value, ResolveLength(max, parentLength, value));
            }

            return value;
        }

        private static AIToUGUICssLength ParseLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new AIToUGUICssLength(0f, AIToUGUILengthUnit.None);
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized == "auto")
            {
                return new AIToUGUICssLength(0f, AIToUGUILengthUnit.Auto);
            }

            if (normalized.EndsWith("%", StringComparison.Ordinal) &&
                float.TryParse(normalized.Substring(0, normalized.Length - 1), out var percent))
            {
                return new AIToUGUICssLength(percent, AIToUGUILengthUnit.Percent);
            }

            var number = ParseFloat(normalized, float.NaN);
            if (float.IsNaN(number))
            {
                return new AIToUGUICssLength(0f, AIToUGUILengthUnit.None);
            }

            return new AIToUGUICssLength(number, AIToUGUILengthUnit.Pixel);
        }

        private static float ResolveLength(AIToUGUICssLength length, float parentLength, float fallback)
        {
            return length.Unit switch
            {
                AIToUGUILengthUnit.Pixel => length.Value,
                AIToUGUILengthUnit.Percent => parentLength * length.Value * 0.01f,
                AIToUGUILengthUnit.Auto => fallback,
                _ => fallback
            };
        }

        private static float ParseFloat(string value, float fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var match = NumberRegex.Match(value);
            if (!match.Success || !float.TryParse(match.Value, out var parsed))
            {
                return fallback;
            }

            return parsed;
        }

        private static RectOffset ParseBox(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new RectOffset();
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return new RectOffset();
            }

            var values = parts.Select(part => Mathf.RoundToInt(ParseFloat(part, 0f))).ToArray();
            return values.Length switch
            {
                1 => new RectOffset(values[0], values[0], values[0], values[0]),
                2 => new RectOffset(values[1], values[1], values[0], values[0]),
                3 => new RectOffset(values[1], values[1], values[0], values[2]),
                _ => new RectOffset(values[3], values[1], values[0], values[2])
            };
        }

        private static TextAnchor ResolveChildAlignment(string direction, string justify, string align)
        {
            var horizontal = direction == "row" ? justify : align;
            var vertical = direction == "row" ? align : justify;
            return ResolveAnchor(horizontal, vertical);
        }

        private static TextAnchor ResolveAnchor(string horizontal, string vertical)
        {
            var h = horizontal switch
            {
                "center" => 1,
                "flex-end" => 2,
                "end" => 2,
                _ => 0
            };

            var v = vertical switch
            {
                "center" => 1,
                "flex-end" => 2,
                "end" => 2,
                _ => 0
            };

            return (v, h) switch
            {
                (0, 0) => TextAnchor.UpperLeft,
                (0, 1) => TextAnchor.UpperCenter,
                (0, 2) => TextAnchor.UpperRight,
                (1, 0) => TextAnchor.MiddleLeft,
                (1, 1) => TextAnchor.MiddleCenter,
                (1, 2) => TextAnchor.MiddleRight,
                (2, 0) => TextAnchor.LowerLeft,
                (2, 1) => TextAnchor.LowerCenter,
                (2, 2) => TextAnchor.LowerRight,
                _ => TextAnchor.UpperLeft
            };
        }

        private static string GetStyle(AIToUGUICompiledNode node, string key)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return node.Styles.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static string GetAttribute(AIToUGUICompiledNode node, string key)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return node.Attributes.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
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

        private static string GetShapeId(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(node.ShapeId))
            {
                return node.ShapeId.Trim().ToLowerInvariant();
            }

            return GetAttribute(node, "data-ui-shape").Trim().ToLowerInvariant();
        }

        private static string GetFrameId(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(node.FrameId))
            {
                return node.FrameId.Trim().ToLowerInvariant();
            }

            return GetAttribute(node, "data-ui-frame").Trim().ToLowerInvariant();
        }

        private static float ResolveFlexible(string flex, string flexGrow, AIToUGUICssLength widthOrHeight)
        {
            if (!string.IsNullOrWhiteSpace(flexGrow))
            {
                return Mathf.Max(0f, ParseFloat(flexGrow, 0f));
            }

            if (!string.IsNullOrWhiteSpace(flex))
            {
                var parts = flex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    return Mathf.Max(0f, ParseFloat(parts[0], 0f));
                }
            }

            return 0f;
        }

        private static bool NeedsShape(AIToUGUICompiledNode node, bool isRoot, AIToUGUIThemeDefinition theme)
        {
            if (node == null)
            {
                return false;
            }

            if (HasClass(node, "route-line"))
            {
                return true;
            }

            if (isRoot)
            {
                return theme != null ||
                       !string.IsNullOrWhiteSpace(GetStyle(node, "background")) ||
                       !string.IsNullOrWhiteSpace(GetStyle(node, "background-color")) ||
                       !string.IsNullOrWhiteSpace(GetShapeId(node)) ||
                       !string.IsNullOrWhiteSpace(GetFrameId(node));
            }

            if (node.ControlType == AIToUGUIControlType.Button ||
                node.ControlType == AIToUGUIControlType.Input ||
                node.ControlType == AIToUGUIControlType.Toggle ||
                node.ControlType == AIToUGUIControlType.Slider ||
                node.ControlType == AIToUGUIControlType.Dropdown)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(GetStyle(node, "background")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "background-color")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "border")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "box-shadow")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-preset")) ||
                   !string.IsNullOrWhiteSpace(GetShapeId(node)) ||
                   !string.IsNullOrWhiteSpace(GetFrameId(node)) ||
                   ResolveShapeRenderBackend(node, out _, out _, out _) == AIToUGUIShapeRenderBackend.WindinatorLite;
        }

        private static AIToUGUIShapeRenderBackend ResolveShapeRenderBackend(
            AIToUGUICompiledNode node,
            out AIToUGUIWindinatorShapeKind windinatorKind,
            out Vector4 cornerRadii,
            out float shapeAmount)
        {
            windinatorKind = ResolveWindinatorShapeKind(node, out cornerRadii, out shapeAmount);
            return windinatorKind == AIToUGUIWindinatorShapeKind.None
                ? AIToUGUIShapeRenderBackend.ProceduralUi
                : AIToUGUIShapeRenderBackend.WindinatorLite;
        }

        private static bool RequestsWindinatorPrimitiveOverride(AIToUGUICompiledNode node)
        {
            return ResolveWindinatorShapeKind(node, out _, out _) != AIToUGUIWindinatorShapeKind.None;
        }

        private static AIToUGUIWindinatorShapeKind ResolveWindinatorShapeKind(
            AIToUGUICompiledNode node,
            out Vector4 cornerRadii,
            out float shapeAmount)
        {
            cornerRadii = Vector4.zero;
            shapeAmount = 0f;
            if (node == null)
            {
                return AIToUGUIWindinatorShapeKind.None;
            }

            var explicitShapeId = GetShapeId(node);
            if (!string.IsNullOrWhiteSpace(explicitShapeId))
            {
                TryExtractCornerRadii(node, out cornerRadii);
                shapeAmount = ResolveShapeAmount(node, cornerRadii);
                if (ShouldPreferCornerRadiiShape(explicitShapeId, cornerRadii))
                {
                    return AIToUGUIWindinatorShapeKind.PerCornerRoundedRect;
                }

                switch (explicitShapeId)
                {
                    case "per-corner":
                        return AIToUGUIWindinatorShapeKind.PerCornerRoundedRect;
                    case "cut-corner":
                        return AIToUGUIWindinatorShapeKind.CutCorner;
                    case "plate":
                        return AIToUGUIWindinatorShapeKind.Plate;
                    case "banner":
                        return AIToUGUIWindinatorShapeKind.Banner;
                    default:
                        return AIToUGUIWindinatorShapeKind.None;
                }
            }

            if (TryExtractCornerRadii(node, out cornerRadii) &&
                (HasShapeSemantic(node, "per-corner") || HasDistinctCornerRadii(cornerRadii)))
            {
                shapeAmount = ResolveShapeAmount(node, cornerRadii);
                return AIToUGUIWindinatorShapeKind.PerCornerRoundedRect;
            }

            return AIToUGUIWindinatorShapeKind.None;
        }

        private static bool ShouldPreferCornerRadiiShape(string explicitShapeId, Vector4 cornerRadii)
        {
            if (string.IsNullOrWhiteSpace(explicitShapeId) || cornerRadii == Vector4.zero)
            {
                return false;
            }

            switch (explicitShapeId.Trim().ToLowerInvariant())
            {
                case "plate":
                case "banner":
                case "cut-corner":
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasShapeSemantic(AIToUGUICompiledNode node, string token)
        {
            if (node == null || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var normalizedToken = token.Trim().ToLowerInvariant();
            var explicitShapeId = GetShapeId(node);
            if (!string.IsNullOrWhiteSpace(explicitShapeId))
            {
                return string.Equals(explicitShapeId, normalizedToken, StringComparison.OrdinalIgnoreCase);
            }

            for (var i = 0; i < node.Classes.Count; i++)
            {
                var className = node.Classes[i];
                if (string.IsNullOrWhiteSpace(className))
                {
                    continue;
                }

                var normalizedClass = className.Trim().ToLowerInvariant();
                if (normalizedClass == normalizedToken ||
                    normalizedClass == $"ai-shape-{normalizedToken}" ||
                    normalizedClass == $"shape-{normalizedToken}" ||
                    normalizedClass.EndsWith($"/{normalizedToken}", StringComparison.Ordinal) ||
                    normalizedClass.Contains($"-{normalizedToken}"))
                {
                    return true;
                }
            }

            return !string.IsNullOrWhiteSpace(node.Role) &&
                   node.Role.IndexOf(normalizedToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryExtractCornerRadii(AIToUGUICompiledNode node, out Vector4 cornerRadii)
        {
            cornerRadii = Vector4.zero;
            if (node == null)
            {
                return false;
            }

            var borderRadius = GetStyle(node, "border-radius");
            if (string.IsNullOrWhiteSpace(borderRadius))
            {
                return false;
            }

            var normalized = borderRadius.Replace("/", " ");
            var matches = NumberRegex.Matches(normalized);
            if (matches.Count == 0)
            {
                return false;
            }

            var values = new List<float>(matches.Count);
            for (var i = 0; i < matches.Count; i++)
            {
                if (float.TryParse(matches[i].Value, out var parsed))
                {
                    values.Add(Mathf.Max(0f, parsed));
                }
            }

            if (values.Count == 0)
            {
                return false;
            }

            switch (values.Count)
            {
                case 1:
                    cornerRadii = new Vector4(values[0], values[0], values[0], values[0]);
                    break;
                case 2:
                    cornerRadii = new Vector4(values[0], values[1], values[0], values[1]);
                    break;
                case 3:
                    cornerRadii = new Vector4(values[0], values[1], values[2], values[1]);
                    break;
                default:
                    cornerRadii = new Vector4(values[0], values[1], values[2], values[3]);
                    break;
            }

            return true;
        }

        private static bool HasDistinctCornerRadii(Vector4 cornerRadii)
        {
            return !Mathf.Approximately(cornerRadii.x, cornerRadii.y) ||
                   !Mathf.Approximately(cornerRadii.x, cornerRadii.z) ||
                   !Mathf.Approximately(cornerRadii.x, cornerRadii.w);
        }

        private static float ResolveShapeAmount(AIToUGUICompiledNode node, Vector4 cornerRadii)
        {
            var explicitAmount = ParseFloat(GetStyle(node, "-ai-shape-amount"), float.NaN);
            if (!float.IsNaN(explicitAmount))
            {
                return Mathf.Max(0f, explicitAmount);
            }

            if (cornerRadii != Vector4.zero)
            {
                return Mathf.Max(Mathf.Max(cornerRadii.x, cornerRadii.y), Mathf.Max(cornerRadii.z, cornerRadii.w));
            }

            return Mathf.Max(0f, ParseFloat(GetStyle(node, "border-radius"), 0f));
        }

        private static bool ResolveUseMaxRoundness(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset)
        {
            var shapeId = GetShapeId(node);
            if (!string.IsNullOrWhiteSpace(shapeId))
            {
                return false;
            }

            if (HasExplicitVisualAuthoring(node))
            {
                return false;
            }

            return preset != null && preset.useMaxRoundness;
        }

        private static AIToUGUIVisualPreset ResolveVisualPreset(AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (theme == null)
            {
                return null;
            }

            var presetId = GetStyle(node, "-ai-preset");
            if (!string.IsNullOrWhiteSpace(presetId))
            {
                return theme.ResolveVisualPreset(presetId);
            }

            if (IsTransparentOverlayButton(node))
            {
                return null;
            }

            if (node.ControlType == AIToUGUIControlType.Button)
            {
                return theme.ResolveVisualPreset("button/default");
            }

            if (!string.IsNullOrWhiteSpace(node.Role) && node.Role.StartsWith("card/", StringComparison.OrdinalIgnoreCase))
            {
                return theme.ResolveVisualPreset("card/default");
            }

            if (!string.IsNullOrWhiteSpace(node.Role) &&
                (node.Role.StartsWith("window/", StringComparison.OrdinalIgnoreCase) ||
                 node.Role.StartsWith("panel/", StringComparison.OrdinalIgnoreCase)))
            {
                return theme.ResolveVisualPreset("panel/default");
            }

            return null;
        }

        private static Color ExtractFillColor(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme, bool isRoot)
        {
            if (TryResolveBackgroundPrimary(node, out var backgroundColor))
            {
                return backgroundColor;
            }

            if (preset != null && preset.enableFill)
            {
                return preset.fillColor;
            }

            if (isRoot && theme != null)
            {
                return theme.pageBackground;
            }

            if (theme == null)
            {
                return Color.clear;
            }

            if (node.ControlType == AIToUGUIControlType.Button)
            {
                return theme.buttonFill;
            }

            if (!string.IsNullOrWhiteSpace(node.Role) && node.Role.StartsWith("card/", StringComparison.OrdinalIgnoreCase))
            {
                return theme.cardFill;
            }

            return theme.panelFill;
        }

        private static Color ExtractGradientColor(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset)
        {
            if (TryResolveBackgroundSecondary(node, out var backgroundColor))
            {
                return backgroundColor;
            }

            return preset != null ? preset.gradientColor : Color.clear;
        }

        private static AIToUGUIGradientDirection ExtractGradientDirection(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset)
        {
            if (TryResolveGradientDirection(node, out var direction))
            {
                return direction;
            }

            return preset != null ? preset.gradientDirection : AIToUGUIGradientDirection.Vertical;
        }

        private static float ExtractCornerRadius(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme, AIToUGUIControlType controlType)
        {
            if (TryExtractCornerRadii(node, out var cornerRadii))
            {
                return Mathf.Max(
                    Mathf.Max(cornerRadii.x, cornerRadii.y),
                    Mathf.Max(cornerRadii.z, cornerRadii.w));
            }

            var radius = ParseFloat(GetStyle(node, "border-radius"), float.NaN);
            if (!float.IsNaN(radius))
            {
                return Mathf.Max(0f, radius);
            }

            if (HasExplicitVisualAuthoring(node))
            {
                return 0f;
            }

            if (preset != null)
            {
                return preset.cornerRadius;
            }

            if (theme == null)
            {
                return 0f;
            }

            if (controlType == AIToUGUIControlType.Button)
            {
                return theme.buttonRadius;
            }

            if (!string.IsNullOrWhiteSpace(node.Role) && node.Role.StartsWith("card/", StringComparison.OrdinalIgnoreCase))
            {
                return theme.cardRadius;
            }

            return theme.panelRadius;
        }

        private static bool HasExplicitVisualAuthoring(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(GetShapeId(node)) ||
                   !string.IsNullOrWhiteSpace(GetFrameId(node)) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "background")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "background-color")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "border")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "border-width")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "box-shadow")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-color")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-blur")) ||
                   !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-intensity"));
        }

        private static float ExtractOutlineWidth(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            var border = GetStyle(node, "border");
            if (!string.IsNullOrWhiteSpace(border))
            {
                var match = BorderWidthRegex.Match(border);
                if (match.Success && float.TryParse(match.Groups["width"].Value, out var width))
                {
                    return Mathf.Max(0f, width);
                }
            }

            var borderWidth = ParseFloat(GetStyle(node, "border-width"), float.NaN);
            if (!float.IsNaN(borderWidth))
            {
                return Mathf.Max(0f, borderWidth);
            }

            if (preset != null)
            {
                return preset.outlineWidth;
            }

            return theme != null ? theme.outlineWidth : 0f;
        }

        private static Color ExtractOutlineColor(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            if (TryResolveBorderColor(node, out var color))
            {
                return color;
            }

            if (preset != null)
            {
                return preset.outlineColor;
            }

            return theme != null ? theme.outlineColor : Color.clear;
        }

        private static float ExtractShadowSize(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            if (TryResolvePrimaryShadowLayer(node, out var layer, out var hasExplicitBoxShadow))
            {
                return Mathf.Max(Mathf.Abs(layer.Offset.x), Mathf.Abs(layer.Offset.y));
            }

            if (hasExplicitBoxShadow)
            {
                return 0f;
            }

            if (preset != null)
            {
                return preset.shadowSize;
            }

            return theme != null ? theme.shadowSize : 0f;
        }

        private static float ExtractShadowBlur(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            if (TryResolvePrimaryShadowLayer(node, out var layer, out var hasExplicitBoxShadow))
            {
                return Mathf.Max(0f, layer.Blur);
            }

            if (hasExplicitBoxShadow)
            {
                return 0f;
            }

            if (preset != null)
            {
                return preset.shadowBlur;
            }

            return theme != null ? theme.shadowBlur : 0f;
        }

        private static Color ExtractShadowColor(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            if (TryResolvePrimaryShadowLayer(node, out var layer, out var hasExplicitBoxShadow))
            {
                return layer.Color;
            }

            if (hasExplicitBoxShadow)
            {
                return Color.clear;
            }

            if (preset != null)
            {
                return preset.shadowColor;
            }

            return theme != null ? theme.shadowColor : Color.clear;
        }

        private static bool ExtractGlowEnabled(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset)
        {
            var raw = GetStyle(node, "-ai-glow");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
            }

            if (TryResolveGlowLayer(node, out _, out var hasExplicitBoxShadow))
            {
                return true;
            }

            if (hasExplicitBoxShadow)
            {
                return false;
            }

            return preset != null && preset.enableGlow;
        }

        private static Color ExtractGlowColor(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            if (TryParseColor(GetStyle(node, "-ai-glow-color"), out var color))
            {
                return color;
            }

            if (TryResolveGlowLayer(node, out var layer, out var hasExplicitBoxShadow))
            {
                return layer.Color;
            }

            if (hasExplicitBoxShadow)
            {
                return Color.clear;
            }

            if (preset != null)
            {
                return preset.glowColor;
            }

            return theme != null ? theme.glowColor : Color.clear;
        }

        private static float ExtractGlowBlur(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            var blur = ParseFloat(GetStyle(node, "-ai-glow-blur"), float.NaN);
            if (!float.IsNaN(blur))
            {
                return blur;
            }

            if (TryResolveGlowLayer(node, out var layer, out var hasExplicitBoxShadow))
            {
                return Mathf.Max(0f, layer.Blur);
            }

            if (hasExplicitBoxShadow)
            {
                return 0f;
            }

            if (preset != null)
            {
                return preset.glowBlur;
            }

            return theme != null ? theme.glowBlur : 0f;
        }

        private static float ExtractGlowIntensity(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset, AIToUGUIThemeDefinition theme)
        {
            var intensity = ParseFloat(GetStyle(node, "-ai-glow-intensity"), float.NaN);
            if (!float.IsNaN(intensity))
            {
                return intensity;
            }

            if (TryResolveGlowLayer(node, out _, out var hasExplicitBoxShadow))
            {
                return 1f;
            }

            if (hasExplicitBoxShadow)
            {
                return 0f;
            }

            if (preset != null)
            {
                return preset.glowIntensity;
            }

            return theme != null ? theme.glowIntensity : 0f;
        }

        private static bool HasGradient(AIToUGUICompiledNode node, AIToUGUIVisualPreset preset)
        {
            var background = GetStyle(node, "background");
            if (!string.IsNullOrWhiteSpace(background) && background.IndexOf("gradient", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return preset != null && preset.useGradient;
        }

        private static Color ExtractTextColor(AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (TryParseColor(GetStyle(node, "color"), out var color))
            {
                return color;
            }

            if (TryResolveTextBackgroundColor(node, theme, out var backgroundColor))
            {
                return ResolveAccessibleTextColor(backgroundColor);
            }

            if (theme == null)
            {
                return Color.white;
            }

            if (!string.IsNullOrWhiteSpace(node.Role) &&
                (node.Role.Contains("secondary", StringComparison.OrdinalIgnoreCase) ||
                 node.Role.Contains("muted", StringComparison.OrdinalIgnoreCase)))
            {
                return theme.textSecondary;
            }

            return theme.textPrimary;
        }

        private static bool TryResolveTextBackgroundColor(AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme, out Color color)
        {
            color = Color.clear;
            if (node == null)
            {
                return false;
            }

            if (TryResolveBackgroundPrimary(node, out var primaryColor))
            {
                if (TryResolveBackgroundSecondary(node, out var secondaryColor))
                {
                    color = AverageColor(primaryColor, secondaryColor);
                    return true;
                }

                color = primaryColor;
                return true;
            }

            var preset = ResolveVisualPreset(node, theme);
            if (preset == null || !preset.enableFill || preset.fillColor.a <= 0.001f)
            {
                return false;
            }

            if (preset.useGradient && preset.gradientColor.a > 0.001f)
            {
                color = AverageColor(preset.fillColor, preset.gradientColor);
                return true;
            }

            color = preset.fillColor;
            return true;
        }

        private static Color ResolveAccessibleTextColor(Color backgroundColor)
        {
            var whiteContrast = CalculateContrastRatio(Color.white, backgroundColor);
            var blackContrast = CalculateContrastRatio(Color.black, backgroundColor);
            return blackContrast > whiteContrast ? Color.black : Color.white;
        }

        private static Color AverageColor(Color a, Color b)
        {
            return new Color(
                (a.r + b.r) * 0.5f,
                (a.g + b.g) * 0.5f,
                (a.b + b.b) * 0.5f,
                Mathf.Max(a.a, b.a));
        }

        private static float CalculateContrastRatio(Color foreground, Color background)
        {
            var foregroundLuminance = CalculateRelativeLuminance(foreground);
            var backgroundLuminance = CalculateRelativeLuminance(background);
            var lighter = Mathf.Max(foregroundLuminance, backgroundLuminance);
            var darker = Mathf.Min(foregroundLuminance, backgroundLuminance);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        private static float CalculateRelativeLuminance(Color color)
        {
            return 0.2126f * LinearizeSrgb(color.r) +
                   0.7152f * LinearizeSrgb(color.g) +
                   0.0722f * LinearizeSrgb(color.b);
        }

        private static float LinearizeSrgb(float value)
        {
            value = Mathf.Clamp01(value);
            return value <= 0.03928f
                ? value / 12.92f
                : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        private static TextAlignmentOptions ResolveTextAlignment(AIToUGUICompiledNode node)
        {
            var value = GetStyle(node, "text-align");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return ResolveTextAlignmentFromStyle(value, node);
            }

            if (ShouldCenterAlignedText(node))
            {
                return TextAlignmentOptions.Center;
            }

            return ResolveTextAlignmentFromStyle(value, node);
        }

        private static TextAlignmentOptions ResolveTextAlignmentFromStyle(string value, AIToUGUICompiledNode node)
        {
            if (ShouldCenterAlignedText(node) && string.IsNullOrWhiteSpace(value))
            {
                return TextAlignmentOptions.Center;
            }

            return value switch
            {
                "center" => TextAlignmentOptions.Center,
                "right" => TextAlignmentOptions.Right,
                _ => TextAlignmentOptions.Left
            };
        }

        private static bool ShouldCenterAlignedText(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.ControlType == AIToUGUIControlType.Button ||
                string.Equals(node.TagName, "button", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(node.Role) &&
                (node.Role.StartsWith("button/", StringComparison.OrdinalIgnoreCase) ||
                 node.Role.StartsWith("chip/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return HasClass(node, "slab-button") ||
                   HasClass(node, "small-button") ||
                   HasClass(node, "chip");
        }

        private static void ConfigureTextLayout(TextMeshProUGUI text, AIToUGUICompiledNode node, Vector2 availableSize)
        {
            if (text == null || node == null)
            {
                return;
            }

            var baseFontSize = Mathf.Max(12f, ParseFloat(GetStyle(node, "font-size"), text.fontSize > 0f ? text.fontSize : 24f));
            var forcedSingleLine = ShouldForceSingleLine(node);
            var wrapping = !forcedSingleLine && ShouldWrapText(node);
            var maxLines = ResolveMaxVisibleLines(node, availableSize, baseFontSize, wrapping);
            var singleLine = forcedSingleLine || maxLines == 1;
            if (singleLine)
            {
                wrapping = false;
            }

            var hasHeightConstraint = availableSize.y > 0.5f;
            var autoFit = singleLine || hasHeightConstraint;

            text.enableWordWrapping = wrapping;
            text.overflowMode = autoFit ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            text.maxVisibleLines = maxLines > 0 ? maxLines : UnlimitedVisibleLines;
            text.enableAutoSizing = autoFit;
            if (text.enableAutoSizing)
            {
                text.fontSizeMax = baseFontSize;
                text.fontSizeMin = ResolveAutoFitFontSizeMin(baseFontSize, singleLine);
            }
            else
            {
                text.fontSizeMin = baseFontSize;
                text.fontSizeMax = baseFontSize;
            }
        }

        private static Vector2 ResolveInitialTextAvailableSize(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return Vector2.zero;
            }

            var width = ParseLength(GetStyle(node, "width"));
            var height = ParseLength(GetStyle(node, "height"));
            return new Vector2(
                width.IsValid ? Mathf.Max(0f, width.Value) : 0f,
                height.IsValid ? Mathf.Max(0f, height.Value) : 0f);
        }

        private static float ClampMeasuredTextHeight(AIToUGUICompiledNode node, float fontSize, float preferredHeight, float heightConstraint)
        {
            var height = preferredHeight;
            var maxLines = ResolveMaxVisibleLines(node, new Vector2(0f, heightConstraint), fontSize, ShouldWrapText(node));
            if (maxLines > 0)
            {
                height = Mathf.Min(height, ResolveTextLineHeightPixels(node, fontSize) * maxLines);
            }

            if (heightConstraint > 0f)
            {
                height = Mathf.Min(height, heightConstraint);
            }

            return Mathf.Max(0f, height);
        }

        private static bool ShouldForceSingleLine(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.ControlType == AIToUGUIControlType.Button ||
                string.Equals(node.TagName, "button", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(node.Role) && node.Role.StartsWith("button/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return HasClass(node, "headline") ||
                   HasClass(node, "section-title") ||
                   HasClass(node, "micro-label") ||
                   HasClass(node, "stat-number") ||
                   HasClass(node, "chip") ||
                   HasClass(node, "small-button") ||
                   HasClass(node, "slab-button");
        }

        private static bool ShouldWrapText(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (ShouldForceSingleLine(node))
            {
                return false;
            }

            if (HasClass(node, "body-text"))
            {
                return true;
            }

            return !string.Equals(node.TagName, "span", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveMaxVisibleLines(AIToUGUICompiledNode node, Vector2 availableSize, float fontSize, bool wrapping)
        {
            if (node == null)
            {
                return UnlimitedVisibleLines;
            }

            var lineLimitFromHeight = ResolveLineLimitFromHeight(node, availableSize, fontSize);
            if (ShouldForceSingleLine(node))
            {
                return 1;
            }

            if (!wrapping)
            {
                return lineLimitFromHeight > 0 ? lineLimitFromHeight : UnlimitedVisibleLines;
            }

            var cappedLines = lineLimitFromHeight > 0 ? lineLimitFromHeight : UnlimitedVisibleLines;
            if (HasClass(node, "body-text"))
            {
                cappedLines = cappedLines > 0
                    ? Mathf.Min(cappedLines, 4)
                    : 4;
            }

            return cappedLines > 0 ? Mathf.Max(1, cappedLines) : UnlimitedVisibleLines;
        }

        private static int ResolveLineLimitFromHeight(AIToUGUICompiledNode node, Vector2 availableSize, float fontSize)
        {
            if (node == null || availableSize.y <= 0f)
            {
                return UnlimitedVisibleLines;
            }

            var lineHeight = ResolveTextLineHeightPixels(node, fontSize);
            if (lineHeight <= 0f)
            {
                return UnlimitedVisibleLines;
            }

            return Mathf.Max(1, Mathf.FloorToInt((availableSize.y + 0.5f) / lineHeight));
        }

        private static float ResolveAutoFitFontSizeMin(float baseFontSize, bool singleLine)
        {
            var ratio = singleLine ? 0.25f : 0.45f;
            return Mathf.Max(4f, Mathf.Min(baseFontSize, baseFontSize * ratio));
        }

        private static float ResolveTextLineHeightPixels(AIToUGUICompiledNode node, float fontSize)
        {
            var lineHeight = GetStyle(node, "line-height");
            if (string.IsNullOrWhiteSpace(lineHeight))
            {
                return fontSize * 1.2f;
            }

            var normalized = lineHeight.Trim().ToLowerInvariant();
            if (normalized.EndsWith("px", StringComparison.Ordinal))
            {
                return Mathf.Max(0f, ParseFloat(normalized, fontSize));
            }

            if (normalized.EndsWith("%", StringComparison.Ordinal))
            {
                return Mathf.Max(0f, fontSize * ParseFloat(normalized, 100f) * 0.01f);
            }

            var value = ParseFloat(normalized, 1.2f);
            if (value <= 10f)
            {
                return Mathf.Max(0f, fontSize * value);
            }

            return Mathf.Max(0f, value);
        }

        private static bool HasClass(AIToUGUICompiledNode node, string className)
        {
            if (node == null || string.IsNullOrWhiteSpace(className))
            {
                return false;
            }

            for (var i = 0; i < node.Classes.Count; i++)
            {
                if (string.Equals(node.Classes[i], className, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldUseRouteLineAdapter(AIToUGUICompiledNode node)
        {
            return HasClass(node, "route-line") || ShouldUseThinRotatedLineAdapter(node);
        }

        private static bool ShouldUseThinRotatedLineAdapter(AIToUGUICompiledNode node)
        {
            if (node == null ||
                node.ControlType != AIToUGUIControlType.Div ||
                node.Children.Count > 0 ||
                !string.IsNullOrWhiteSpace(node.Text))
            {
                return false;
            }

            if (HasClass(node, "route-line") ||
                !HasExplicitThinLineFill(node) ||
                HasGradient(node, null) ||
                !IsVisiblyTiltedLineRotation(ResolveRotationZ(node)) ||
                ExtractOutlineWidth(node, null, null) > 0.001f ||
                ExtractCornerRadius(node, null, null, node.ControlType) > 0.25f ||
                !string.IsNullOrWhiteSpace(GetShapeId(node)) ||
                !string.IsNullOrWhiteSpace(GetFrameId(node)) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "box-shadow")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-color")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-blur")) ||
                !string.IsNullOrWhiteSpace(GetStyle(node, "-ai-glow-intensity")) ||
                string.Equals(ResolveBorderStyle(node), "dashed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var size = ResolveThinLineVisualSize(node);
            var shortSide = Mathf.Min(size.x, size.y);
            var longSide = Mathf.Max(size.x, size.y);
            return shortSide > 0f &&
                   shortSide <= 4f &&
                   longSide >= 24f &&
                   longSide >= shortSide * 8f;
        }

        private static bool HasExplicitThinLineFill(AIToUGUICompiledNode node)
        {
            return node != null &&
                   (!string.IsNullOrWhiteSpace(GetStyle(node, "background")) ||
                    !string.IsNullOrWhiteSpace(GetStyle(node, "background-color")));
        }

        private static Vector2 ResolveThinLineVisualSize(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return Vector2.zero;
            }

            var width = node.AbsoluteRect.Width > 0f
                ? node.AbsoluteRect.Width
                : ResolveLength(ParseLength(GetStyle(node, "width")), 0f, 0f);
            var height = node.AbsoluteRect.Height > 0f
                ? node.AbsoluteRect.Height
                : ResolveLength(ParseLength(GetStyle(node, "height")), 0f, 0f);
            return new Vector2(Mathf.Abs(width), Mathf.Abs(height));
        }

        private static bool IsVisiblyTiltedLineRotation(float rotation)
        {
            var normalized = Mathf.Abs(Mathf.Repeat(rotation, 180f));
            var axisDistance = Mathf.Min(
                normalized,
                Mathf.Abs(normalized - 90f),
                Mathf.Abs(normalized - 180f));
            return axisDistance >= 1f;
        }

        private static void RemoveComponentIfPresent<T>(GameObject target) where T : Component
        {
            if (target == null)
            {
                return;
            }

            var component = target.GetComponent<T>();
            if (component == null)
            {
                return;
            }

            AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
        }

        private static void RemoveComponentIfPresent(GameObject target, Type componentType)
        {
            if (target == null || componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return;
            }

            var component = target.GetComponent(componentType);
            if (component == null)
            {
                return;
            }

            AIToUGUIEditorUiMutationGuard.DestroyComponentSafely(component);
        }

        private static void ApplyTextStyling(TextMeshProUGUI text, AIToUGUICompiledNode node)
        {
            if (text == null || node == null)
            {
                return;
            }

            text.characterSpacing = ParseFloat(GetStyle(node, "letter-spacing"), 0f);

            var lineHeight = GetStyle(node, "line-height");
            if (!string.IsNullOrWhiteSpace(lineHeight))
            {
                var targetLineHeight = ResolveTextLineHeightPixels(node, text.fontSize);
                text.lineSpacing = Mathf.Max(-text.fontSize, targetLineHeight - text.fontSize);
            }

            var fontWeight = ParseFloat(GetStyle(node, "font-weight"), 400f);
            if (fontWeight >= 700f)
            {
                text.fontStyle |= FontStyles.Bold;
            }
        }

        private static TMP_FontAsset ResolveFont(AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (theme == null)
            {
                return null;
            }

            var fonts = theme.fonts ?? (theme.fonts = new AIToUGUIThemeFontSet());

            var fontFamily = GetStyle(node, "font-family");
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                if (fontFamily.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return fonts.monoFont != null ? fonts.monoFont : fonts.primaryFont;
                }

                if (fontFamily.IndexOf("heading", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fontFamily.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return fonts.headingFont != null ? fonts.headingFont : fonts.primaryFont;
                }
            }

            if (node.TagName == "h1" || node.TagName == "h2" || node.TagName == "h3")
            {
                return fonts.headingFont != null ? fonts.headingFont : fonts.primaryFont;
            }

            return fonts.primaryFont;
        }

        private static TMP_FontAsset ResolveFallbackFontAsset()
        {
            if (s_fallbackFontResolved)
            {
                return s_fallbackFontAsset;
            }

            s_fallbackFontResolved = true;
            s_fallbackFontAsset = TMP_Settings.defaultFontAsset;
            if (s_fallbackFontAsset != null)
            {
                return s_fallbackFontAsset;
            }

            var fontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (var i = 0; i < fontGuids.Length; i++)
            {
                var fontPath = AssetDatabase.GUIDToAssetPath(fontGuids[i]);
                if (string.IsNullOrWhiteSpace(fontPath))
                {
                    continue;
                }

                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (font == null)
                {
                    continue;
                }

                s_fallbackFontAsset = font;
                break;
            }

            return s_fallbackFontAsset;
        }

        private static AIToUGUIMotionPreset ResolveMotionPreset(AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme, bool isRoot)
        {
            if (theme == null)
            {
                return null;
            }

            var motionId = GetStyle(node, "-ai-motion");
            if (!string.IsNullOrWhiteSpace(motionId))
            {
                return theme.ResolveMotionPreset(motionId);
            }

            if (isRoot)
            {
                return theme.ResolveMotionPreset("motion/page") ?? theme.ResolveMotionPreset("motion/default");
            }

            if (node.ControlType == AIToUGUIControlType.Button)
            {
                return theme.ResolveMotionPreset("motion/button") ?? theme.ResolveMotionPreset("motion/default");
            }

            return null;
        }

        private static AIToUGUILoopMotionPreset ResolveLoopMotionPreset(AIToUGUICompiledNode node, AIToUGUIThemeDefinition theme)
        {
            if (node == null || theme == null)
            {
                return null;
            }

            var presetId = GetStyle(node, "-ai-loop-motion");
            if (string.IsNullOrWhiteSpace(presetId))
            {
                presetId = GetAttribute(node, "data-ui-loop-motion");
            }

            return string.IsNullOrWhiteSpace(presetId) ? null : theme.ResolveLoopMotionPreset(presetId);
        }

        private static void AttachLoopMotion(GameObject target, AIToUGUICompiledNode node, AIToUGUILoopMotionPreset preset)
        {
            if (target == null)
            {
                return;
            }

            var existingBinder = target.GetComponent<AIToUGUILoopMotionBinder>();
            if (preset == null)
            {
                if (existingBinder != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingBinder);
                }

                return;
            }

            var binder = existingBinder ?? target.AddComponent<AIToUGUILoopMotionBinder>();
            binder.Configure(preset, ResolveLoopMotionDelay(node));
        }

        private static float ResolveLoopMotionDelay(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return 0f;
            }

            var value = GetStyle(node, "-ai-loop-delay");
            if (string.IsNullOrWhiteSpace(value))
            {
                value = GetStyle(node, "animation-delay");
            }

            return ParseFloat(value, 0f);
        }

        private static void ApplyBuiltNodePostLayoutStyles(AIToUGUIBuiltNode built, bool isRoot)
        {
            if (built == null)
            {
                return;
            }

            ApplyNodeRotation(built.RectTransform, built.Node, isRoot);
            ApplyNodeTranslation(built.RectTransform, built.Node);
            ApplyNodeOpacity(built.GameObject, built.Node);
            RebaseLoopMotionState(ResolveMotionTarget(built.GameObject, built.Node));

            for (var i = 0; i < built.Children.Count; i++)
            {
                ApplyBuiltNodePostLayoutStyles(built.Children[i], false);
            }
        }

        private static void ApplyNodeRotation(RectTransform rect, AIToUGUICompiledNode node, bool isRoot)
        {
            if (rect == null)
            {
                return;
            }

            var rotation = rect.localEulerAngles;
            rotation.x = 0f;
            rotation.y = 0f;
            // CSS rotate() uses the opposite on-screen sign compared with Unity's Z rotation.
            rotation.z = isRoot ? 0f : -ResolveRotationZ(node);
            rect.localEulerAngles = rotation;
        }

        private static float ResolveRotationZ(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return 0f;
            }

            if (TryParseRotationDegrees(GetStyle(node, "-ai-rotation-z"), out var degrees))
            {
                return degrees;
            }

            if (TryParseRotationDegrees(GetAttribute(node, "data-ui-rotation"), out degrees))
            {
                return degrees;
            }

            if (TryParseRotationDegrees(GetStyle(node, "transform"), out degrees))
            {
                return degrees;
            }

            return 0f;
        }

        private static bool TryParseRotationDegrees(string raw, out float degrees)
        {
            degrees = 0f;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var normalized = raw.Trim();
            var match = RotateTransformRegex.Match(normalized);
            if (match.Success)
            {
                return float.TryParse(match.Groups["angle"].Value, out degrees);
            }

            if (normalized.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 3).Trim();
            }

            return float.TryParse(normalized, out degrees);
        }

        private static void ApplyNodeTranslation(RectTransform rect, AIToUGUICompiledNode node)
        {
            if (rect == null || node == null)
            {
                return;
            }

            var translateX = ResolveTranslateOffset(GetStyle(node, "-ai-translate-x"), rect.rect.width);
            var translateY = ResolveTranslateOffset(GetStyle(node, "-ai-translate-y"), rect.rect.height);
            if (Mathf.Abs(translateX) <= 0.001f && Mathf.Abs(translateY) <= 0.001f)
            {
                return;
            }

            rect.anchoredPosition += new Vector2(translateX, -translateY);
        }

        private static float ResolveTranslateOffset(string raw, float referenceSize)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0f;
            }

            var length = ParseLength(raw);
            return length.IsValid ? ResolveLength(length, referenceSize, 0f) : 0f;
        }

        private static void ApplyNodeOpacity(GameObject target, AIToUGUICompiledNode node)
        {
            if (target == null)
            {
                return;
            }

            var opacity = ResolveOpacity(node);
            if (float.IsNaN(opacity))
            {
                return;
            }

            opacity = Mathf.Clamp01(opacity);
            var hasChildTree = target.transform.childCount > 0;
            var directGraphics = target.GetComponents<Graphic>();
            if (directGraphics.Length > 0 && !hasChildTree)
            {
                for (var i = 0; i < directGraphics.Length; i++)
                {
                    var color = directGraphics[i].color;
                    color.a *= opacity;
                    directGraphics[i].color = color;
                }

                return;
            }

            if (!target.TryGetComponent<CanvasGroup>(out var group) || group == null)
            {
                try
                {
                    group = target.AddComponent<CanvasGroup>();
                }
                catch (Exception)
                {
                    return;
                }
            }

            if (group != null)
            {
                group.alpha = opacity;
            }
        }

        private static float ResolveOpacity(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return float.NaN;
            }

            var value = GetStyle(node, "opacity");
            return string.IsNullOrWhiteSpace(value) ? float.NaN : ParseFloat(value, float.NaN);
        }

        private static void RebaseLoopMotionState(GameObject target)
        {
            if (target != null && target.TryGetComponent<AIToUGUILoopMotionBinder>(out var binder))
            {
                binder.RebaseRestState();
            }
        }

        private static string ResolveBorderStyle(AIToUGUICompiledNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            var borderStyle = GetStyle(node, "-ai-border-style");
            if (!string.IsNullOrWhiteSpace(borderStyle))
            {
                return borderStyle.Trim();
            }

            borderStyle = GetStyle(node, "border-style");
            if (!string.IsNullOrWhiteSpace(borderStyle))
            {
                return borderStyle.Trim();
            }

            var border = GetStyle(node, "border");
            return !string.IsNullOrWhiteSpace(border) && border.IndexOf("dashed", StringComparison.OrdinalIgnoreCase) >= 0
                ? "dashed"
                : "solid";
        }

        private static bool HasExplicitBackgroundAuthoring(AIToUGUICompiledNode node)
        {
            return node != null &&
                   (!string.IsNullOrWhiteSpace(GetStyle(node, "background")) ||
                    !string.IsNullOrWhiteSpace(GetStyle(node, "background-color")));
        }

        private static void ConfigureDashedBorder(GameObject target, AIToUGUICompiledNode node, float radius, Color outlineColor, float outlineWidth)
        {
            if (target == null)
            {
                return;
            }

            var adapter = target.GetComponent<AIToUGUIDashedBorderAdapter>() ?? target.AddComponent<AIToUGUIDashedBorderAdapter>();
            var thickness = Mathf.Max(1f, outlineWidth > 0f ? outlineWidth : 1f);
            var forceEllipse = ShouldForceEllipseDashedBorder(node);
            var dashLengthRaw = forceEllipse ? Mathf.Max(8f, thickness * 3.2f) : Mathf.Max(10f, thickness * 4.2f);
            var dashLength = Mathf.Max(1f, Mathf.Round(dashLengthRaw));
            var gapLength = Mathf.Max(1f, Mathf.Round(Mathf.Max(4f, dashLength * 0.58f)));
            adapter.Configure(thickness, outlineColor, radius, forceEllipse, dashLength, gapLength);
        }

        private static void CleanupDashedBorder(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            var adapter = target.GetComponent<AIToUGUIDashedBorderAdapter>();
            if (adapter != null)
            {
                UnityEngine.Object.DestroyImmediate(adapter);
            }
        }

        private static bool ShouldForceEllipseDashedBorder(AIToUGUICompiledNode node)
        {
            var shapeId = GetShapeId(node);
            if (string.Equals(shapeId, "circle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shapeId, "ellipse", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var role = node != null ? node.Role : string.Empty;
            return !string.IsNullOrWhiteSpace(role) &&
                   (role.IndexOf("ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    role.IndexOf("orb", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static GameObject CreateStretchChild(GameObject parent, string name)
        {
            return CreateStretchChild(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static void ApplyContentPadding(RectTransform rect, AIToUGUICompiledNode node)
        {
            if (rect == null || node == null)
            {
                return;
            }

            var padding = ParseBox(GetStyle(node, "padding"));
            rect.offsetMin = new Vector2(padding.left, padding.bottom);
            rect.offsetMax = new Vector2(-padding.right, -padding.top);
        }

        private static void StretchToParent(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static GameObject CreateStretchChild(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            return CreateStretchChild(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        }

        private static GameObject CreateStretchChild(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var child = CreateChild(parent, name);
            var rect = child.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            return child;
        }

        private static GameObject CreateAnchoredChild(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPosition)
        {
            var child = CreateChild(parent, name);
            var rect = child.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            rect.pivot = new Vector2(0.5f, 0.5f);
            return child;
        }

        private static GameObject CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            child.transform.SetParent(parent.transform, false);
            ConfigureInternalLayoutIsolation(parent, child, name);
            return child;
        }

        private static void ConfigureInternalLayoutIsolation(GameObject parent, GameObject child, string childName)
        {
            if (parent == null || child == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(childName) ||
                !childName.StartsWith(InternalNodePrefix, StringComparison.Ordinal) ||
                !HasDirectSemanticLayout(parent))
            {
                return;
            }

            var layoutElement = child.GetComponent<LayoutElement>() ?? child.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var flexalonObject = GetOrAddFlexalonComponent<global::Flexalon.FlexalonObject>(child);
            flexalonObject.SkipLayout = true;
        }

        private static bool HasDirectSemanticLayout(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            var components = target.GetComponents<global::Flexalon.FlexalonComponent>();
            for (var i = 0; i < components.Length; i++)
            {
                if (IsSemanticLayoutComponent(components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveBackgroundPrimary(AIToUGUICompiledNode node, out Color color)
        {
            var background = GetStyle(node, "background");
            var colors = ExtractColors(background);
            if (colors.Count > 0)
            {
                color = colors[0];
                return true;
            }

            var backgroundColor = GetStyle(node, "background-color");
            if (TryParseColor(backgroundColor, out color))
            {
                return true;
            }

            color = Color.clear;
            return false;
        }

        private static void EnsureCanvasChannels(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.rootCanvas.additionalShaderChannels |= RequiredProceduralUiChannels;
        }

        private static bool TryResolveBackgroundSecondary(AIToUGUICompiledNode node, out Color color)
        {
            var background = GetStyle(node, "background");
            var colors = ExtractColors(background);
            if (colors.Count > 1)
            {
                color = colors[1];
                return true;
            }

            color = Color.clear;
            return false;
        }

        private static bool TryResolveGradientDirection(AIToUGUICompiledNode node, out AIToUGUIGradientDirection direction)
        {
            var background = GetStyle(node, "background");
            if (string.IsNullOrWhiteSpace(background))
            {
                direction = AIToUGUIGradientDirection.Vertical;
                return false;
            }

            var match = LinearGradientDirectionRegex.Match(background);
            if (!match.Success)
            {
                direction = background.IndexOf("radial-gradient", StringComparison.OrdinalIgnoreCase) >= 0
                    ? AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight
                    : AIToUGUIGradientDirection.Vertical;
                return false;
            }

            var raw = match.Groups["direction"].Value.Trim().ToLowerInvariant();
            direction = raw switch
            {
                "to right" => AIToUGUIGradientDirection.Horizontal,
                "to left" => AIToUGUIGradientDirection.Horizontal,
                "to bottom" => AIToUGUIGradientDirection.Vertical,
                "to top" => AIToUGUIGradientDirection.Vertical,
                _ when raw.Contains("45") => AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight,
                _ when raw.Contains("135") => AIToUGUIGradientDirection.DiagonalBottomLeftToTopRight,
                _ when raw.Contains("90") || raw.Contains("270") => AIToUGUIGradientDirection.Horizontal,
                _ => AIToUGUIGradientDirection.Vertical
            };
            return true;
        }

        private static bool TryResolveBorderColor(AIToUGUICompiledNode node, out Color color)
        {
            var borderColor = GetStyle(node, "border-color");
            if (TryParseColor(borderColor, out color))
            {
                return true;
            }

            var border = GetStyle(node, "border");
            var matches = ColorRegex.Matches(border);
            if (matches.Count > 0 && TryParseColor(matches[matches.Count - 1].Value, out color))
            {
                return true;
            }

            color = Color.clear;
            return false;
        }

        private static bool TryResolveShadowValues(AIToUGUICompiledNode node, out Vector2 offset, out float blur, out Color color)
        {
            offset = Vector2.zero;
            blur = 0f;
            color = Color.clear;

            if (TryResolvePrimaryShadowLayer(node, out var layer, out _))
            {
                offset = layer.Offset;
                blur = layer.Blur;
                color = layer.Color;
                return true;
            }

            return false;
        }

        private readonly struct AIToUGUIBoxShadowLayer
        {
            public AIToUGUIBoxShadowLayer(Vector2 offset, float blur, float spread, Color color, bool inset)
            {
                Offset = offset;
                Blur = blur;
                Spread = spread;
                Color = color;
                Inset = inset;
            }

            public Vector2 Offset { get; }
            public float Blur { get; }
            public float Spread { get; }
            public Color Color { get; }
            public bool Inset { get; }
            public bool HasVisibleColor => Color.a > 0.001f;
            public bool HasOffset => Mathf.Abs(Offset.x) > 0.01f || Mathf.Abs(Offset.y) > 0.01f;
        }

        private static bool TryResolvePrimaryShadowLayer(
            AIToUGUICompiledNode node,
            out AIToUGUIBoxShadowLayer layer,
            out bool hasExplicitBoxShadow)
        {
            if (TryGetBoxShadowLayers(node, out var layers, out hasExplicitBoxShadow))
            {
                var bestIndex = -1;
                var bestScore = float.MinValue;
                for (var i = 0; i < layers.Count; i++)
                {
                    var candidate = layers[i];
                    if (candidate.Inset || !candidate.HasVisibleColor || !candidate.HasOffset)
                    {
                        continue;
                    }

                    var score = candidate.Offset.sqrMagnitude +
                                candidate.Blur * 0.01f +
                                candidate.Color.a * 0.001f +
                                (i + 1) * 0.0001f;
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestIndex = i;
                }

                if (bestIndex >= 0)
                {
                    layer = layers[bestIndex];
                    return true;
                }
            }

            layer = default;
            return false;
        }

        private static bool TryResolveGlowLayer(
            AIToUGUICompiledNode node,
            out AIToUGUIBoxShadowLayer layer,
            out bool hasExplicitBoxShadow)
        {
            if (TryGetBoxShadowLayers(node, out var layers, out hasExplicitBoxShadow))
            {
                var bestIndex = -1;
                var bestScore = float.MinValue;
                for (var i = 0; i < layers.Count; i++)
                {
                    var candidate = layers[i];
                    if (candidate.Inset || !candidate.HasVisibleColor || candidate.HasOffset)
                    {
                        continue;
                    }

                    if (candidate.Blur <= 0.01f && candidate.Spread <= 0.01f)
                    {
                        continue;
                    }

                    var score = candidate.Blur +
                                candidate.Spread * 0.1f +
                                candidate.Color.a * 0.01f +
                                (i + 1) * 0.0001f;
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestIndex = i;
                }

                if (bestIndex >= 0)
                {
                    layer = layers[bestIndex];
                    return true;
                }
            }

            layer = default;
            return false;
        }

        private static bool TryGetBoxShadowLayers(
            AIToUGUICompiledNode node,
            out List<AIToUGUIBoxShadowLayer> layers,
            out bool hasExplicitBoxShadow)
        {
            layers = new List<AIToUGUIBoxShadowLayer>();
            var boxShadow = GetStyle(node, "box-shadow");
            hasExplicitBoxShadow = !string.IsNullOrWhiteSpace(boxShadow);
            if (!hasExplicitBoxShadow)
            {
                return false;
            }

            if (string.Equals(boxShadow.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rawLayers = SplitCssCommaSeparated(boxShadow);
            for (var i = 0; i < rawLayers.Count; i++)
            {
                if (TryParseBoxShadowLayer(rawLayers[i], out var layer))
                {
                    layers.Add(layer);
                }
            }

            return layers.Count > 0;
        }

        private static List<string> SplitCssCommaSeparated(string raw)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return values;
            }

            var depth = 0;
            var segmentStart = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                switch (raw[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth = Mathf.Max(0, depth - 1);
                        break;
                    case ',' when depth == 0:
                        var segment = raw.Substring(segmentStart, i - segmentStart).Trim();
                        if (!string.IsNullOrWhiteSpace(segment))
                        {
                            values.Add(segment);
                        }

                        segmentStart = i + 1;
                        break;
                }
            }

            var tail = raw.Substring(segmentStart).Trim();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                values.Add(tail);
            }

            return values;
        }

        private static bool TryParseBoxShadowLayer(string raw, out AIToUGUIBoxShadowLayer layer)
        {
            layer = default;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var working = raw.Trim();
            var inset = working.IndexOf("inset", StringComparison.OrdinalIgnoreCase) >= 0;

            var color = Color.clear;
            var colorMatch = ColorRegex.Match(working);
            if (colorMatch.Success)
            {
                TryParseColor(colorMatch.Value, out color);
                working = working.Remove(colorMatch.Index, colorMatch.Length);
            }

            var tokens = working
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !string.Equals(token, "inset", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (tokens.Length < 2)
            {
                return false;
            }

            var offset = new Vector2(
                ParseCssNumberToken(tokens[0]),
                ParseCssNumberToken(tokens[1]));
            var blur = tokens.Length >= 3 ? Mathf.Max(0f, ParseCssNumberToken(tokens[2])) : 0f;
            var spread = tokens.Length >= 4 ? ParseCssNumberToken(tokens[3]) : 0f;
            layer = new AIToUGUIBoxShadowLayer(offset, blur, spread, color, inset);
            return true;
        }

        private static float ParseCssNumberToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return 0f;
            }

            var match = NumberRegex.Match(token);
            return match.Success ? ParseFloat(match.Value, 0f) : 0f;
        }

        private static List<Color> ExtractColors(string raw)
        {
            var colors = new List<Color>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return colors;
            }

            foreach (Match match in ColorRegex.Matches(raw))
            {
                if (TryParseColor(match.Value, out var color))
                {
                    colors.Add(color);
                }
            }

            return colors;
        }

        private static bool TryParseColor(string raw, out Color color)
        {
            color = Color.clear;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            raw = raw.Trim();
            if (string.Equals(raw, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = new Color(0f, 0f, 0f, 0f);
                return true;
            }

            if (raw.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString(raw, out color))
            {
                return true;
            }

            if (raw.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var values = NumberRegex.Matches(raw).Cast<Match>().Select(match => ParseFloat(match.Value, 0f)).ToArray();
                if (values.Length >= 3)
                {
                    var alpha = values.Length >= 4 ? values[3] : 1f;
                    if (alpha > 1f)
                    {
                        alpha /= 255f;
                    }

                    color = new Color(values[0] / 255f, values[1] / 255f, values[2] / 255f, Mathf.Clamp01(alpha));
                    return true;
                }
            }

            return false;
        }
    }
}

#endif
