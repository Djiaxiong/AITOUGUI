using System;
using System.Collections.Generic;

namespace AIToUGUI
{
    public static class AIToUGUIElementContractUtility
    {
        public const string DefaultVariantId = "default";
        public const string DefaultComponentVariantId = "default";
        public const string ProceduralRenderStrategyId = "procedural";
        public const string HybridRenderStrategyId = "hybrid";
        public const string RasterRenderStrategyId = "raster";

        private static readonly HashSet<string> PrimitiveElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "button",
            "input",
            "toggle",
            "slider",
            "dropdown",
            "scrollbar",
            "scrollview",
            "image",
            "progress"
        };

        private static readonly HashSet<string> CompositeComponentFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "frame/window",
            "button/compound",
            "card/item",
            "header/section",
            "list/row",
            "nav/tab"
        };

        public static bool IsPrimitiveElement(string elementId)
        {
            return !string.IsNullOrWhiteSpace(elementId) && PrimitiveElementIds.Contains(elementId.Trim());
        }

        public static bool RequiresExplicitElementMarker(AIToUGUIControlType controlType)
        {
            switch (controlType)
            {
                case AIToUGUIControlType.Button:
                case AIToUGUIControlType.Input:
                case AIToUGUIControlType.Scroll:
                case AIToUGUIControlType.Scrollbar:
                case AIToUGUIControlType.Toggle:
                case AIToUGUIControlType.Slider:
                case AIToUGUIControlType.Dropdown:
                case AIToUGUIControlType.Image:
                case AIToUGUIControlType.Progress:
                    return true;
                default:
                    return false;
            }
        }

        public static string NormalizeVariantId(string variantId)
        {
            return string.IsNullOrWhiteSpace(variantId) ? DefaultVariantId : variantId.Trim();
        }

        public static string NormalizeComponentFamily(string componentFamily)
        {
            return string.IsNullOrWhiteSpace(componentFamily)
                ? string.Empty
                : componentFamily.Trim().ToLowerInvariant();
        }

        public static string NormalizeComponentVariantId(string componentVariant)
        {
            return string.IsNullOrWhiteSpace(componentVariant)
                ? DefaultComponentVariantId
                : componentVariant.Trim().ToLowerInvariant();
        }

        public static bool IsKnownCompositeComponentFamily(string componentFamily)
        {
            return !string.IsNullOrWhiteSpace(componentFamily) &&
                   CompositeComponentFamilies.Contains(NormalizeComponentFamily(componentFamily));
        }

        public static string NormalizeRenderStrategy(string renderStrategy)
        {
            if (string.IsNullOrWhiteSpace(renderStrategy))
            {
                return ProceduralRenderStrategyId;
            }

            return renderStrategy.Trim().ToLowerInvariant() switch
            {
                "hybrid" => HybridRenderStrategyId,
                "raster" => RasterRenderStrategyId,
                _ => ProceduralRenderStrategyId
            };
        }

        public static string NormalizeAssetTypeId(string assetType)
        {
            if (string.IsNullOrWhiteSpace(assetType))
            {
                return nameof(AIToUGUIAssetType.Icon);
            }

            return assetType.Trim().ToLowerInvariant() switch
            {
                "ornament" => nameof(AIToUGUIAssetType.Ornament),
                "snapshot" => nameof(AIToUGUIAssetType.Snapshot),
                "frame" => nameof(AIToUGUIAssetType.Frame),
                "background" => nameof(AIToUGUIAssetType.Background),
                _ => nameof(AIToUGUIAssetType.Icon)
            };
        }

        public static string NormalizeAssetImportModeId(string importMode)
        {
            if (string.IsNullOrWhiteSpace(importMode))
            {
                return nameof(AIToUGUIAssetImportMode.Auto);
            }

            return importMode.Trim().ToLowerInvariant() switch
            {
                "sprite" => nameof(AIToUGUIAssetImportMode.Sprite),
                "nineslice" => nameof(AIToUGUIAssetImportMode.NineSlice),
                "nine-slice" => nameof(AIToUGUIAssetImportMode.NineSlice),
                "tile" => nameof(AIToUGUIAssetImportMode.Tile),
                "readonlyoverlay" => nameof(AIToUGUIAssetImportMode.ReadOnlyOverlay),
                "read-only-overlay" => nameof(AIToUGUIAssetImportMode.ReadOnlyOverlay),
                _ => nameof(AIToUGUIAssetImportMode.Auto)
            };
        }

        public static string BuildElementKey(string elementId, string variantId)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return string.Empty;
            }

            var normalizedElementId = elementId.Trim();
            if (!IsPrimitiveElement(normalizedElementId))
            {
                return normalizedElementId;
            }

            return $"{normalizedElementId}/{NormalizeVariantId(variantId)}";
        }

        public static bool TryNormalizeElementIdentity(string rawElementId, string rawVariantId, out string elementId, out string variantId)
        {
            var originalElementId = string.IsNullOrWhiteSpace(rawElementId) ? string.Empty : rawElementId.Trim();
            elementId = originalElementId;
            variantId = string.IsNullOrWhiteSpace(rawVariantId) ? string.Empty : rawVariantId.Trim();
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return false;
            }

            if (IsPrimitiveElement(elementId))
            {
                variantId = NormalizeVariantId(variantId);
                return true;
            }

            var slashIndex = elementId.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= elementId.Length - 1)
            {
                return false;
            }

            var candidateElementId = elementId.Substring(0, slashIndex).Trim();
            if (!IsPrimitiveElement(candidateElementId))
            {
                return false;
            }

            elementId = candidateElementId;
            variantId = NormalizeVariantId(string.IsNullOrWhiteSpace(variantId)
                ? originalElementId.Substring(slashIndex + 1)
                : variantId);
            return true;
        }

        public static AIToUGUIControlType InferPrimitiveControlType(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return AIToUGUIControlType.Div;
            }

            return elementId.Trim().ToLowerInvariant() switch
            {
                "button" => AIToUGUIControlType.Button,
                "input" => AIToUGUIControlType.Input,
                "toggle" => AIToUGUIControlType.Toggle,
                "slider" => AIToUGUIControlType.Slider,
                "dropdown" => AIToUGUIControlType.Dropdown,
                "scrollbar" => AIToUGUIControlType.Scrollbar,
                "scrollview" => AIToUGUIControlType.Scroll,
                "image" => AIToUGUIControlType.Image,
                "progress" => AIToUGUIControlType.Progress,
                _ => AIToUGUIControlType.Div
            };
        }

        public static string[] GetDefaultSlots(string elementId)
        {
            return string.IsNullOrWhiteSpace(elementId)
                ? Array.Empty<string>()
                : elementId.Trim().ToLowerInvariant() switch
                {
                    "button" => new[] { "Label", "Icon", "Graphic", "Button" },
                    "input" => new[] { "InputField", "Text", "Placeholder", "Icon" },
                    "toggle" => new[] { "Toggle", "Background", "Checkmark", "Label" },
                    "slider" => new[] { "Slider", "Track", "Fill", "Handle" },
                    "progress" => new[] { "Track", "Fill", "Label" },
                    "dropdown" => new[] { "Dropdown", "CaptionText", "Arrow", "TemplateRoot", "ItemLabel" },
                    "scrollbar" => new[] { "Scrollbar", "Track", "Handle" },
                    "scrollview" => new[] { "ScrollRect", "Viewport", "Content", "VerticalScrollbar", "HorizontalScrollbar" },
                    "image" => new[] { "Graphic" },
                    _ => Array.Empty<string>()
                };
        }

        public static string[] GetDefaultSlotsForComponentFamily(string componentFamily)
        {
            return NormalizeComponentFamily(componentFamily) switch
            {
                "frame/window" => new[] { "Background", "Header", "Content", "Footer", "Decoration" },
                "button/compound" => new[] { "Button", "Graphic", "Icon", "Content", "Label", "SecondaryText", "Badge", "Decoration" },
                "card/item" => new[] { "Graphic", "Icon", "Content", "PrimaryText", "SecondaryText", "Badge", "Footer", "Decoration" },
                "header/section" => new[] { "Graphic", "Icon", "Title", "Subtitle", "Action", "Decoration" },
                "list/row" => new[] { "Graphic", "Leading", "Content", "Trailing", "Badge", "Decoration" },
                "nav/tab" => new[] { "Button", "Graphic", "Indicator", "Icon", "Label", "Badge", "Decoration" },
                _ => Array.Empty<string>()
            };
        }
    }
}
