#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Editor
{
    [Serializable]
    internal sealed class AIToUGUISitePackageManifest
    {
        public string siteId = "site_id";
        public string displayName = "UI Site";
        public int designWidth = 1920;
        public int designHeight = 1080;
        public string themeCss = "theme.css";
        public string[] sharedStyles = Array.Empty<string>();
        public string prefabOutputRoot = AIToUGUIGeneratedAssetPaths.GetPrefabsRoot("site_id");
        public string metadataOutputRoot = AIToUGUIGeneratedAssetPaths.GetMetadataRoot("site_id");
        public AIToUGUIPagePackageManifest[] pages = Array.Empty<AIToUGUIPagePackageManifest>();
    }

    [Serializable]
    internal sealed class AIToUGUIPagePackageManifest
    {
        public string pageId = "page_id";
        public string displayName = "Page";
        public string html = "pages/page.html";
        public string prefabName = "GeneratedPage";
        public string runtimePageId = string.Empty;
        public string targetLayer = "Normal";
        public bool attachPanelComponent = false;
        public string panelComponentTypeName = string.Empty;
        public string[] localStyles = Array.Empty<string>();
    }

    internal enum AIToUGUIStudioSection
    {
        Home,
        Workspace
    }

    internal sealed class AIToUGUIStudioWindow : EditorWindow
    {
        private const string SvgResolutionEditorPrefsKey = "AIToUGUI.SvgTargetResolution";
        [SerializeField] private AIToUGUIPreviewMount _previewMount;
        [SerializeField] private AIToUGUIStudioSection _activeSection = AIToUGUIStudioSection.Home;
        [SerializeField] private int _svgTargetResolution = 512;

        private readonly List<AIToUGUICompiledBundleDefinition> _bundles = new List<AIToUGUICompiledBundleDefinition>();

        private TextAsset _bundleJsonAsset;
        private AIToUGUICompiledBundleDefinition _selectedBundle;
        private AIToUGUICompiledBundlePageSummary _selectedPage;
        private AIToUGUIParsedBundle _parsedBundle;
        private AIToUGUICompiledPage _previewPage;

        private Vector2 _bundleScroll;
        private Vector2 _pageScroll;
        private Vector2 _detailScroll;
        private GUIStyle _selectedCardStyle;
        private GUIStyle _normalCardStyle;
        private GUIStyle _selectedToolbarTabStyle;
        private GUIStyle _normalToolbarTabStyle;

        [MenuItem("Tools/AIToUGUI/Studio")]
        private static void Open()
        {
            var window = GetWindow<AIToUGUIStudioWindow>("AIToUGUI \u5DE5\u4F5C\u53F0");
            window.minSize = new Vector2(1280f, 760f);
            window.RefreshBundles();
        }

        private void OnEnable()
        {
            _svgTargetResolution = EditorPrefs.GetInt(SvgResolutionEditorPrefsKey, 512);
            RefreshBundles();
            if (_previewMount == null)
            {
                _previewMount = FindObjectOfType<AIToUGUIPreviewMount>();
            }
        }

        private void OnDisable()
        {
            DisposeParsedBundle();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawTopTabs();
            EditorGUILayout.Space(10f);

            switch (_activeSection)
            {
                case AIToUGUIStudioSection.Home:
                    DrawHomePage();
                    break;
                case AIToUGUIStudioSection.Workspace:
                    DrawWorkspacePage();
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (_normalCardStyle == null)
            {
                _normalCardStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 62f,
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12,
                    wordWrap = true,
                    padding = new RectOffset(12, 12, 8, 8)
                };
            }

            if (_selectedCardStyle == null)
            {
                _selectedCardStyle = new GUIStyle(_normalCardStyle)
                {
                    fontStyle = FontStyle.Bold
                };
            }

            if (_normalToolbarTabStyle == null)
            {
                _normalToolbarTabStyle = new GUIStyle(EditorStyles.toolbarButton)
                {
                    fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (_selectedToolbarTabStyle == null)
            {
                _selectedToolbarTabStyle = new GUIStyle(EditorStyles.toolbarButton)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        private void DrawTopTabs()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawToolbarTab(AIToUGUIStudioSection.Home, "\u9996\u9875");
                DrawToolbarTab(AIToUGUIStudioSection.Workspace, "Bundle \u5DE5\u4F5C\u533A", _selectedBundle != null);
                GUILayout.FlexibleSpace();

                if (_selectedBundle != null)
                {
                    GUILayout.Label($"\u5F53\u524D Bundle\uff1A{_selectedBundle.displayName}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawToolbarTab(AIToUGUIStudioSection section, string label, bool enabled = true)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                var previousColor = GUI.backgroundColor;
                GUI.backgroundColor = _activeSection == section ? new Color(0.22f, 0.55f, 0.95f, 1f) : previousColor;
                if (GUILayout.Button(label, _activeSection == section ? _selectedToolbarTabStyle : _normalToolbarTabStyle, GUILayout.Width(128f)))
                {
                    _activeSection = section;
                }

                GUI.backgroundColor = previousColor;
            }
        }

        private void DrawHomePage()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("\u5BFC\u5165 Bundle", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "\u9009\u62E9 compiled_site_bundle.json\uff0c\u5E76\u5BFC\u5165\u4E3A Unity \u5185\u7684 Bundle \u8D44\u4EA7\u3002",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Bundle JSON", GUILayout.Width(78f));
                    _bundleJsonAsset = (TextAsset)EditorGUILayout.ObjectField(_bundleJsonAsset, typeof(TextAsset), false);

                    using (new EditorGUI.DisabledScope(_bundleJsonAsset == null))
                    {
                        if (GUILayout.Button("\u5BFC\u5165 JSON", GUILayout.Width(96f)))
                        {
                            var imported = AIToUGUIBundleImportUtility.ImportBundle(_bundleJsonAsset);
                            RefreshBundles();
                            SelectBundle(imported);
                            if (imported != null)
                            {
                                _activeSection = AIToUGUIStudioSection.Workspace;
                            }
                        }
                    }

                    if (GUILayout.Button("\u5237\u65B0\u5217\u8868", GUILayout.Width(96f)))
                    {
                        RefreshBundles();
                    }
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    "\u63A8\u8350\u6D41\u7A0B\uff1A\n" +
                    "1. \u5916\u90E8 AI \u751F\u6210 HTML \u7AD9\u70B9\u5305\u3002\n" +
                    "2. \u7528 Python \u5DE5\u5177\u6821\u9A8C\u5E76\u7F16\u8BD1\u4E3A compiled_site_bundle.json\u3002\n" +
                    "3. \u5728\u8FD9\u91CC\u5BFC\u5165 JSON\u3002\n" +
                    "4. \u8FDB\u5165 Bundle \u5DE5\u4F5C\u533A\u9884\u89C8\u3001\u8C03\u6574\u5E76\u5BFC\u51FA Prefab\u3002",
                    MessageType.None);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("\u5DF2\u6709 Bundles", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    _bundles.Count == 0
                        ? "\u5F53\u524D\u8FD8\u6CA1\u6709\u5BFC\u5165\u8FC7 Bundle\u3002"
                        : $"\u5171 {_bundles.Count} \u4E2A Bundle",
                    EditorStyles.miniLabel);
                EditorGUILayout.Space(6f);

                _bundleScroll = EditorGUILayout.BeginScrollView(_bundleScroll);
                if (_bundles.Count == 0)
                {
                    EditorGUILayout.HelpBox("\u5148\u5728\u4E0A\u65B9\u5BFC\u5165\u4E00\u4E2A compiled_site_bundle.json\u3002", MessageType.Info);
                }

                for (var i = 0; i < _bundles.Count; i++)
                {
                    var bundle = _bundles[i];
                    if (bundle == null)
                    {
                        continue;
                    }

                    DrawBundleCard(bundle);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawWorkspacePage()
        {
            if (_selectedBundle == null)
            {
                EditorGUILayout.HelpBox("\u8FD8\u6CA1\u6709\u9009\u4E2D Bundle\uff0c\u8BF7\u5148\u56DE\u5230\u9996\u9875\u5BFC\u5165\u6216\u9009\u62E9\u4E00\u4E2A Bundle\u3002", MessageType.Info);
                if (GUILayout.Button("\u8FD4\u56DE\u9996\u9875", GUILayout.Width(100f)))
                {
                    _activeSection = AIToUGUIStudioSection.Home;
                }

                return;
            }

            DrawWorkspaceToolbar();
            EditorGUILayout.Space(10f);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPageColumn();
                GUILayout.Space(12f);
                DrawDetailColumn();
            }
        }

        private void DrawWorkspaceToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Bundle\uff1A{_selectedBundle.displayName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_selectedBundle.siteId, EditorStyles.miniLabel);
                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("\u8FD4\u56DE\u9996\u9875", GUILayout.Width(88f)))
                    {
                        _activeSection = AIToUGUIStudioSection.Home;
                    }

                    if (GUILayout.Button("\u540C\u6B65 Bundle", GUILayout.Width(96f)))
                    {
                        AIToUGUIBundleImportUtility.RefreshBundle(_selectedBundle);
                        RefreshBundles();
                        SelectBundle(_selectedBundle);
                    }

                    if (GUILayout.Button("\u6821\u9A8C", GUILayout.Width(72f)))
                    {
                        RefreshSelection();
                    }

                    if (GUILayout.Button("SVG \u8D44\u4EA7", GUILayout.Width(88f)))
                    {
                        PrepareSvgAssetsForSelection();
                    }

                    if (GUILayout.Button("\u5BFC\u51FA\u6574\u5305", GUILayout.Width(88f)))
                    {
                        ReplaceParsedBundle(AIToUGUIBundleBaker.BakeBundle(_selectedBundle));
                        UpdatePreviewPageFromSelection();
                    }

                    using (new EditorGUI.DisabledScope(_selectedPage == null))
                    {
                        if (GUILayout.Button("\u5BFC\u51FA\u5F53\u524D\u9875", GUILayout.Width(96f)))
                        {
                            ReplaceParsedBundle(AIToUGUIBundleBaker.BakePage(_selectedBundle, _selectedPage.pageId, _previewMount, true));
                            UpdatePreviewPageFromSelection();
                        }
                    }

                    using (new EditorGUI.DisabledScope(_selectedPage == null || _previewMount == null))
                    {
                        if (GUILayout.Button("\u9884\u89C8\u5F53\u524D\u9875", GUILayout.Width(96f)))
                        {
                            PreviewSelection();
                        }

                        if (GUILayout.Button("\u6E05\u7A7A\u9884\u89C8", GUILayout.Width(88f)))
                        {
                            AIToUGUISiteBaker.ClearPreview(_previewMount);
                        }
                    }
                }

                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("\u9884\u89C8\u6302\u70B9", GUILayout.Width(60f));
                    _previewMount = (AIToUGUIPreviewMount)EditorGUILayout.ObjectField(_previewMount, typeof(AIToUGUIPreviewMount), true);
                    EditorGUILayout.LabelField("SVG \u5206\u8FA8\u7387", GUILayout.Width(72f));
                    _svgTargetResolution = EditorGUILayout.IntPopup(
                        _svgTargetResolution,
                        new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096" },
                        new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096 },
                        GUILayout.Width(88f));
                    EditorPrefs.SetInt(SvgResolutionEditorPrefsKey, _svgTargetResolution);
                }
            }
        }

        private void DrawBundleCard(AIToUGUICompiledBundleDefinition bundle)
        {
            var selected = bundle == _selectedBundle;
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = selected ? new Color(0.20f, 0.52f, 0.96f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);

            var label = selected
                ? $"\u25CF {bundle.displayName}\n{bundle.siteId}"
                : $"{bundle.displayName}\n{bundle.siteId}";
            if (GUILayout.Button(label, selected ? _selectedCardStyle : _normalCardStyle))
            {
                SelectBundle(bundle);
                _activeSection = AIToUGUIStudioSection.Workspace;
            }

            GUI.backgroundColor = previousColor;
        }

        private void DrawPageColumn()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(340f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("\u9875\u9762\u5217\u8868", EditorStyles.boldLabel);
                var pageCount = _selectedBundle != null && _selectedBundle.pages != null ? _selectedBundle.pages.Count : 0;
                EditorGUILayout.LabelField(
                    _selectedBundle != null ? $"\u5171 {pageCount} \u9875" : "\u672A\u9009\u62E9 Bundle",
                    EditorStyles.miniLabel);
                EditorGUILayout.Space(6f);

                _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll);
                if (_selectedBundle == null)
                {
                    EditorGUILayout.HelpBox("\u8BF7\u5148\u4ECE\u9996\u9875\u9009\u62E9\u4E00\u4E2A Bundle\u3002", MessageType.None);
                }
                else if (_selectedBundle.pages == null || _selectedBundle.pages.Count == 0)
                {
                    EditorGUILayout.HelpBox("\u5F53\u524D Bundle \u6CA1\u6709\u9875\u9762\u3002", MessageType.Warning);
                }
                else
                {
                    for (var i = 0; i < _selectedBundle.pages.Count; i++)
                    {
                        var page = _selectedBundle.pages[i];
                        if (page == null)
                        {
                            continue;
                        }

                        var selected = page == _selectedPage;
                        var previousColor = GUI.backgroundColor;
                        GUI.backgroundColor = selected ? new Color(0.20f, 0.52f, 0.96f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);

                        var label = selected
                            ? $"\u25CF {page.displayName} [{page.pageId}]\n{page.prefabName}"
                            : $"{page.displayName} [{page.pageId}]\n{page.prefabName}";
                        if (GUILayout.Button(label, selected ? _selectedCardStyle : _normalCardStyle))
                        {
                            _selectedPage = page;
                            UpdatePreviewPageFromSelection();
                        }

                        GUI.backgroundColor = previousColor;
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawDetailColumn()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("\u5F53\u524D\u4FE1\u606F", EditorStyles.boldLabel);
                EditorGUILayout.Space(6f);

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                if (_selectedBundle == null)
                {
                    EditorGUILayout.HelpBox("\u5F53\u524D\u6CA1\u6709\u9009\u4E2D Bundle\u3002", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                DrawSelectionSummary();
                EditorGUILayout.Space(12f);
                DrawWarnings();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSelectionSummary()
        {
            EditorGUILayout.LabelField("\u5F53\u524D\u9009\u62E9", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Bundle", $"{_selectedBundle.displayName} ({_selectedBundle.siteId})");
            EditorGUILayout.LabelField("\u8BBE\u8BA1\u5206\u8FA8\u7387", _selectedBundle.designResolution.ToString());
            EditorGUILayout.LabelField("JSON \u8DEF\u5F84", _selectedBundle.bundleJson != null ? AssetDatabase.GetAssetPath(_selectedBundle.bundleJson) : "-");
            EditorGUILayout.LabelField("Prefab \u8F93\u51FA\u76EE\u5F55", string.IsNullOrWhiteSpace(_selectedBundle.prefabOutputRoot) ? "-" : _selectedBundle.prefabOutputRoot);
            EditorGUILayout.LabelField("Metadata \u8F93\u51FA\u76EE\u5F55", string.IsNullOrWhiteSpace(_selectedBundle.metadataOutputRoot) ? "-" : _selectedBundle.metadataOutputRoot);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Export Options", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var keepExportNodeMarkers = EditorGUILayout.Toggle("Keep Export Node Markers", _selectedBundle.keepExportNodeMarkers);
            var keepAssetBindingManifests = EditorGUILayout.Toggle("Keep Asset Binding Manifests", _selectedBundle.keepAssetBindingManifests);
            var useOverflowMaskHosts = EditorGUILayout.Toggle("Use Overflow Mask Hosts", _selectedBundle.useOverflowMaskHosts);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selectedBundle, "Update Export Options");
                _selectedBundle.keepExportNodeMarkers = keepExportNodeMarkers;
                _selectedBundle.keepAssetBindingManifests = keepAssetBindingManifests;
                _selectedBundle.useOverflowMaskHosts = useOverflowMaskHosts;
                EditorUtility.SetDirty(_selectedBundle);
            }

            if (!_selectedBundle.keepExportNodeMarkers)
            {
                EditorGUILayout.HelpBox("Turning off Export Node Markers disables runtime slot/container/template lookup on the saved prefab.", MessageType.Warning);
            }

            if (!_selectedBundle.useOverflowMaskHosts)
            {
                EditorGUILayout.HelpBox("Turning off Overflow Mask Hosts removes overflow clipping and skips __ai_ContentMask generation.", MessageType.Info);
            }

            if (_selectedPage == null)
            {
                EditorGUILayout.HelpBox("\u5F53\u524D\u6CA1\u6709\u9009\u4E2D\u9875\u9762\u3002", MessageType.None);
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("\u9875\u9762", $"{_selectedPage.displayName} ({_selectedPage.pageId})");
            EditorGUILayout.LabelField("Prefab \u540D\u79F0", _selectedPage.prefabName);
            EditorGUILayout.LabelField("\u76EE\u6807\u5C42\u7EA7", _selectedPage.targetLayer.ToString());
            EditorGUILayout.LabelField("\u903B\u8F91\u8DEF\u5F84", string.IsNullOrWhiteSpace(_selectedPage.logicalPath) ? "-" : _selectedPage.logicalPath);
            DrawSelectedPageRuntimeConfig();
            EditorGUILayout.LabelField("\u9884\u89C8\u6302\u70B9", _previewMount != null ? _previewMount.name : "-");
            EditorGUILayout.LabelField(
                "\u5F53\u524D\u9884\u89C8\u72B6\u6001",
                HasLivePreviewForSelection()
                    ? "\u5DF2\u627E\u5230\u573A\u666F\u4E2D\u7684\u5F53\u524D\u9875\u9884\u89C8\uff0c\u5BFC\u51FA\u5F53\u524D\u9875\u65F6\u4F1A\u4EE5\u5F53\u524D\u5C42\u7EA7\u548C\u4FEE\u6539\u540E\u7684\u5185\u5BB9\u4E3A\u51C6"
                    : "\u5F53\u524D\u672A\u627E\u5230\u8BE5\u9875\u7684\u6D3B\u52A8\u9884\u89C8\uff0c\u5BFC\u51FA\u65F6\u4F1A\u6309 JSON \u7F16\u8BD1\u7ED3\u679C\u751F\u6210");
            EditorGUILayout.LabelField("\u5BFC\u51FA Prefab", _previewPage != null ? AIToUGUISiteBaker.BuildPrefabPath(_previewPage) : "-");
            EditorGUILayout.LabelField("\u5BFC\u51FA Metadata", _previewPage != null ? AIToUGUISiteBaker.BuildMetadataPath(_previewPage) : "-");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("\u5B9A\u4F4D Bundle", GUILayout.Width(100f)))
                {
                    EditorGUIUtility.PingObject(_selectedBundle);
                }

                if (GUILayout.Button("\u5B9A\u4F4D JSON", GUILayout.Width(100f)) && _selectedBundle.bundleJson != null)
                {
                    EditorGUIUtility.PingObject(_selectedBundle.bundleJson);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("\u5B9A\u4F4D Prefab", GUILayout.Width(100f)) && _previewPage != null)
                {
                    PingAsset(AIToUGUISiteBaker.BuildPrefabPath(_previewPage));
                }

                if (GUILayout.Button("\u5B9A\u4F4D Metadata", GUILayout.Width(100f)) && _previewPage != null)
                {
                    PingAsset(AIToUGUISiteBaker.BuildMetadataPath(_previewPage));
                }
            }
        }

        private void DrawSelectedPageRuntimeConfig()
        {
            if (_selectedBundle == null || _selectedPage == null)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("\u8FD0\u884C\u65F6\u914D\u7F6E", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var runtimePageId = EditorGUILayout.TextField("\u8FD0\u884C\u65F6 Page ID", _selectedPage.runtimePageId);
            var attachPanelComponent = EditorGUILayout.Toggle("\u6302\u8F7D Panel \u7EC4\u4EF6", _selectedPage.attachPanelComponent);
            var panelComponentTypeName = EditorGUILayout.TextField("Panel \u7EC4\u4EF6\u7C7B\u578B", _selectedPage.panelComponentTypeName);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selectedBundle, "Edit AIToUGUI Page Runtime Config");
                _selectedPage.runtimePageId = string.IsNullOrWhiteSpace(runtimePageId)
                    ? AIToUGUIRuntimePageIdUtility.BuildDefaultRuntimePageId(_selectedBundle.siteId, _selectedPage.pageId)
                    : runtimePageId.Trim();
                _selectedPage.attachPanelComponent = attachPanelComponent;
                _selectedPage.panelComponentTypeName = panelComponentTypeName?.Trim() ?? string.Empty;
                EditorUtility.SetDirty(_selectedBundle);
                RefreshSelection();
            }
        }

        private void DrawWarnings()
        {
            EditorGUILayout.LabelField("\u6821\u9A8C\u4FE1\u606F", EditorStyles.boldLabel);
            if (_previewPage == null)
            {
                if (_parsedBundle != null && _parsedBundle.Warnings.Count > 0)
                {
                    for (var i = 0; i < _parsedBundle.Warnings.Count; i++)
                    {
                        EditorGUILayout.HelpBox(_parsedBundle.Warnings[i], MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("\u5148\u6267\u884C\u4E00\u6B21\u6821\u9A8C\uff0c\u6216\u5207\u6362\u9875\u9762\u67E5\u770B\u5F53\u524D\u9875\u4FE1\u606F\u3002", MessageType.None);
                }

                return;
            }

            if (_parsedBundle != null && _parsedBundle.Warnings.Count > 0)
            {
                for (var i = 0; i < _parsedBundle.Warnings.Count; i++)
                {
                    EditorGUILayout.HelpBox(_parsedBundle.Warnings[i], MessageType.Warning);
                }

                EditorGUILayout.Space(4f);
            }

            if (_previewPage.Errors.Count > 0)
            {
                for (var i = 0; i < _previewPage.Errors.Count; i++)
                {
                    EditorGUILayout.HelpBox(_previewPage.Errors[i], MessageType.Error);
                }

                if (_previewPage.Warnings.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                }
            }

            if (_previewPage.Warnings.Count == 0)
            {
                if (_previewPage.Errors.Count == 0)
                {
                    EditorGUILayout.HelpBox("\u5F53\u524D\u9875\u9762\u6CA1\u6709\u8B66\u544A\u6216\u9519\u8BEF\u3002", MessageType.Info);
                }

                return;
            }

            for (var i = 0; i < _previewPage.Warnings.Count; i++)
            {
                EditorGUILayout.HelpBox(_previewPage.Warnings[i], MessageType.Warning);
            }
        }

        private void RefreshBundles()
        {
            _bundles.Clear();
            var guids = AssetDatabase.FindAssets("t:AIToUGUICompiledBundleDefinition");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var bundle = AssetDatabase.LoadAssetAtPath<AIToUGUICompiledBundleDefinition>(path);
                if (bundle != null)
                {
                    AIToUGUIBundleImportUtility.RefreshBundle(bundle, false);
                    _bundles.Add(bundle);
                }
            }

            _bundles.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.OrdinalIgnoreCase));
        }

        private void SelectBundle(AIToUGUICompiledBundleDefinition bundle)
        {
            _selectedBundle = bundle;
            _selectedPage = bundle != null && bundle.pages != null && bundle.pages.Count > 0 ? bundle.pages[0] : null;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (_selectedBundle == null)
            {
                ReplaceParsedBundle(null);
                _previewPage = null;
                Repaint();
                return;
            }

            ReplaceParsedBundle(AIToUGUIBundleBaker.Validate(_selectedBundle));
            UpdatePreviewPageFromSelection();
            Repaint();
        }

        private void UpdatePreviewPageFromSelection()
        {
            _previewPage = _parsedBundle != null && _selectedPage != null
                ? _parsedBundle.FindPage(_selectedPage.pageId)
                : null;
        }

        private void PreviewSelection()
        {
            if (_selectedBundle == null || _selectedPage == null || _previewMount == null)
            {
                return;
            }

            if (_parsedBundle == null)
            {
                ReplaceParsedBundle(AIToUGUIBundleBaker.Validate(_selectedBundle));
            }

            AIToUGUIBundleBaker.PreviewPage(_parsedBundle, _selectedPage.pageId, _previewMount);
            UpdatePreviewPageFromSelection();
            Repaint();
        }

        private void PrepareSvgAssetsForSelection()
        {
            if (_selectedBundle == null)
            {
                return;
            }

            if (_parsedBundle == null)
            {
                ReplaceParsedBundle(AIToUGUIBundleBaker.Validate(_selectedBundle));
            }

            if (_parsedBundle == null)
            {
                return;
            }

            var settings = new AIToUGUISvgSpriteImportSettings
            {
                targetResolution = _svgTargetResolution
            };
            var result = AIToUGUISvgSpriteAssetUtility.PrepareAssetsForParsedBundle(_parsedBundle, true, settings);
            AIToUGUISvgSpriteAssetUtility.LogResult(result, "SVG sprite rebuild");
            UpdatePreviewPageFromSelection();
            Repaint();
        }

        private void ReplaceParsedBundle(AIToUGUIParsedBundle parsedBundle)
        {
            DisposeParsedBundle();
            _parsedBundle = parsedBundle;
        }

        private void DisposeParsedBundle()
        {
            if (_parsedBundle != null)
            {
                _parsedBundle.Dispose();
                _parsedBundle = null;
            }
        }

        private static void PingAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        private bool HasLivePreviewForSelection()
        {
            if (_previewMount == null || _selectedPage == null || _selectedBundle == null)
            {
                return false;
            }

            var previews = _previewMount.GetComponentsInChildren<AIToUGUIPreviewInstance>(true);
            for (var i = 0; i < previews.Length; i++)
            {
                var preview = previews[i];
                if (preview == null)
                {
                    continue;
                }

                var pageRoot = preview.GetComponent<AIToUGUIPageRoot>();
                var resolvedPageId = !string.IsNullOrWhiteSpace(preview.pageId)
                    ? preview.pageId
                    : pageRoot != null ? pageRoot.pageId : string.Empty;
                var resolvedSiteId = !string.IsNullOrWhiteSpace(preview.siteId)
                    ? preview.siteId
                    : pageRoot != null ? pageRoot.siteId : string.Empty;

                if (string.Equals(resolvedPageId, _selectedPage.pageId, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(resolvedSiteId) || string.Equals(resolvedSiteId, _selectedBundle.siteId, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            var pageRoots = _previewMount.GetComponentsInChildren<AIToUGUIPageRoot>(true);
            for (var i = 0; i < pageRoots.Length; i++)
            {
                var pageRoot = pageRoots[i];
                if (pageRoot == null)
                {
                    continue;
                }

                if (string.Equals(pageRoot.pageId, _selectedPage.pageId, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(pageRoot.siteId) || string.Equals(pageRoot.siteId, _selectedBundle.siteId, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

#endif
