using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AIToUGUI
{
    [CreateAssetMenu(fileName = "AIToUGUITheme", menuName = "AIToUGUI/Theme Definition")]
    public sealed class AIToUGUIThemeDefinition : ScriptableObject
    {
        [Title("基础信息")]
        [LabelText("主题 ID")]
        public string themeId = "default_theme";

        [LabelText("显示名称")]
        public string displayName = "Default Theme";

        [Title("风格层")]
        [LabelText("A 层主风格")]
        public AIToUGUIMainStyle mainStyle = AIToUGUIMainStyle.Informationism;

        [LabelText("B 层增强")]
        public AIToUGUIEnhancementStyle enhancementStyle = AIToUGUIEnhancementStyle.ResponsiveTypography;

        [LabelText("C 层质感")]
        public AIToUGUITextureFilterStyle textureFilterStyle = AIToUGUITextureFilterStyle.None;

        [Title("字体")]
        [InlineProperty]
        public AIToUGUIThemeFontSet fonts = new AIToUGUIThemeFontSet();

        [Title("基础颜色")]
        [LabelText("页面背景")]
        public Color pageBackground = new Color(0.06f, 0.08f, 0.11f, 1f);

        [LabelText("主面板填充")]
        public Color panelFill = new Color(0.13f, 0.16f, 0.2f, 0.96f);

        [LabelText("卡片填充")]
        public Color cardFill = new Color(0.17f, 0.2f, 0.25f, 0.98f);

        [LabelText("按钮填充")]
        public Color buttonFill = new Color(0.22f, 0.31f, 0.41f, 1f);

        [LabelText("强调色")]
        public Color accentColor = new Color(0.93f, 0.79f, 0.42f, 1f);

        [LabelText("主文本")]
        public Color textPrimary = new Color(0.94f, 0.95f, 0.97f, 1f);

        [LabelText("次文本")]
        public Color textSecondary = new Color(0.72f, 0.76f, 0.82f, 1f);

        [LabelText("描边色")]
        public Color outlineColor = new Color(1f, 1f, 1f, 0.12f);

        [LabelText("阴影色")]
        public Color shadowColor = new Color(0f, 0f, 0f, 0.28f);

        [LabelText("发光色")]
        public Color glowColor = new Color(0.45f, 0.76f, 1f, 0.35f);

        [Title("基础尺寸")]
        [LabelText("页面内边距")]
        public float pagePadding = 32f;

        [LabelText("面板圆角")]
        public float panelRadius = 22f;

        [LabelText("卡片圆角")]
        public float cardRadius = 16f;

        [LabelText("按钮圆角")]
        public float buttonRadius = 14f;

        [LabelText("描边宽度")]
        public float outlineWidth = 1f;

        [LabelText("阴影尺寸")]
        public float shadowSize = 12f;

        [LabelText("阴影模糊")]
        public float shadowBlur = 28f;

        [LabelText("发光模糊")]
        public float glowBlur = 24f;

        [LabelText("发光强度")]
        public float glowIntensity = 1f;

        [Title("Token")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIThemeToken> tokens = new List<AIToUGUIThemeToken>();

        [Title("视觉 Preset")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIVisualPreset> visualPresets = new List<AIToUGUIVisualPreset>();

        [Title("动效 Preset")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUIMotionPreset> motionPresets = new List<AIToUGUIMotionPreset>();

        [Title("循环动效 Preset")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<AIToUGUILoopMotionPreset> loopMotionPresets = new List<AIToUGUILoopMotionPreset>();

        public AIToUGUIVisualPreset ResolveVisualPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return null;
            }

            for (var i = 0; i < visualPresets.Count; i++)
            {
                var preset = visualPresets[i];
                if (preset != null && string.Equals(preset.presetId, presetId, System.StringComparison.Ordinal))
                {
                    return preset;
                }
            }

            return null;
        }

        public AIToUGUIMotionPreset ResolveMotionPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return null;
            }

            for (var i = 0; i < motionPresets.Count; i++)
            {
                var preset = motionPresets[i];
                if (preset != null && string.Equals(preset.presetId, presetId, System.StringComparison.Ordinal))
                {
                    return preset;
                }
            }

            return null;
        }

        public AIToUGUILoopMotionPreset ResolveLoopMotionPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return null;
            }

            for (var i = 0; i < loopMotionPresets.Count; i++)
            {
                var preset = loopMotionPresets[i];
                if (preset != null && string.Equals(preset.presetId, presetId, System.StringComparison.Ordinal))
                {
                    return preset;
                }
            }

            return null;
        }
    }
}
