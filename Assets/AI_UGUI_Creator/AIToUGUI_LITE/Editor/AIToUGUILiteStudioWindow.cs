#if UNITY_EDITOR

using TMPro;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Lite
{
    public sealed class AIToUGUILiteStudioWindow : EditorWindow
    {
        [SerializeField] private TextAsset _bundleJsonAsset;
        [SerializeField] private AIToUGUILitePreviewMount _previewMount;
        [SerializeField] private TMP_FontAsset _defaultFontOverride;

        private AIToUGUILiteParsedBundle _parsedBundle;
        private int _selectedPageIndex = -1;
        private Vector2 _pageScroll;
        private Vector2 _warningScroll;

        [MenuItem("Tools/AIToUGUI/Lite Studio")]
        private static void Open()
        {
            var window = GetWindow<AIToUGUILiteStudioWindow>("AIToUGUI Lite");
            window.minSize = new Vector2(980f, 680f);
        }

        private void OnEnable()
        {
            if (_previewMount == null)
            {
                _previewMount = FindObjectOfType<AIToUGUILitePreviewMount>();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Lite Preview Workflow", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Import a compiled_site_bundle.json, choose a Canvas preview mount, then build clean UGUI/TMP page previews.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6f);

                _bundleJsonAsset = (TextAsset)EditorGUILayout.ObjectField("Bundle JSON", _bundleJsonAsset, typeof(TextAsset), false);
                _previewMount = (AIToUGUILitePreviewMount)EditorGUILayout.ObjectField("Preview Mount", _previewMount, typeof(AIToUGUILitePreviewMount), true);
                _defaultFontOverride = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP Font Override", _defaultFontOverride, typeof(TMP_FontAsset), false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_bundleJsonAsset == null))
                    {
                        if (GUILayout.Button("Parse JSON", GUILayout.Width(120f)))
                        {
                            ParseCurrentBundle();
                        }
                    }

                    using (new EditorGUI.DisabledScope(_bundleJsonAsset == null || _previewMount == null))
                    {
                        if (GUILayout.Button("Parse All", GUILayout.Width(120f)))
                        {
                            ParseAllPages();
                        }

                        if (GUILayout.Button("Parse Selected", GUILayout.Width(120f)))
                        {
                            ParseSelectedPage();
                        }
                    }

                    using (new EditorGUI.DisabledScope(_previewMount == null))
                    {
                        if (GUILayout.Button("Clear Preview", GUILayout.Width(120f)))
                        {
                            var cleared = AIToUGUILitePreviewBuilder.Clear(_previewMount);
                            Debug.Log($"[AIToUGUI Lite] Cleared {cleared} preview root(s).");
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPageList();
                DrawWarnings();
            }

        }

        private void DrawPageList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.45f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("Pages", EditorStyles.boldLabel);
                if (_parsedBundle == null || !_parsedBundle.IsValid)
                {
                    EditorGUILayout.HelpBox("Parse a bundle JSON to inspect available pages.", MessageType.Info);
                    return;
                }

                _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll);
                for (var i = 0; i < _parsedBundle.Bundle.pages.Length; i++)
                {
                    var page = _parsedBundle.Bundle.pages[i];
                    if (page == null)
                    {
                        continue;
                    }

                    var label = $"{(string.IsNullOrWhiteSpace(page.displayName) ? page.pageId : page.displayName)} [{page.pageId}]";
                    var selected = _selectedPageIndex == i;
                    if (GUILayout.Toggle(selected, label, "Button"))
                    {
                        _selectedPageIndex = i;
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawWarnings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);
                if (_parsedBundle == null)
                {
                    EditorGUILayout.HelpBox("No parsed bundle yet.", MessageType.None);
                    return;
                }

                if (_parsedBundle.Warnings.Count == 0)
                {
                    EditorGUILayout.HelpBox("No warnings. Bundle is ready for lite preview generation.", MessageType.Info);
                    return;
                }

                _warningScroll = EditorGUILayout.BeginScrollView(_warningScroll);
                for (var i = 0; i < _parsedBundle.Warnings.Count; i++)
                {
                    EditorGUILayout.HelpBox(_parsedBundle.Warnings[i], MessageType.Warning);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void ParseCurrentBundle()
        {
            _parsedBundle = AIToUGUILiteBundleParser.Parse(_bundleJsonAsset);
            _selectedPageIndex = _parsedBundle != null && _parsedBundle.PageCount > 0 ? 0 : -1;
        }

        private void ParseAllPages()
        {
            EnsureParsedBundle();
            if (_parsedBundle == null || !_parsedBundle.IsValid || _previewMount == null)
            {
                return;
            }

            var result = AIToUGUILitePreviewBuilder.BuildAll(_parsedBundle, _previewMount, CreateBuildOptions());
            Debug.Log($"[AIToUGUI Lite] Built {result.builtPageCount} page preview(s).");
            Selection.activeGameObject = result.builtRoots.Count > 0 ? result.builtRoots[0] : _previewMount.gameObject;
        }

        private void ParseSelectedPage()
        {
            EnsureParsedBundle();
            if (_parsedBundle == null || !_parsedBundle.IsValid || _previewMount == null)
            {
                return;
            }

            if (_selectedPageIndex < 0 || _selectedPageIndex >= _parsedBundle.Bundle.pages.Length)
            {
                Debug.LogWarning("[AIToUGUI Lite] No page is selected.");
                return;
            }

            if (_previewMount.clearBeforePreview)
            {
                AIToUGUILitePreviewBuilder.Clear(_previewMount);
            }

            var root = AIToUGUILitePreviewBuilder.BuildPage(
                _parsedBundle,
                _parsedBundle.Bundle.pages[_selectedPageIndex],
                _previewMount,
                CreateBuildOptions());
            if (root != null)
            {
                Debug.Log($"[AIToUGUI Lite] Built page '{root.name}'.");
                Selection.activeGameObject = root;
            }
        }

        private AIToUGUILitePreviewBuildOptions CreateBuildOptions()
        {
            return new AIToUGUILitePreviewBuildOptions
            {
                defaultFontOverride = _defaultFontOverride
            };
        }

        private void EnsureParsedBundle()
        {
            if (_bundleJsonAsset == null)
            {
                _parsedBundle = null;
                _selectedPageIndex = -1;
                return;
            }

            if (_parsedBundle == null || _parsedBundle.SourceAsset != _bundleJsonAsset)
            {
                ParseCurrentBundle();
                return;
            }

            _parsedBundle = AIToUGUILiteBundleParser.Parse(_bundleJsonAsset);
        }

    }
}

#endif
