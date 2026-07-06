using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AIToUGUI
{
    internal sealed class CompiledVector4JsonConverter : JsonConverter<Vector4>
    {
        public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return Vector4.zero;
            }

            var token = JToken.Load(reader);
            return token.Type switch
            {
                JTokenType.Array => ReadFromArray(token as JArray),
                JTokenType.Object => ReadFromObject(token as JObject),
                _ => Vector4.zero
            };
        }

        public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.x);
            writer.WriteValue(value.y);
            writer.WriteValue(value.z);
            writer.WriteValue(value.w);
            writer.WriteEndArray();
        }

        private static Vector4 ReadFromArray(JArray array)
        {
            if (array == null || array.Count < 4)
            {
                return Vector4.zero;
            }

            return new Vector4(
                ReadFloat(array, 0),
                ReadFloat(array, 1),
                ReadFloat(array, 2),
                ReadFloat(array, 3));
        }

        private static Vector4 ReadFromObject(JObject obj)
        {
            if (obj == null)
            {
                return Vector4.zero;
            }

            return new Vector4(
                ReadFloat(obj, "x"),
                ReadFloat(obj, "y"),
                ReadFloat(obj, "z"),
                ReadFloat(obj, "w"));
        }

        private static float ReadFloat(JArray array, int index)
        {
            if (array == null || index < 0 || index >= array.Count)
            {
                return 0f;
            }

            var token = array[index];
            return token != null && token.Type is JTokenType.Integer or JTokenType.Float
                ? token.Value<float>()
                : 0f;
        }

        private static float ReadFloat(JObject obj, string key)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key))
            {
                return 0f;
            }

            var token = obj[key];
            return token != null && token.Type is JTokenType.Integer or JTokenType.Float
                ? token.Value<float>()
                : 0f;
        }
    }

    [Serializable]
    public sealed class CompiledSiteBundle
    {
        public string schemaVersion = "1.1";
        public bool compiledWithForce;
        public CompiledSiteDto site = new CompiledSiteDto();
        public CompiledThemeDto theme = new CompiledThemeDto();
        public CompiledBundleAssetDto[] assets = Array.Empty<CompiledBundleAssetDto>();
        public CompiledBundleDowngradeDto[] downgrades = Array.Empty<CompiledBundleDowngradeDto>();
        public CompiledPageDto[] pages = Array.Empty<CompiledPageDto>();
    }

    [Serializable]
    public sealed class CompiledSiteDto
    {
        public string siteId = "site_id";
        public string displayName = "Compiled Site";
        public int designWidth = 1920;
        public int designHeight = 1080;
        public string prefabOutputRoot = AIToUGUIGeneratedAssetPaths.GetPrefabsRoot("site_id");
        public string metadataOutputRoot = AIToUGUIGeneratedAssetPaths.GetMetadataRoot("site_id");
    }

    [Serializable]
    public sealed class CompiledThemeDto
    {
        public string themeId = "compiled_theme";
        public string displayName = "Compiled Theme";
        public string pageBackground = "#101010";
        public string panelFill = "#1e1e1e";
        public string cardFill = "#262626";
        public string buttonFill = "#333333";
        public string accentColor = "#f0c550";
        public string textPrimary = "#ffffff";
        public string textSecondary = "#c0c0c0";
        public string outlineColor = "rgba(255,255,255,0.12)";
        public string shadowColor = "rgba(0,0,0,0.25)";
        public float panelRadius;
        public float cardRadius;
        public float buttonRadius;
        public CompiledThemeTokenDto[] tokens = Array.Empty<CompiledThemeTokenDto>();
        public CompiledVisualPresetDto[] visualPresets = Array.Empty<CompiledVisualPresetDto>();
        public CompiledMotionPresetDto[] motionPresets = Array.Empty<CompiledMotionPresetDto>();
        public CompiledLoopMotionPresetDto[] loopMotionPresets = Array.Empty<CompiledLoopMotionPresetDto>();
    }

    [Serializable]
    public sealed class CompiledThemeTokenDto
    {
        public string tokenId;
        public string value;
    }

    [Serializable]
    public sealed class CompiledVisualPresetDto
    {
        public string presetId;
        public bool enableFill = true;
        public string fillColor = "#ffffff";
        public bool useGradient;
        public string gradientColor = "#ffffff";
        public string gradientDirection = nameof(AIToUGUIGradientDirection.None);
        public float cornerRadius;
        public bool useMaxRoundness;
        public float outlineWidth;
        public string outlineColor = "rgba(255,255,255,0.12)";
        public float shadowOffsetX;
        public float shadowOffsetY;
        public float shadowBlur;
        public string shadowColor = "rgba(0,0,0,0.25)";
        public bool enableGlow;
        public string glowColor = "rgba(255,255,255,0.25)";
        public float glowBlur;
        public float glowIntensity = 1f;
    }

    [Serializable]
    public sealed class CompiledMotionPresetDto
    {
        public string presetId = "motion/default";
        public string enterMotion = nameof(AIToUGUIMotionType.Fade);
        public string hoverMotion = nameof(AIToUGUIMotionType.HoverLift);
        public string pressMotion = nameof(AIToUGUIMotionType.ScaleIn);
        public float duration = 0.22f;
        public float distance = 26f;
        public float scale = 0.96f;
        public string ease = "OutCubic";
    }

    [Serializable]
    public sealed class CompiledLoopMotionPresetDto
    {
        public string presetId = "loop/rotate-slow";
        public string loopType = nameof(AIToUGUILoopMotionType.Rotate);
        public float duration = 12f;
        public float amplitude = 1f;
        public string ease = "Linear";
    }

    [Serializable]
    public sealed class CompiledPageDto
    {
        public string pageId = "page_id";
        public string runtimePageId;
        public string displayName = "Page";
        public string sourceRelativePath;
        public string prefabName = "CompiledPage";
        public string targetLayer = "Normal";
        public string logicalPath = "UI/Generated/Site/CompiledPage";
        public CompiledNodeDto root = new CompiledNodeDto();
    }

    [Serializable]
    public sealed class CompiledNodeDto
    {
        public string name;
        public string tag = "div";
        public string controlType = nameof(AIToUGUIControlType.Div);
        public string role;
        public string elementId;
        public string variantId;
        public string shapeId;
        public string frameId;
        public string slotId;
        public string containerId;
        public string templateId;
        public string componentFamily;
        public string componentVariant;
        public bool hasExplicitTemplateId;
        public bool hasExplicitComponentFamily;
        public bool hasExplicitCompositeElement;
        public string renderStrategy = nameof(AIToUGUIRenderStrategy.Procedural);
        public string motionId;
        public string text;
        public string stabilityLevel = "suggested";
        public CompiledAbsoluteRectDto absoluteRect = new CompiledAbsoluteRectDto();
        public CompiledNodeAssetRefDto[] assetRefs = Array.Empty<CompiledNodeAssetRefDto>();
        public string[] fidelityNotes = Array.Empty<string>();
        public string[] classes = Array.Empty<string>();
        public CompiledAttributeDto[] attributes = Array.Empty<CompiledAttributeDto>();
        public CompiledLayoutDto layout = new CompiledLayoutDto();
        public CompiledVisualDto visual = new CompiledVisualDto();
        public CompiledTextStyleDto textStyle = new CompiledTextStyleDto();
        public CompiledMotionDto motion = new CompiledMotionDto();
        public CompiledNodeDto[] children = Array.Empty<CompiledNodeDto>();
    }

    [Serializable]
    public sealed class CompiledAbsoluteRectDto
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public bool measured;
        public string source;
    }

    [Serializable]
    public sealed class CompiledAttributeDto
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class CompiledNodeAssetRefDto
    {
        public string assetId;
        public string assetType = nameof(AIToUGUIAssetType.Icon);
        public string usage;
        public string importMode = nameof(AIToUGUIAssetImportMode.Auto);
        public string source;
        public string notes;
        public string logicalAssetPath;
        [JsonConverter(typeof(CompiledVector4JsonConverter))]
        public Vector4 sliceBorder;
        public float pixelsPerUnit = 100f;
        public float preferredWidth;
        public float preferredHeight;
        public string tintPolicy;
        public string atlasGroup;
    }

    [Serializable]
    public sealed class CompiledBundleAssetDto
    {
        public string assetId;
        public string assetType = nameof(AIToUGUIAssetType.Icon);
        public string usage;
        public string importMode = nameof(AIToUGUIAssetImportMode.Auto);
        public string source;
        public string[] linkedNodeNames = Array.Empty<string>();
        public string notes;
        public string logicalAssetPath;
        [JsonConverter(typeof(CompiledVector4JsonConverter))]
        public Vector4 sliceBorder;
        public float pixelsPerUnit = 100f;
        public float preferredWidth;
        public float preferredHeight;
        public string tintPolicy;
        public string atlasGroup;
    }

    [Serializable]
    public sealed class CompiledBundleDowngradeDto
    {
        public string feature;
        public string action;
        public string location;
        public string selector;
        public string message;
    }

    [Serializable]
    public sealed class CompiledLayoutDto
    {
        public string display;
        public string layoutMode;
        public string position;
        public string anchorHorizontal;
        public string anchorVertical;
        public string left;
        public string right;
        public string top;
        public string bottom;
        public string width;
        public string height;
        public string minWidth;
        public string maxWidth;
        public string minHeight;
        public string maxHeight;
        public string padding;
        public string margin;
        public string marginLeft;
        public string marginRight;
        public string marginTop;
        public string marginBottom;
        public string flex;
        public string flexGrow;
        public string flexShrink;
        public string gap;
        public string justifyContent;
        public string alignItems;
        public string flexDirection;
        public string flexWrap;
        public string overflow;
        public string overflowX;
        public string overflowY;
        public string boxSizing;
        public string translateX;
        public string translateY;
        public float rotationZ;
        public CompiledGridLayoutDto gridLayout;
        public CompiledCurveLayoutDto curveLayout;
    }

    [Serializable]
    public sealed class CompiledGridLayoutDto
    {
        public int columns;
        public int rows;
        public int layers = 1;
        public string cellType;
        public string cellWidth;
        public string cellHeight;
        public string gapX;
        public string gapY;
        public string columnDirection;
        public string rowDirection;
        public string horizontalAlign;
        public string verticalAlign;
    }

    [Serializable]
    public sealed class CompiledCurveLayoutDto
    {
        public string spacingMode;
        public float spacing;
        public float startAt;
        public string rotation;
        public string extendBefore;
        public string extendAfter;
        public bool lockTangents;
        public bool lockPositions;
        public CompiledCurvePointDto[] points = Array.Empty<CompiledCurvePointDto>();
    }

    [Serializable]
    public sealed class CompiledCurvePointDto
    {
        public float x;
        public float y;
        public float tangentX;
        public float tangentY;
        public string tangentMode;
    }

    [Serializable]
    public sealed class CompiledVisualDto
    {
        public string background;
        public string backgroundColor;
        public string border;
        public string borderStyle;
        public string borderRadius;
        public string boxShadow;
        public string opacity;
        public bool enableFill = true;
        public string fillColor;
        public bool useGradient;
        public string gradientColor;
        public string gradientDirection = nameof(AIToUGUIGradientDirection.None);
        public float cornerRadius;
        public bool useMaxRoundness;
        public float outlineWidth;
        public string outlineColor;
        public float shadowOffsetX;
        public float shadowOffsetY;
        public float shadowBlur;
        public string shadowColor;
        public bool enableGlow;
        public string glowColor;
        public float glowBlur;
        public float glowIntensity = 1f;
    }

    [Serializable]
    public sealed class CompiledTextStyleDto
    {
        public string color;
        public string fontSize;
        public string fontFamily;
        public string fontWeight;
        public string lineHeight;
        public string textAlign;
        public string letterSpacing;
        public string textTransform;
    }

    [Serializable]
    public sealed class CompiledMotionDto
    {
        public string presetId;
        public string enterMotion;
        public string hoverMotion;
        public string pressMotion;
        public float duration;
        public float distance;
        public float scale = 1f;
        public string ease;
        public string loopPresetId;
        public float loopDelay;
    }
}
