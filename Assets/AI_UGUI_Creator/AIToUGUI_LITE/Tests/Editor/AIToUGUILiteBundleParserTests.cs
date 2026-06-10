#if UNITY_EDITOR

using AIToUGUI.Lite;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Lite.Tests
{
    public sealed class AIToUGUILiteBundleParserTests
    {
        private const string SampleBundlePath = "Assets/AI_UGUI_Creator/Cases/DEV_Test/ArcadeFighterUI/compiled_site_bundle.json";

        [Test]
        public void Parse_ValidCompiledBundle_ReadsSiteAndPages()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SampleBundlePath);
            Assert.That(asset, Is.Not.Null, "Sample compiled bundle JSON is missing.");

            var parsed = AIToUGUILiteBundleParser.Parse(asset);

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.IsValid, Is.True);
            Assert.That(parsed.Bundle.site.siteId, Is.EqualTo("arcade_fighter_ui"));
            Assert.That(parsed.PageCount, Is.GreaterThan(1));
            Assert.That(parsed.Bundle.pages[0].root, Is.Not.Null);
        }

        [Test]
        public void Parse_UnsupportedEffects_EmitWarnings()
        {
            const string json =
@"{
  ""site"": { ""siteId"": ""test_site"", ""displayName"": ""Test Site"", ""designWidth"": 1920, ""designHeight"": 1080 },
  ""theme"": { ""motionPresets"": [ { ""presetId"": ""motion/default"", ""enterMotion"": ""Fade"", ""hoverMotion"": ""HoverLift"", ""pressMotion"": ""ScaleIn"", ""duration"": 0.2, ""distance"": 20, ""scale"": 0.95 } ] },
  ""pages"": [
    {
      ""pageId"": ""page_a"",
      ""displayName"": ""Page A"",
      ""root"": {
        ""name"": ""Root"",
        ""tag"": ""div"",
        ""controlType"": ""Div"",
        ""shapeId"": ""banner"",
        ""motionId"": ""motion/button"",
        ""layout"": { ""width"": ""1920px"", ""height"": ""1080px"" },
        ""visual"": {
          ""fillColor"": ""#101010"",
          ""useGradient"": true,
          ""gradientColor"": ""#ffffff"",
          ""cornerRadius"": 18,
          ""outlineWidth"": 2,
          ""outlineColor"": ""#ff00ff"",
          ""boxShadow"": ""0 0 12px rgba(0,0,0,0.5)"",
          ""enableGlow"": true,
          ""glowColor"": ""#00ffff"",
          ""glowBlur"": 12
        },
        ""motion"": { ""enterMotion"": ""Fade"", ""duration"": 0.2, ""distance"": 20, ""scale"": 0.95 },
        ""textStyle"": { ""color"": ""#ffffff"", ""fontSize"": ""24px"" },
        ""children"": []
      }
    }
  ]
}";
            var parsed = AIToUGUILiteBundleParser.Parse(new TextAsset(json));

            Assert.That(parsed.IsValid, Is.True);
            Assert.That(parsed.Warnings.Count, Is.GreaterThan(0));
            Assert.That(string.Join("\n", parsed.Warnings), Does.Contain("ignores"));
        }
    }
}

#endif
