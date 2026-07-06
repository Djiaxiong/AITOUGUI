using System;
using System.Collections.Generic;
using PrimeTween;
using Riten.Windinator.Shapes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AIToUGUI
{
    public enum AIToUGUIIndicatorShapeSource
    {
        Auto = 0,
        ProceduralRoundedRect = 1,
        FillTextureAlpha = 2
    }

    public sealed class AIToUGUIWindinatorGooeyTabBarDemoBootstrap : MonoBehaviour
    {
        private const string DemoSceneName = "AIToUGUI_GooeyTabBarDemo";
        private const string DemoRootName = "AIToUGUI_GooeyTabBarDemo";
        private static Texture2D s_indicatorFillTexture;
        private static Texture2D s_indicatorFlowTexture;

        private static readonly DemoTabDefinition[] DemoTabs =
        {
            new DemoTabDefinition("Battle", "B", "Mission panel is active.", false, 0),
            new DemoTabDefinition("Crew", "C", "Crew roster and staff shortcuts.", true, 4),
            new DemoTabDefinition("Forge", "F", "Upgrade line and boost actions.", true, 11),
            new DemoTabDefinition("Guild", "G", "Guild missions and social shortcuts.", false, 0),
            new DemoTabDefinition("Map", "M", "World navigation and event routing.", false, 0)
        };

        private readonly List<DemoTabView> _tabViews = new List<DemoTabView>(DemoTabs.Length);

        private Font _defaultFont;
        private IAIToUGUITabIndicator _indicator;
        private Text _contentTitle;
        private Text _contentSubtitle;
        private int _selectedIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapOnDemoScene()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!string.Equals(scene.name, DemoSceneName, StringComparison.Ordinal))
            {
                return;
            }

            if (FindObjectOfType<AIToUGUIWindinatorGooeyTabBarDemoBootstrap>() != null)
            {
                return;
            }

            var root = new GameObject(DemoRootName, typeof(RectTransform));
            root.AddComponent<AIToUGUIWindinatorGooeyTabBarDemoBootstrap>().Build();
        }

        private void Build()
        {
            var layerRoot = UISystem.Instance.GetLayerRoot(UILayer.Normal);
            if (layerRoot == null)
            {
                Debug.LogWarning("[AIToUGUI] Gooey tab bar demo skipped because UISystem layer root is missing.", this);
                Destroy(gameObject);
                return;
            }

            _defaultFont = LoadBuiltinFont();

            transform.SetParent(layerRoot, false);
            var rootRect = GetComponent<RectTransform>();
            StretchFullScreen(rootRect);
            EnsureCanvasChannels(layerRoot.GetComponentInParent<Canvas>());

            var backdrop = CreateRect("Backdrop", rootRect, Vector2.zero, Vector2.zero);
            StretchFullScreen(backdrop);
            ConfigureWindinatorShape(
                backdrop.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.04f, 0.06f, 0.10f, 1f),
                true,
                new Color(0.08f, 0.12f, 0.18f, 1f),
                AIToUGUIGradientDirection.Vertical,
                0f,
                Vector4.zero,
                0f,
                0f,
                Color.clear,
                0f,
                0f,
                Color.clear,
                false,
                Color.clear,
                0f,
                0f);

            var stage = CreateRect("Stage", rootRect, new Vector2(860f, 640f), Vector2.zero);
            ConfigureWindinatorShape(
                stage.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.08f, 0.11f, 0.16f, 0.96f),
                true,
                new Color(0.11f, 0.16f, 0.24f, 0.96f),
                AIToUGUIGradientDirection.Vertical,
                34f,
                Vector4.one * 34f,
                0f,
                1.2f,
                new Color(0.72f, 0.82f, 0.96f, 0.14f),
                18f,
                32f,
                new Color(0f, 0f, 0f, 0.46f),
                false,
                Color.clear,
                0f,
                0f);

            CreateText(
                "Hint",
                stage,
                new Vector2(720f, 28f),
                new Vector2(0f, 274f),
                18,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                new Color(0.86f, 0.92f, 0.98f, 0.78f),
                "Click tabs to move the gooey indicator. The yellow selected plate is now texture-backed.");

            _contentTitle = CreateText(
                "ContentTitle",
                stage,
                new Vector2(560f, 72f),
                new Vector2(0f, 118f),
                46,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white,
                string.Empty);

            _contentSubtitle = CreateText(
                "ContentSubtitle",
                stage,
                new Vector2(620f, 32f),
                new Vector2(0f, 62f),
                19,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                new Color(0.82f, 0.88f, 0.95f, 0.76f),
                string.Empty);

            var heroCard = CreateRect("HeroCard", stage, new Vector2(620f, 240f), new Vector2(0f, -54f));
            ConfigureWindinatorShape(
                heroCard.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.10f, 0.14f, 0.20f, 0.92f),
                true,
                new Color(0.14f, 0.20f, 0.28f, 0.92f),
                AIToUGUIGradientDirection.DiagonalTopLeftToBottomRight,
                28f,
                Vector4.one * 28f,
                0f,
                1f,
                new Color(0.76f, 0.84f, 0.96f, 0.12f),
                14f,
                26f,
                new Color(0f, 0f, 0f, 0.38f),
                true,
                new Color(0.28f, 0.55f, 0.92f, 0.16f),
                20f,
                0.55f);

            CreateText(
                "CardEyebrow",
                heroCard,
                new Vector2(240f, 24f),
                new Vector2(0f, 78f),
                16,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.86f, 0.52f, 0.94f),
                "Gooey Transition Preview");

            CreateText(
                "CardBody",
                heroCard,
                new Vector2(500f, 84f),
                new Vector2(0f, 8f),
                26,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white,
                "Indicator leaves the old slot,\nstretches, then settles into the new slot.");

            var navShell = CreateRect("NavShell", stage, new Vector2(700f, 118f), new Vector2(0f, -234f));
            ConfigureWindinatorShape(
                navShell.gameObject,
                AIToUGUIWindinatorShapeKind.PerCornerRoundedRect,
                new Color(0.12f, 0.12f, 0.19f, 0.98f),
                true,
                new Color(0.16f, 0.15f, 0.24f, 0.98f),
                AIToUGUIGradientDirection.Vertical,
                30f,
                Vector4.one * 30f,
                0f,
                1f,
                new Color(0.90f, 0.84f, 0.64f, 0.10f),
                12f,
                26f,
                new Color(0f, 0f, 0f, 0.42f),
                false,
                Color.clear,
                0f,
                0f);

            var indicatorLayer = CreateRect("IndicatorLayer", navShell, new Vector2(660f, 92f), Vector2.zero);
            var indicatorGraphic = indicatorLayer.gameObject.AddComponent<CanvasGraphic>();
            indicatorGraphic.raycastTarget = false;
            indicatorGraphic.maskable = true;
            indicatorGraphic.Quality = 1.35f;
            indicatorGraphic.color = Color.white;
            indicatorGraphic.LeftUpColor = Color.white;
            indicatorGraphic.RightUpColor = Color.white;
            indicatorGraphic.RightDownColor = Color.white;
            indicatorGraphic.LeftDownColor = Color.white;
            indicatorGraphic.SetOutline(new Color(1f, 1f, 1f, 0.12f), 2f);
            indicatorGraphic.SetShadow(new Color(0.46f, 0.24f, 0.02f, 0.42f), 16f, 20f);
            indicatorGraphic.SetMargin(60f);

            _indicator = indicatorLayer.gameObject.AddComponent<AIToUGUIWindinatorProceduralTabIndicator>();

            var slots = new List<RectTransform>(DemoTabs.Length);
            var startX = -264f;
            const float slotSpacing = 132f;

            for (var i = 0; i < DemoTabs.Length; i++)
            {
                var tab = DemoTabs[i];
                var slot = CreateRect("Tab_" + tab.Label, navShell, new Vector2(116f, 92f), new Vector2(startX + slotSpacing * i, 0f));
                slots.Add(slot);

                var hitGraphic = slot.gameObject.AddComponent<Image>();
                hitGraphic.color = new Color(1f, 1f, 1f, 0.001f);
                hitGraphic.raycastTarget = true;

                var button = slot.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var capturedIndex = i;
                button.onClick.AddListener(() => SelectTab(capturedIndex, false));

                var binder = slot.gameObject.AddComponent<AIToUGUIAnimationBinder>();
                binder.SetListenToPointerEvents(true);
                binder.ApplyRecommendedButtonFeedback();

                var icon = CreateText(
                    "Icon",
                    slot,
                    new Vector2(64f, 34f),
                    new Vector2(0f, 12f),
                    28,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    Color.white,
                    tab.IconGlyph);

                var label = CreateText(
                    "Label",
                    slot,
                    new Vector2(84f, 22f),
                    new Vector2(0f, -22f),
                    15,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    Color.white,
                    tab.Label);

                Text badgeText = null;
                Image badgeImage = null;
                if (tab.HasBadge)
                {
                    var badge = CreateRect("Badge", slot, new Vector2(24f, 24f), new Vector2(28f, 28f));
                    badgeImage = badge.gameObject.AddComponent<Image>();
                    badgeImage.color = new Color(0.90f, 0.16f, 0.26f, 1f);
                    badgeText = CreateText(
                        "BadgeText",
                        badge,
                        new Vector2(24f, 20f),
                        Vector2.zero,
                        12,
                        FontStyle.Bold,
                        TextAnchor.MiddleCenter,
                        Color.white,
                        tab.BadgeValue.ToString());
                }

                _tabViews.Add(new DemoTabView(slot, icon, label, badgeImage, badgeText));
            }

            _indicator.Configure(slots, 0);
            SelectTab(0, true);
        }

        private void SelectTab(int index, bool instant)
        {
            if (index < 0 || index >= DemoTabs.Length)
            {
                return;
            }

            _selectedIndex = index;
            _indicator?.Select(index, instant);

            for (var i = 0; i < _tabViews.Count; i++)
            {
                ApplyTabVisual(_tabViews[i], DemoTabs[i], i == index);
            }

            if (_contentTitle != null)
            {
                _contentTitle.text = DemoTabs[index].Label;
            }

            if (_contentSubtitle != null)
            {
                _contentSubtitle.text = DemoTabs[index].Description;
            }
        }

        private static void ApplyTabVisual(DemoTabView view, DemoTabDefinition definition, bool selected)
        {
            if (view == null)
            {
                return;
            }

            if (view.Icon != null)
            {
                view.Icon.color = selected
                    ? new Color(0.13f, 0.12f, 0.16f, 1f)
                    : new Color(0.86f, 0.92f, 1f, 0.88f);
            }

            if (view.Label != null)
            {
                view.Label.color = selected
                    ? new Color(0.12f, 0.11f, 0.16f, 1f)
                    : new Color(0.84f, 0.88f, 0.96f, 0.78f);
            }

            if (view.BadgeImage != null)
            {
                view.BadgeImage.color = selected
                    ? new Color(0.86f, 0.14f, 0.22f, 1f)
                    : new Color(0.92f, 0.20f, 0.28f, 1f);
            }

            if (view.BadgeText != null)
            {
                view.BadgeText.color = Color.white;
            }
        }

        private static void EnsureCanvasChannels(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.rootCanvas.additionalShaderChannels |=
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.TexCoord3;
        }

        private static void ConfigureWindinatorShape(
            GameObject target,
            AIToUGUIWindinatorShapeKind shapeKind,
            Color fillColor,
            bool useGradient,
            Color gradientColor,
            AIToUGUIGradientDirection gradientDirection,
            float cornerRadius,
            Vector4 cornerRadii,
            float shapeAmount,
            float outlineWidth,
            Color outlineColor,
            float shadowSize,
            float shadowBlur,
            Color shadowColor,
            bool enableGlow,
            Color glowColor,
            float glowBlur,
            float glowIntensity)
        {
            if (target == null)
            {
                return;
            }

            var adapter = target.GetComponent<AIToUGUIWindinatorShapeAdapter>() ??
                          target.AddComponent<AIToUGUIWindinatorShapeAdapter>();

            adapter.Configure(
                shapeKind,
                true,
                fillColor,
                useGradient,
                gradientColor,
                gradientDirection,
                cornerRadius,
                cornerRadii,
                shapeAmount,
                outlineWidth,
                outlineColor,
                shadowSize,
                shadowBlur,
                shadowColor,
                enableGlow,
                glowColor,
                glowBlur,
                glowIntensity);
        }

        private Text CreateText(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 anchoredPosition,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color,
            string value)
        {
            var rect = CreateRect(name, parent, size, anchoredPosition);
            var text = rect.gameObject.AddComponent<Text>();
            text.raycastTarget = false;
            text.font = _defaultFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = value;
            return text;
        }

        private static RectTransform CreateRect(string name, RectTransform parent, Vector2 size, Vector2 anchoredPosition)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void StretchFullScreen(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        private static Font LoadBuiltinFont()
        {
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        private static Texture2D GetIndicatorFillTexture()
        {
            if (s_indicatorFillTexture != null)
            {
                return s_indicatorFillTexture;
            }

            const int width = 128;
            const int height = 96;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "AIToUGUI_GooeyIndicatorFill",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                var v = y / (float)(height - 1);
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1);

                    var topColor = new Color(1f, 0.83f, 0.34f, 1f);
                    var bottomColor = new Color(0.92f, 0.58f, 0.12f, 1f);
                    var baseColor = Color.Lerp(bottomColor, topColor, Mathf.Pow(v, 0.72f));

                    var leftShadow = Mathf.SmoothStep(0.22f, 0f, u) * 0.14f;
                    var rightWarmEdge = Mathf.SmoothStep(0.72f, 1f, u) * 0.08f;
                    var topGloss = Mathf.Exp(-Mathf.Pow((v - 0.78f) / 0.18f, 2f)) * 0.20f;
                    var centerGlow = Mathf.Exp(
                        -(
                            Mathf.Pow((u - 0.44f) / 0.22f, 2f) +
                            Mathf.Pow((v - 0.60f) / 0.28f, 2f))) * 0.18f;
                    var stripe = Mathf.Exp(-Mathf.Pow((u - 0.20f) / 0.06f, 2f)) * 0.10f;
                    var lowerShade = Mathf.SmoothStep(0f, 0.26f, v) * 0.10f;

                    var color = baseColor;
                    color *= 1f - leftShadow;
                    color *= 1f - lowerShade;
                    color.r += topGloss * 0.18f + centerGlow * 0.12f + rightWarmEdge * 0.08f + stripe * 0.10f;
                    color.g += topGloss * 0.12f + centerGlow * 0.07f + stripe * 0.05f;
                    color.b += topGloss * 0.04f;
                    color.a = 1f;

                    pixels[y * width + x] = ClampColor(color);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            s_indicatorFillTexture = texture;
            return s_indicatorFillTexture;
        }

        private static Texture2D GetIndicatorFlowTexture()
        {
            if (s_indicatorFlowTexture != null)
            {
                return s_indicatorFlowTexture;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AIToUGUI_GooeyIndicatorFlow",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                var v = y / (float)(size - 1);
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)(size - 1);

                    var noiseA = Mathf.PerlinNoise(u * 4.1f + 0.17f, v * 3.7f + 0.31f);
                    var noiseB = Mathf.PerlinNoise(u * 3.2f + 5.31f, v * 4.6f + 2.17f);
                    var swirl = Mathf.Sin((u + v * 1.12f) * Mathf.PI * 3.0f) * 0.5f + 0.5f;
                    var r = Mathf.Clamp01(noiseA * 0.72f + swirl * 0.28f);
                    var g = Mathf.Clamp01(noiseB * 0.74f + (1f - swirl) * 0.26f);

                    pixels[y * size + x] = new Color(r, g, 0.5f, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            s_indicatorFlowTexture = texture;
            return s_indicatorFlowTexture;
        }

        private static Color ClampColor(Color color)
        {
            color.r = Mathf.Clamp01(color.r);
            color.g = Mathf.Clamp01(color.g);
            color.b = Mathf.Clamp01(color.b);
            color.a = Mathf.Clamp01(color.a);
            return color;
        }

        private readonly struct DemoTabDefinition
        {
            public DemoTabDefinition(string label, string iconGlyph, string description, bool hasBadge, int badgeValue)
            {
                Label = label;
                IconGlyph = iconGlyph;
                Description = description;
                HasBadge = hasBadge;
                BadgeValue = badgeValue;
            }

            public string Label { get; }
            public string IconGlyph { get; }
            public string Description { get; }
            public bool HasBadge { get; }
            public int BadgeValue { get; }
        }

        private sealed class DemoTabView
        {
            public DemoTabView(RectTransform root, Text icon, Text label, Image badgeImage, Text badgeText)
            {
                Root = root;
                Icon = icon;
                Label = label;
                BadgeImage = badgeImage;
                BadgeText = badgeText;
            }

            public RectTransform Root { get; }
            public Text Icon { get; }
            public Text Label { get; }
            public Image BadgeImage { get; }
            public Text BadgeText { get; }
        }
    }

    [Obsolete("Use AIToUGUIWindinatorProceduralTabIndicator instead.")]
    public sealed class AIToUGUIWindinatorGooeyTabIndicator : AIToUGUIWindinatorProceduralTabIndicator
    {
    }
}
