using System;
using Newtonsoft.Json;
using UnityEngine;

namespace AIToUGUI.Lite
{
    public static class AIToUGUILiteBundleParser
    {
        public static AIToUGUILiteParsedBundle Parse(TextAsset jsonAsset)
        {
            var parsed = new AIToUGUILiteParsedBundle(jsonAsset);
            if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text))
            {
                parsed.AddWarning("Compiled bundle JSON is missing.");
                return parsed;
            }

            try
            {
                parsed.Bundle = JsonConvert.DeserializeObject<LiteCompiledSiteBundle>(jsonAsset.text);
            }
            catch (Exception exception)
            {
                parsed.AddWarning($"Compiled bundle parse failed: {exception.Message}");
                return parsed;
            }

            if (parsed.Bundle == null || parsed.Bundle.site == null)
            {
                parsed.AddWarning("Compiled bundle JSON is empty or missing site metadata.");
                return parsed;
            }

            if (parsed.Bundle.pages == null)
            {
                parsed.Bundle.pages = Array.Empty<LiteCompiledPageDto>();
            }

            if (parsed.Bundle.theme == null)
            {
                parsed.Bundle.theme = new LiteCompiledThemeDto();
            }

            NormalizeTheme(parsed.Bundle.theme);
            ScanPages(parsed);
            return parsed;
        }

        private static void NormalizeTheme(LiteCompiledThemeDto theme)
        {
            if (theme.tokens == null)
            {
                theme.tokens = Array.Empty<LiteCompiledThemeTokenDto>();
            }

            if (theme.visualPresets == null)
            {
                theme.visualPresets = Array.Empty<LiteCompiledVisualPresetDto>();
            }

            if (theme.motionPresets == null)
            {
                theme.motionPresets = Array.Empty<LiteCompiledMotionPresetDto>();
            }
        }

        private static void ScanPages(AIToUGUILiteParsedBundle parsed)
        {
            var pages = parsed.Bundle.pages;
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i];
                if (page == null)
                {
                    parsed.AddWarning($"Page at index {i} is null and will be skipped.");
                    continue;
                }

                if (page.root == null)
                {
                    parsed.AddWarning($"Page '{page.pageId}' has no root node and will be skipped.");
                    continue;
                }

                NormalizeNode(page.root);
            }
        }

        private static void NormalizeNode(LiteCompiledNodeDto node)
        {
            if (node == null)
            {
                return;
            }

            if (node.attributes == null)
            {
                node.attributes = Array.Empty<LiteCompiledAttributeDto>();
            }

            if (node.classes == null)
            {
                node.classes = Array.Empty<string>();
            }

            if (node.children == null)
            {
                node.children = Array.Empty<LiteCompiledNodeDto>();
            }

            if (node.layout == null)
            {
                node.layout = new LiteCompiledLayoutDto();
            }

            if (node.visual == null)
            {
                node.visual = new LiteCompiledVisualDto();
            }

            if (node.textStyle == null)
            {
                node.textStyle = new LiteCompiledTextStyleDto();
            }

            if (node.motion == null)
            {
                node.motion = new LiteCompiledMotionDto();
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                NormalizeNode(node.children[i]);
            }
        }
    }
}
