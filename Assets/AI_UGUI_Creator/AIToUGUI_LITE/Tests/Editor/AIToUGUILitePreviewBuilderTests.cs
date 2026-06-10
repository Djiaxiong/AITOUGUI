#if UNITY_EDITOR

using System.IO;
using AIToUGUI.Lite;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AIToUGUI.Lite.Tests
{
    public sealed class AIToUGUILitePreviewBuilderTests
    {
        private const string SampleBundlePath = "Assets/AI_UGUI_Creator/Cases/DEV_Test/ArcadeFighterUI/compiled_site_bundle.json";
        private GameObject _canvasGo;
        private AIToUGUILitePreviewMount _mount;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("LitePreviewCanvas", typeof(RectTransform), typeof(Canvas));
            var rootRect = _canvasGo.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1920f, 1080f);

            var mountGo = new GameObject("PreviewMount", typeof(RectTransform), typeof(AIToUGUILitePreviewMount));
            var mountRect = mountGo.GetComponent<RectTransform>();
            mountRect.SetParent(rootRect, false);
            mountRect.anchorMin = Vector2.zero;
            mountRect.anchorMax = Vector2.one;
            mountRect.offsetMin = Vector2.zero;
            mountRect.offsetMax = Vector2.zero;
            _mount = mountGo.GetComponent<AIToUGUILitePreviewMount>();
            _mount.clearBeforePreview = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null)
            {
                Object.DestroyImmediate(_canvasGo);
            }
        }

        [Test]
        public void BuildAll_MultiPageBundle_GeneratesExpectedPageRootsWithoutFullVersionComponents()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SampleBundlePath);
            Assert.That(asset, Is.Not.Null, "Sample compiled bundle JSON is missing.");
            var parsed = AIToUGUILiteBundleParser.Parse(asset);

            var fileCountBefore = Directory.GetFiles(Path.Combine(Application.dataPath, "AI_UGUI_Creator", "AIToUGUI_lite"), "*.*", SearchOption.AllDirectories).Length;
            var result = AIToUGUILitePreviewBuilder.BuildAll(parsed, _mount, new AIToUGUILitePreviewBuildOptions());
            var fileCountAfter = Directory.GetFiles(Path.Combine(Application.dataPath, "AI_UGUI_Creator", "AIToUGUI_lite"), "*.*", SearchOption.AllDirectories).Length;

            Assert.That(result.builtPageCount, Is.EqualTo(parsed.PageCount));
            Assert.That(_mount.transform.childCount, Is.EqualTo(parsed.PageCount));
            Assert.That(fileCountAfter, Is.EqualTo(fileCountBefore), "Lite preview build should not write new files.");

            var allComponents = _mount.GetComponentsInChildren<Component>(true);
            foreach (var component in allComponents)
            {
                Assert.That(component, Is.Not.Null);
                var fullName = component.GetType().FullName ?? string.Empty;
                Assert.That(fullName, Does.Not.Contain("LeTai.TrueShadow"));
                Assert.That(fullName, Does.Not.Contain("DTT."));
                Assert.That(fullName, Does.Not.Contain("Windinator"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIShadowEffect"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIShapeAdapter"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIAnimationBinder"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIViewPanel"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIPageRoot"));
            }
        }

        [Test]
        public void BuildPage_WithUnsupportedFields_BuildsCleanPreviewHierarchy()
        {
            const string json =
@"{
  ""site"": { ""siteId"": ""test_site"", ""displayName"": ""Test Site"", ""designWidth"": 1920, ""designHeight"": 1080 },
  ""theme"": { ""textPrimary"": ""#ffffff"", ""accentColor"": ""#ff8800"" },
  ""pages"": [
    {
      ""pageId"": ""page_a"",
      ""displayName"": ""Page A"",
      ""root"": {
        ""name"": ""Root"",
        ""tag"": ""div"",
        ""controlType"": ""Div"",
        ""shapeId"": ""banner"",
        ""layout"": { ""width"": ""1920px"", ""height"": ""1080px"" },
        ""visual"": { ""fillColor"": ""#101010"", ""boxShadow"": ""0 0 12px rgba(0,0,0,0.5)"", ""enableGlow"": true, ""glowColor"": ""#00ffff"", ""glowBlur"": 12 },
        ""motion"": { ""enterMotion"": ""Fade"", ""duration"": 0.2, ""distance"": 20, ""scale"": 0.95 },
        ""children"": [
          {
            ""name"": ""Title"",
            ""tag"": ""div"",
            ""controlType"": ""Text"",
            ""text"": ""HELLO"",
            ""layout"": { ""left"": ""100px"", ""top"": ""100px"", ""width"": ""400px"", ""height"": ""80px"" },
            ""textStyle"": { ""color"": ""#ffffff"", ""fontSize"": ""32px"" },
            ""children"": []
          }
        ]
      }
    }
  ]
}";
            var parsed = AIToUGUILiteBundleParser.Parse(new TextAsset(json));
            var root = AIToUGUILitePreviewBuilder.BuildPage(parsed, parsed.Bundle.pages[0], _mount, new AIToUGUILitePreviewBuildOptions());

            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetComponent<AIToUGUILitePageRoot>(), Is.Not.Null);
            Assert.That(root.GetComponent<AIToUGUILitePreviewInstance>(), Is.Not.Null);
            Assert.That(root.GetComponentInChildren<TMPro.TextMeshProUGUI>(true), Is.Not.Null);
            Assert.That(root.GetComponentInChildren<Button>(true), Is.Null);

            var allComponents = root.GetComponentsInChildren<Component>(true);
            foreach (var component in allComponents)
            {
                Assert.That(component, Is.Not.Null);
                var fullName = component.GetType().FullName ?? string.Empty;
                Assert.That(fullName, Does.Not.Contain("LeTai.TrueShadow"));
                Assert.That(fullName, Does.Not.Contain("DTT."));
                Assert.That(fullName, Does.Not.Contain("Windinator"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIShadowEffect"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIShapeAdapter"));
                Assert.That(fullName, Does.Not.Contain("AIToUGUIAnimationBinder"));
            }
        }
    }
}

#endif
