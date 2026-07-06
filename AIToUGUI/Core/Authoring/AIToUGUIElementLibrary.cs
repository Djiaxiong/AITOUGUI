using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [CreateAssetMenu(fileName = "AIToUGUIElementLibrary", menuName = "AIToUGUI/Element Library")]
    public sealed class AIToUGUIElementLibrary : ScriptableObject
    {
        [Title("Element Templates")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIElementTemplate> templates = new List<AIToUGUIElementTemplate>();

        public AIToUGUIElementTemplate ResolveTemplateByTemplateId(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return null;
            }

            var normalizedTemplateId = templateId.Trim();
            for (var i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                if (template != null &&
                    string.Equals(template.templateId, normalizedTemplateId, StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            return null;
        }

        public AIToUGUIElementTemplate ResolveTemplateByComponent(string componentFamily, string componentVariant = null)
        {
            var normalizedFamily = AIToUGUIElementContractUtility.NormalizeComponentFamily(componentFamily);
            if (string.IsNullOrWhiteSpace(normalizedFamily))
            {
                return null;
            }

            var normalizedVariant = AIToUGUIElementContractUtility.NormalizeComponentVariantId(componentVariant);
            for (var i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                if (template == null ||
                    !string.Equals(template.componentFamily, normalizedFamily, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(
                        AIToUGUIElementContractUtility.NormalizeComponentVariantId(template.componentVariant),
                        normalizedVariant,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            for (var i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                if (template == null ||
                    !string.Equals(template.componentFamily, normalizedFamily, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(template.componentVariant) ||
                    string.Equals(
                        AIToUGUIElementContractUtility.NormalizeComponentVariantId(template.componentVariant),
                        AIToUGUIElementContractUtility.DefaultComponentVariantId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            return null;
        }

        public AIToUGUIElementTemplate ResolveTemplate(string elementId, string variantId = null)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return null;
            }

            var normalizedElementId = elementId.Trim();
            var normalizedVariantId = AIToUGUIElementContractUtility.NormalizeVariantId(variantId);

            for (var i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                if (template == null)
                {
                    continue;
                }

                if (string.Equals(template.elementId, normalizedElementId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!AIToUGUIElementContractUtility.IsPrimitiveElement(normalizedElementId))
                    {
                        return template;
                    }

                    if (string.Equals(
                            AIToUGUIElementContractUtility.NormalizeVariantId(template.variantId),
                            normalizedVariantId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return template;
                    }
                }

                var combinedKey = AIToUGUIElementContractUtility.BuildElementKey(template.elementId, template.variantId);
                if (string.Equals(combinedKey, normalizedElementId, StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            return null;
        }
    }
}
