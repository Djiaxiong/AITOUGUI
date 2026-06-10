using System;

namespace AIToUGUI.Lite
{
    public enum AIToUGUILiteControlType
    {
        Auto,
        Div,
        Text,
        Button,
        Input,
        Scroll,
        Scrollbar,
        Toggle,
        Slider,
        Dropdown,
        Image,
        Progress
    }

    [Serializable]
    public sealed class LiteCompiledSiteBundle
    {
        public string schemaVersion = "1.0";
        public LiteCompiledSiteDto site = new LiteCompiledSiteDto();
        public LiteCompiledThemeDto theme = new LiteCompiledThemeDto();
        public LiteCompiledPageDto[] pages = Array.Empty<LiteCompiledPageDto>();
    }

    [Serializable]
    public sealed class LiteCompiledSiteDto
    {
        public string siteId = "site_id";
        public string displayName = "Compiled Site";
        public int designWidth = 1920;
        public int designHeight = 1080;
    }

    [Serializable]
    public sealed class LiteCompiledThemeDto
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
        public LiteCompiledThemeTokenDto[] tokens = Array.Empty<LiteCompiledThemeTokenDto>();
        public LiteCompiledVisualPresetDto[] visualPresets = Array.Empty<LiteCompiledVisualPresetDto>();
        public LiteCompiledMotionPresetDto[] motionPresets = Array.Empty<LiteCompiledMotionPresetDto>();
    }

    [Serializable]
    public sealed class LiteCompiledThemeTokenDto
    {
        public string tokenId;
        public string value;
    }

    [Serializable]
    public sealed class LiteCompiledVisualPresetDto
    {
        public string presetId;
        public bool enableFill = true;
        public string fillColor = "#ffffff";
        public bool useGradient;
        public string gradientColor = "#ffffff";
        public string gradientDirection = "None";
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
    public sealed class LiteCompiledMotionPresetDto
    {
        public string presetId = "motion/default";
        public string enterMotion = "Fade";
        public string hoverMotion = "HoverLift";
        public string pressMotion = "ScaleIn";
        public float duration = 0.22f;
        public float distance = 26f;
        public float scale = 0.96f;
        public string ease = "OutCubic";
    }

    [Serializable]
    public sealed class LiteCompiledPageDto
    {
        public string pageId = "page_id";
        public string runtimePageId;
        public string displayName = "Page";
        public string prefabName = "CompiledPage";
        public string targetLayer = "Normal";
        public string logicalPath = "UI/Generated/Site/CompiledPage";
        public LiteCompiledNodeDto root = new LiteCompiledNodeDto();
    }

    [Serializable]
    public sealed class LiteCompiledNodeDto
    {
        public string name;
        public string tag = "div";
        public string controlType = nameof(AIToUGUILiteControlType.Div);
        public string role;
        public string elementId;
        public string variantId;
        public string shapeId;
        public string frameId;
        public string slotId;
        public string containerId;
        public string templateId;
        public string motionId;
        public string text;
        public LiteCompiledAttributeDto[] attributes = Array.Empty<LiteCompiledAttributeDto>();
        public string[] classes = Array.Empty<string>();
        public LiteCompiledLayoutDto layout = new LiteCompiledLayoutDto();
        public LiteCompiledVisualDto visual = new LiteCompiledVisualDto();
        public LiteCompiledTextStyleDto textStyle = new LiteCompiledTextStyleDto();
        public LiteCompiledMotionDto motion = new LiteCompiledMotionDto();
        public LiteCompiledNodeDto[] children = Array.Empty<LiteCompiledNodeDto>();
    }

    [Serializable]
    public sealed class LiteCompiledAttributeDto
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class LiteCompiledLayoutDto
    {
        public string display;
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
        public string gap;
        public string justifyContent;
        public string alignItems;
        public string flexDirection;
        public string overflow;
        public string overflowX;
        public string overflowY;
        public string boxSizing;
    }

    [Serializable]
    public sealed class LiteCompiledVisualDto
    {
        public string background;
        public string backgroundColor;
        public string border;
        public string borderRadius;
        public string boxShadow;
        public string opacity;
        public bool enableFill = true;
        public string fillColor;
        public bool useGradient;
        public string gradientColor;
        public string gradientDirection = "None";
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
    public sealed class LiteCompiledTextStyleDto
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
    public sealed class LiteCompiledMotionDto
    {
        public string presetId;
        public string enterMotion;
        public string hoverMotion;
        public string pressMotion;
        public float duration;
        public float distance;
        public float scale = 1f;
        public string ease;
    }
}
