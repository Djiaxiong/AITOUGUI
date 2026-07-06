#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace AIToUGUI.Editor
{
    internal static class AIToUGUIBundleBaker
    {
        public static AIToUGUIParsedBundle Validate(AIToUGUICompiledBundleDefinition definition)
        {
            AIToUGUIBundleImportUtility.RefreshBundle(definition, false);
            return AIToUGUIBundleParser.Parse(definition);
        }

        public static AIToUGUIParsedBundle BakeBundle(AIToUGUICompiledBundleDefinition definition)
        {
            var parsed = Validate(definition);
            var runtimeErrors = new System.Collections.Generic.List<string>();
            if (!AIToUGUISiteBaker.ValidateRuntimePageIds(parsed.Pages, runtimeErrors))
            {
                parsed.Warnings.AddRange(runtimeErrors);
                return parsed;
            }

            for (var i = 0; i < parsed.Pages.Count; i++)
            {
                AIToUGUISiteBaker.BakeCompiledPage(parsed.Pages[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return parsed;
        }

        public static AIToUGUIParsedBundle BakePage(
            AIToUGUICompiledBundleDefinition definition,
            string pageId,
            AIToUGUIPreviewMount previewMount = null,
            bool preferPreviewHierarchy = false)
        {
            var parsed = Validate(definition);
            var runtimeErrors = new System.Collections.Generic.List<string>();
            if (!AIToUGUISiteBaker.ValidateRuntimePageIds(parsed.Pages, runtimeErrors))
            {
                parsed.Warnings.AddRange(runtimeErrors);
                return parsed;
            }

            var page = parsed.FindPage(pageId);
            if (page != null)
            {
                var bakedFromPreview = preferPreviewHierarchy && previewMount != null
                    ? AIToUGUISiteBaker.BakeCompiledPageFromPreview(page, previewMount)
                    : false;

                if (!bakedFromPreview)
                {
                    AIToUGUISiteBaker.BakeCompiledPage(page);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return parsed;
        }

        public static GameObject PreviewPage(AIToUGUIParsedBundle parsedBundle, string pageId, AIToUGUIPreviewMount mount)
        {
            if (parsedBundle == null)
            {
                return null;
            }

            var page = parsedBundle.FindPage(pageId);
            return page == null ? null : AIToUGUISiteBaker.PreviewCompiledPage(page, mount);
        }
    }
}

#endif
