using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace AIToUGUI
{
    public enum AIToUGUIMainStyle
    {
        NewBrutalism,
        RetroFuturism,
        Collectivism,
        Informationism,
        NaiveDesign,
        BentoNetwork,
        Cyberpunk,
        MemphisRevival
    }

    public enum AIToUGUIEnhancementStyle
    {
        None,
        Procedural3D,
        ExperimentalTypography,
        ResponsiveTypography,
        GrainBlur
    }

    public enum AIToUGUITextureFilterStyle
    {
        None,
        FrostedGlass,
        Neumorphism,
        LiquidGlass
    }

    public enum AIToUGUIControlType
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

    public enum AIToUGUIElementBackingMode
    {
        StyleBacked,
        PrefabBacked
    }

    public enum AIToUGUIRenderStrategy
    {
        Procedural,
        Hybrid,
        Raster
    }

    public enum AIToUGUIAssetType
    {
        Icon,
        Ornament,
        Snapshot,
        Frame,
        Background
    }

    public enum AIToUGUIAssetImportMode
    {
        Auto,
        Sprite,
        NineSlice,
        Tile,
        ReadOnlyOverlay
    }

    public enum AIToUGUIGradientDirection
    {
        None,
        Vertical,
        Horizontal,
        DiagonalTopLeftToBottomRight,
        DiagonalBottomLeftToTopRight
    }

    public enum AIToUGUIShapeRenderBackend
    {
        ProceduralUi,
        WindinatorLite
    }

    public enum AIToUGUIWindinatorShapeKind
    {
        None,
        PerCornerRoundedRect,
        CutCorner,
        Banner,
        Plate
    }

    public enum AIToUGUIMotionType
    {
        None,
        Fade,
        SlideUp,
        SlideDown,
        SlideLeft,
        SlideRight,
        ScaleIn,
        HoverLift,
        Pulse
    }

    public enum AIToUGUILoopMotionType
    {
        None,
        Rotate,
        RotateReverse,
        Float,
        Pulse
    }

    [Serializable]
    public sealed class AIToUGUIAssetReference
    {
        [LabelText("Asset ID")]
        public string assetId;

        [LabelText("Asset Type")]
        public AIToUGUIAssetType assetType = AIToUGUIAssetType.Icon;

        [LabelText("Usage")]
        public string usage;

        [LabelText("Import Mode")]
        public AIToUGUIAssetImportMode importMode = AIToUGUIAssetImportMode.Auto;

        [LabelText("Source")]
        public string source;

        [LabelText("Notes")]
        [TextArea]
        public string notes;

        [LabelText("Logical Asset Path")]
        public string logicalAssetPath;

        [LabelText("Slice Border")]
        public Vector4 sliceBorder;

        [LabelText("Pixels Per Unit")]
        public float pixelsPerUnit = 100f;

        [LabelText("Preferred Width")]
        public float preferredWidth;

        [LabelText("Preferred Height")]
        public float preferredHeight;

        [LabelText("Tint Policy")]
        public string tintPolicy;

        [LabelText("Atlas Group")]
        public string atlasGroup;
    }

    [Serializable]
    public sealed class AIToUGUIBundleDowngradeNote
    {
        [LabelText("Feature")]
        public string feature;

        [LabelText("Action")]
        public string action;

        [LabelText("Location")]
        public string location;

        [LabelText("Selector")]
        public string selector;

        [LabelText("Message")]
        [TextArea]
        public string message;
    }

    [Serializable]
    public sealed class AIToUGUIThemeToken
    {
        [LabelText("Token ID")]
        public string tokenId;

        [LabelText("Value")]
        public string value;
    }

    [Serializable]
    public sealed class AIToUGUIVisualPreset
    {
        [LabelText("Preset ID")]
        public string presetId = "panel/default";

        [LabelText("Description")]
        [TextArea]
        public string description;

        [LabelText("Enable Fill")]
        public bool enableFill = true;

        [LabelText("Fill Color")]
        public Color fillColor = new Color(0.13f, 0.16f, 0.2f, 0.96f);

        [LabelText("Use Gradient")]
        public bool useGradient;

        [LabelText("Gradient Color")]
        public Color gradientColor = new Color(0.08f, 0.1f, 0.14f, 0.96f);

        [LabelText("Gradient Direction")]
        public AIToUGUIGradientDirection gradientDirection = AIToUGUIGradientDirection.Vertical;

        [LabelText("Corner Radius")]
        public float cornerRadius = 18f;

        [LabelText("Use Max Roundness")]
        public bool useMaxRoundness;

        [LabelText("Outline Width")]
        public float outlineWidth = 1f;

        [LabelText("Outline Color")]
        public Color outlineColor = new Color(1f, 1f, 1f, 0.12f);

        [LabelText("Shadow Size")]
        public float shadowSize = 12f;

        [LabelText("Shadow Blur")]
        public float shadowBlur = 28f;

        [LabelText("Shadow Color")]
        public Color shadowColor = new Color(0f, 0f, 0f, 0.28f);

        [LabelText("Enable Glow")]
        public bool enableGlow;

        [LabelText("Glow Color")]
        public Color glowColor = new Color(0.45f, 0.76f, 1f, 0.35f);

        [LabelText("Glow Blur")]
        public float glowBlur = 24f;

        [LabelText("Glow Intensity")]
        public float glowIntensity = 1f;
    }

    [Serializable]
    public sealed class AIToUGUIMotionPreset
    {
        [LabelText("Preset ID")]
        public string presetId = "motion/default";

        [LabelText("Enter Motion")]
        public AIToUGUIMotionType enterMotion = AIToUGUIMotionType.Fade;

        [LabelText("Hover Motion")]
        public AIToUGUIMotionType hoverMotion = AIToUGUIMotionType.HoverLift;

        [LabelText("Press Motion")]
        public AIToUGUIMotionType pressMotion = AIToUGUIMotionType.ScaleIn;

        [LabelText("Duration")]
        public float duration = 0.22f;

        [LabelText("Distance")]
        public float distance = 26f;

        [LabelText("Scale")]
        public float scale = 0.96f;

        [LabelText("Ease")]
        public Ease ease = Ease.OutCubic;
    }

    [Serializable]
    public sealed class AIToUGUILoopMotionPreset
    {
        [LabelText("Preset ID")]
        public string presetId = "loop/rotate-slow";

        [LabelText("Loop Motion")]
        public AIToUGUILoopMotionType loopType = AIToUGUILoopMotionType.Rotate;

        [LabelText("Duration")]
        public float duration = 12f;

        [LabelText("Amplitude")]
        public float amplitude = 1f;

        [LabelText("Ease")]
        public Ease ease = Ease.Linear;
    }

    [Serializable]
    public sealed class AIToUGUIElementTemplate
    {
        [LabelText("Template ID")]
        public string templateId = string.Empty;

        [LabelText("Element ID")]
        public string elementId = "panel/main";

        [LabelText("Variant ID")]
        public string variantId = string.Empty;

        [LabelText("Component Family")]
        public string componentFamily = string.Empty;

        [LabelText("Component Variant")]
        public string componentVariant = string.Empty;

        [LabelText("Control Type")]
        public AIToUGUIControlType controlType = AIToUGUIControlType.Div;

        [LabelText("Backing Mode")]
        public AIToUGUIElementBackingMode backingMode = AIToUGUIElementBackingMode.StyleBacked;

        [LabelText("Default Render Strategy")]
        public AIToUGUIRenderStrategy defaultRenderStrategy = AIToUGUIRenderStrategy.Procedural;

        [LabelText("Default Role")]
        public string defaultRole = "window/main";

        [LabelText("Visual Preset")]
        public string visualPresetId = "panel/default";

        [LabelText("Motion Preset")]
        public string motionPresetId = "motion/default";

        [LabelText("Default Size")]
        public Vector2 defaultSize = new Vector2(320f, 120f);

        [AssetsOnly]
        [LabelText("Prefab")]
        public GameObject prefab;

        [LabelText("Allowed Slots")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> allowedSlots = new List<string>();

        [LabelText("Accessible Components")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<string> accessibleComponentTypes = new List<string>();

        [LabelText("Use Theme Text Color")]
        public bool useThemeTextColor = true;

        [LabelText("Use Theme Accent Color")]
        public bool useThemeAccentColor;
    }

    [Serializable]
    public sealed class AIToUGUIThemeFontSet
    {
        [LabelText("Primary Font")]
        public TMP_FontAsset primaryFont;

        [LabelText("Heading Font")]
        public TMP_FontAsset headingFont;

        [LabelText("Mono Font")]
        public TMP_FontAsset monoFont;
    }
}
