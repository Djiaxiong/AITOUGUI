using System;
using System.Collections.Generic;
using System.Globalization;
using AIToUGUI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class UGUIPrototypeBuilder
{
    private static readonly Color PageColor = new Color(0.07f, 0.09f, 0.12f, 0.96f);
    private static readonly Color PanelColor = new Color(0.12f, 0.15f, 0.2f, 0.94f);
    private static readonly Color CardColor = new Color(0.15f, 0.18f, 0.24f, 0.96f);
    private static readonly Color AccentColor = new Color(0.98f, 0.67f, 0.22f, 1f);
    private static readonly Color DangerColor = new Color(0.85f, 0.27f, 0.27f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.97f, 1f, 1f);
    private static readonly Color MutedTextColor = new Color(0.72f, 0.78f, 0.88f, 1f);
    private static readonly Vector2 DefaultElementSize = new Vector2(160f, 48f);
    private static readonly Vector2 DefaultPanelSize = new Vector2(420f, 240f);

    public static GameObject BuildFromJson(string jsonText, RectTransform parent, string panelId)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return null;
        }

        try
        {
            var rootToken = JToken.Parse(jsonText);
            var nodeToken = rootToken["tree"] ?? rootToken["root"];
            if (nodeToken == null)
            {
                Debug.LogWarning("[UGUIPrototypeBuilder] JSON has no tree/root node.");
                return BuildFallback(parent, panelId, "Prototype root missing");
            }

            var designSize = new Vector2(
                Mathf.Max(1f, ReadFloat(rootToken, "designWidth", 1920f)),
                Mathf.Max(1f, ReadFloat(rootToken, "designHeight", 1080f)));

            return BuildNode(nodeToken, parent, designSize, designSize, true, panelId, 0);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[UGUIPrototypeBuilder] Failed to parse prototype JSON. {exception.Message}");
            return BuildFallback(parent, panelId, "Prototype JSON parse error");
        }
    }

    private static GameObject BuildNode(
        JToken nodeToken,
        RectTransform parent,
        Vector2 parentSize,
        Vector2 designSize,
        bool isRoot,
        string fallbackName,
        int siblingIndex)
    {
        var nodeName = SanitizeName(FirstNonEmpty(ReadString(nodeToken, "name"), fallbackName), siblingIndex);
        var tag = FirstNonEmpty(ReadString(nodeToken, "tag"), "div");
        var role = ReadString(nodeToken, "role");
        var displayText = ResolveDisplayText(nodeToken);
        var controlType = ResolveControlType(nodeToken, tag, displayText);
        var classes = ReadStringArray(nodeToken["classes"]);
        var layoutToken = nodeToken["layout"];
        var visualToken = nodeToken["visualStyle"] ?? nodeToken["visual"];
        var textStyleToken = nodeToken["textStyle"];

        var gameObject = new GameObject(nodeName, typeof(RectTransform));
        var rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);

        if (isRoot)
        {
            var pageRoot = gameObject.AddComponent<AIToUGUIPageRoot>();
            pageRoot.pageId = nodeName;
            pageRoot.runtimePageId = nodeName;
            pageRoot.designResolution = designSize;
        }

        var resolvedSize = ConfigureRect(rectTransform, layoutToken, parentSize, controlType, isRoot);
        var isFlex = ConfigureLayoutGroup(gameObject, layoutToken, classes);
        var graphic = EnsureGraphic(gameObject, visualToken, controlType, role);
        if (graphic != null)
        {
            graphic.color = ResolveBackgroundColor(visualToken, controlType, role);
        }

        var contentParent = rectTransform;
        switch (controlType)
        {
            case AIToUGUIControlType.Button:
                BuildButton(gameObject, rectTransform, displayText, textStyleToken, graphic, role);
                break;
            case AIToUGUIControlType.Input:
                BuildInput(gameObject, rectTransform, displayText, textStyleToken, graphic, role);
                break;
            case AIToUGUIControlType.Toggle:
                BuildToggle(gameObject, rectTransform, displayText, textStyleToken, graphic);
                break;
            case AIToUGUIControlType.Slider:
                BuildSlider(gameObject, rectTransform, graphic);
                break;
            case AIToUGUIControlType.Scrollbar:
                BuildScrollbar(gameObject, rectTransform, graphic);
                break;
            case AIToUGUIControlType.Scroll:
                contentParent = BuildScroll(gameObject, rectTransform, graphic);
                break;
            case AIToUGUIControlType.Dropdown:
                BuildDropdown(gameObject, rectTransform, displayText, textStyleToken, graphic, role);
                break;
            case AIToUGUIControlType.Progress:
                BuildProgress(gameObject, rectTransform, graphic);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(displayText))
                {
                    BuildText(gameObject, rectTransform, displayText, textStyleToken, role, controlType == AIToUGUIControlType.Text);
                }
                break;
        }

        var children = nodeToken["children"] as JArray;
        if (children != null)
        {
            for (var i = 0; i < children.Count; i++)
            {
                BuildNode(children[i], contentParent, resolvedSize, designSize, false, null, i);
            }
        }

        if (!isFlex && graphic == null && controlType == AIToUGUIControlType.Div && string.IsNullOrWhiteSpace(displayText))
        {
            gameObject.AddComponent<CanvasGroup>();
        }

        return gameObject;
    }

    private static Vector2 ConfigureRect(RectTransform rectTransform, JToken layoutToken, Vector2 parentSize, AIToUGUIControlType controlType, bool isRoot)
    {
        if (isRoot)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;
            return parentSize;
        }

        var preferredWidth = ResolveLength(ReadString(layoutToken, "width"), parentSize.x);
        var preferredHeight = ResolveLength(ReadString(layoutToken, "height"), parentSize.y);
        if (preferredWidth <= 0f)
        {
            preferredWidth = controlType == AIToUGUIControlType.Div ? DefaultPanelSize.x : DefaultElementSize.x;
        }

        if (preferredHeight <= 0f)
        {
            preferredHeight = controlType == AIToUGUIControlType.Div ? DefaultPanelSize.y : DefaultElementSize.y;
        }

        if (ParentUsesLayout(rectTransform.parent as RectTransform))
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.sizeDelta = new Vector2(preferredWidth, preferredHeight);
            rectTransform.anchoredPosition = Vector2.zero;
            var layoutElement = GetOrAdd<LayoutElement>(rectTransform.gameObject);
            layoutElement.preferredWidth = preferredWidth;
            layoutElement.preferredHeight = preferredHeight;
            return rectTransform.sizeDelta;
        }

        var x = ResolvePosition(ReadString(layoutToken, "left"), ReadString(layoutToken, "right"), preferredWidth, parentSize.x);
        var y = ResolvePosition(ReadString(layoutToken, "top"), ReadString(layoutToken, "bottom"), preferredHeight, parentSize.y);

        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.sizeDelta = new Vector2(preferredWidth, preferredHeight);
        rectTransform.anchoredPosition = new Vector2(x, -y);
        rectTransform.localScale = Vector3.one;
        return rectTransform.sizeDelta;
    }

    private static bool ConfigureLayoutGroup(GameObject gameObject, JToken layoutToken, List<string> classes)
    {
        var display = ReadString(layoutToken, "display");
        var flexDirection = ReadString(layoutToken, "flex-direction", "flexDirection");
        var isFlex = string.Equals(display, "flex", StringComparison.OrdinalIgnoreCase) ||
                     classes.Contains("stack-horizontal") ||
                     classes.Contains("stack-vertical");
        if (!isFlex)
        {
            return false;
        }

        var horizontal = classes.Contains("stack-horizontal") ||
                         (!classes.Contains("stack-vertical") && !string.Equals(flexDirection, "column", StringComparison.OrdinalIgnoreCase));
        HorizontalOrVerticalLayoutGroup layout = horizontal
            ? GetOrAdd<HorizontalLayoutGroup>(gameObject)
            : GetOrAdd<VerticalLayoutGroup>(gameObject);
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.spacing = ResolveLength(ReadString(layoutToken, "gap"), 0f);
        layout.padding = ParsePadding(ReadString(layoutToken, "padding"));
        return true;
    }

    private static Graphic EnsureGraphic(GameObject gameObject, JToken visualToken, AIToUGUIControlType controlType, string role)
    {
        var needsGraphic = !string.IsNullOrWhiteSpace(ReadString(visualToken, "background-color", "backgroundColor")) ||
                           controlType != AIToUGUIControlType.Div && controlType != AIToUGUIControlType.Text ||
                           role.StartsWith("window/", StringComparison.OrdinalIgnoreCase) ||
                           role.StartsWith("panel/", StringComparison.OrdinalIgnoreCase) ||
                           role.StartsWith("card/", StringComparison.OrdinalIgnoreCase) ||
                           role.StartsWith("button/", StringComparison.OrdinalIgnoreCase);
        return needsGraphic ? GetOrAdd<Image>(gameObject) : null;
    }

    private static void BuildButton(GameObject gameObject, RectTransform rectTransform, string text, JToken textStyleToken, Graphic graphic, string role)
    {
        var button = GetOrAdd<Button>(gameObject);
        if (graphic != null)
        {
            button.targetGraphic = graphic;
        }

        CreateTextChild(rectTransform, "Label", text, textStyleToken, role, true, 8f, 8f);
    }

    private static void BuildInput(GameObject gameObject, RectTransform rectTransform, string text, JToken textStyleToken, Graphic graphic, string role)
    {
        var input = GetOrAdd<TMP_InputField>(gameObject);
        if (graphic is Image image)
        {
            input.targetGraphic = image;
        }

        var viewport = CreateChildRect(rectTransform, "Viewport");
        Stretch(viewport, 8f, 8f);
        var textRect = CreateChildRect(viewport, "Text");
        Stretch(textRect, 0f, 0f);
        var textComponent = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTextStyle(textComponent, textStyleToken, role, false);
        input.textViewport = viewport;
        input.textComponent = textComponent;

        var placeholderRect = CreateChildRect(viewport, "Placeholder");
        Stretch(placeholderRect, 0f, 0f);
        var placeholder = placeholderRect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTextStyle(placeholder, textStyleToken, role, false);
        placeholder.color = MutedTextColor;
        placeholder.text = text;
        input.placeholder = placeholder;
    }

    private static void BuildToggle(GameObject gameObject, RectTransform rectTransform, string text, JToken textStyleToken, Graphic graphic)
    {
        var toggle = GetOrAdd<Toggle>(gameObject);
        if (graphic != null)
        {
            graphic.color = Color.clear;
        }

        var box = CreateFillImage(rectTransform, "Background", new Color(1f, 1f, 1f, 0.15f));
        box.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        box.rectTransform.pivot = new Vector2(0f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(22f, 22f);
        box.rectTransform.anchoredPosition = Vector2.zero;

        var check = CreateFillImage(box.rectTransform, "Checkmark", AccentColor);
        Stretch(check.rectTransform, 4f, 4f);
        toggle.targetGraphic = box;
        toggle.graphic = check;

        CreateTextChild(rectTransform, "Label", text, textStyleToken, string.Empty, false, 30f, 0f);
    }

    private static void BuildSlider(GameObject gameObject, RectTransform rectTransform, Graphic graphic)
    {
        var slider = GetOrAdd<Slider>(gameObject);
        if (graphic != null)
        {
            graphic.color = Color.clear;
        }

        var track = CreateFillImage(rectTransform, "Track", new Color(1f, 1f, 1f, 0.12f));
        Stretch(track.rectTransform, 0f, 0f);
        var fill = CreateFillImage(rectTransform, "Fill", AccentColor);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f);
        fill.rectTransform.anchorMax = new Vector2(0.7f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;
        slider.fillRect = fill.rectTransform;
    }

    private static void BuildScrollbar(GameObject gameObject, RectTransform rectTransform, Graphic graphic)
    {
        var scrollbar = GetOrAdd<Scrollbar>(gameObject);
        if (graphic != null)
        {
            graphic.color = Color.clear;
        }

        var handle = CreateFillImage(rectTransform, "Handle", AccentColor);
        handle.rectTransform.anchorMin = new Vector2(0f, 0f);
        handle.rectTransform.anchorMax = new Vector2(0.25f, 1f);
        handle.rectTransform.offsetMin = Vector2.zero;
        handle.rectTransform.offsetMax = Vector2.zero;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
    }

    private static RectTransform BuildScroll(GameObject gameObject, RectTransform rectTransform, Graphic graphic)
    {
        var scrollRect = GetOrAdd<ScrollRect>(gameObject);
        if (graphic != null)
        {
            graphic.color = Color.clear;
        }

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.SetParent(rectTransform, false);
        Stretch(viewportRect, 0f, 0f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = CreateChildRect(viewportRect, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
        return content;
    }

    private static void BuildDropdown(GameObject gameObject, RectTransform rectTransform, string text, JToken textStyleToken, Graphic graphic, string role)
    {
        var dropdown = GetOrAdd<TMP_Dropdown>(gameObject);
        if (graphic is Image image)
        {
            dropdown.targetGraphic = image;
        }

        dropdown.captionText = CreateTextChild(rectTransform, "Label", text, textStyleToken, role, false, 8f, 24f);
        var arrow = CreateTextChild(rectTransform, "Arrow", "v", textStyleToken, role, true, 0f, 0f);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.sizeDelta = new Vector2(18f, 18f);
        arrow.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
    }

    private static void BuildProgress(GameObject gameObject, RectTransform rectTransform, Graphic graphic)
    {
        if (graphic == null)
        {
            graphic = GetOrAdd<Image>(gameObject);
        }

        graphic.color = new Color(1f, 1f, 1f, 0.12f);
        var fill = CreateFillImage(rectTransform, "Fill", AccentColor);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f);
        fill.rectTransform.anchorMax = new Vector2(0.7f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;
    }

    private static void BuildText(GameObject gameObject, RectTransform rectTransform, string text, JToken textStyleToken, string role, bool standalone)
    {
        if (standalone)
        {
            var label = GetOrAdd<TextMeshProUGUI>(gameObject);
            ApplyTextStyle(label, textStyleToken, role, true);
            label.text = text;
            return;
        }

        CreateTextChild(rectTransform, "Text", text, textStyleToken, role, false, 4f, 4f);
    }

    private static TextMeshProUGUI CreateTextChild(RectTransform parent, string suffix, string text, JToken textStyleToken, string role, bool centered, float insetLeft, float insetRight)
    {
        var rectTransform = CreateChildRect(parent, suffix);
        Stretch(rectTransform, insetLeft, insetRight);
        var label = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTextStyle(label, textStyleToken, role, centered);
        label.text = text ?? string.Empty;
        return label;
    }

    private static void ApplyTextStyle(TMP_Text label, JToken textStyleToken, string role, bool centered)
    {
        label.color = ResolveTextColor(textStyleToken, role);
        label.fontSize = Mathf.Max(18f, ResolveLength(ReadString(textStyleToken, "font-size", "fontSize"), 0f));
        label.alignment = centered ? TextAlignmentOptions.Center : ResolveAlignment(ReadString(textStyleToken, "text-align", "textAlign"));
        label.enableWordWrapping = true;
        label.raycastTarget = false;
    }

    private static Color ResolveTextColor(JToken textStyleToken, string role)
    {
        var raw = ReadString(textStyleToken, "color");
        if (TryParseColor(raw, out var parsed))
        {
            return parsed;
        }

        return role.StartsWith("text/muted", StringComparison.OrdinalIgnoreCase) ? MutedTextColor : TextColor;
    }

    private static Color ResolveBackgroundColor(JToken visualToken, AIToUGUIControlType controlType, string role)
    {
        var raw = ReadString(visualToken, "background-color", "backgroundColor");
        if (TryParseColor(raw, out var parsed))
        {
            return parsed;
        }

        if (role.StartsWith("window/", StringComparison.OrdinalIgnoreCase))
        {
            return PageColor;
        }

        if (role.StartsWith("card/", StringComparison.OrdinalIgnoreCase))
        {
            return CardColor;
        }

        if (role.StartsWith("button/danger", StringComparison.OrdinalIgnoreCase))
        {
            return DangerColor;
        }

        if (role.StartsWith("button/", StringComparison.OrdinalIgnoreCase))
        {
            return AccentColor;
        }

        return controlType switch
        {
            AIToUGUIControlType.Input => new Color(1f, 1f, 1f, 0.08f),
            AIToUGUIControlType.Progress => new Color(1f, 1f, 1f, 0.12f),
            _ => PanelColor
        };
    }

    private static bool TryParseColor(string raw, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim();
        if (normalized.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
        {
            color = normalized.Contains("--page-background", StringComparison.OrdinalIgnoreCase) ? PageColor :
                normalized.Contains("--panel-fill", StringComparison.OrdinalIgnoreCase) ? PanelColor :
                normalized.Contains("--card-fill", StringComparison.OrdinalIgnoreCase) ? CardColor :
                normalized.Contains("--accent-color", StringComparison.OrdinalIgnoreCase) ? AccentColor :
                normalized.Contains("--text-secondary", StringComparison.OrdinalIgnoreCase) ? MutedTextColor :
                TextColor;
            return true;
        }

        if (string.Equals(normalized, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.clear;
            return true;
        }

        if (normalized.StartsWith("rgba", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var values = normalized.Substring(normalized.IndexOf('(') + 1).TrimEnd(')').Split(',');
            if (values.Length >= 3)
            {
                color = new Color(
                    ParseFloat(values[0]) / 255f,
                    ParseFloat(values[1]) / 255f,
                    ParseFloat(values[2]) / 255f,
                    values.Length > 3 ? Mathf.Clamp01(ParseFloat(values[3])) : 1f);
                return true;
            }
        }

        return ColorUtility.TryParseHtmlString(normalized, out color);
    }

    private static string ResolveDisplayText(JToken nodeToken)
    {
        var direct = ReadString(nodeToken, "directText");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Trim();
        }

        var aggregate = ReadString(nodeToken, "aggregateText");
        return string.IsNullOrWhiteSpace(aggregate) ? string.Empty : aggregate.Trim();
    }

    private static AIToUGUIControlType ResolveControlType(JToken nodeToken, string tag, string displayText)
    {
        var explicitType = ReadString(nodeToken, "controlType");
        if (Enum.TryParse(explicitType, true, out AIToUGUIControlType controlType))
        {
            return controlType;
        }

        return tag.ToLowerInvariant() switch
        {
            "button" => AIToUGUIControlType.Button,
            "input" => AIToUGUIControlType.Input,
            "textarea" => AIToUGUIControlType.Input,
            "scroll" => AIToUGUIControlType.Scroll,
            "scrollbar" => AIToUGUIControlType.Scrollbar,
            "toggle" => AIToUGUIControlType.Toggle,
            "slider" => AIToUGUIControlType.Slider,
            "dropdown" => AIToUGUIControlType.Dropdown,
            "image" => AIToUGUIControlType.Image,
            "img" => AIToUGUIControlType.Image,
            "progress" => AIToUGUIControlType.Progress,
            _ => string.IsNullOrWhiteSpace(displayText) ? AIToUGUIControlType.Div : AIToUGUIControlType.Text
        };
    }

    private static RectTransform CreateChildRect(RectTransform parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        var rectTransform = child.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private static Image CreateFillImage(RectTransform parent, string name, Color color)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rectTransform = child.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        var image = child.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static void Stretch(RectTransform rectTransform, float insetLeft, float insetRight)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = new Vector2(insetLeft, insetLeft);
        rectTransform.offsetMax = new Vector2(-insetRight, -insetLeft);
    }

    private static RectOffset ParsePadding(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new RectOffset();
        }

        var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var values = new List<int>();
        for (var i = 0; i < parts.Length; i++)
        {
            values.Add(Mathf.RoundToInt(ResolveLength(parts[i], 0f)));
        }

        if (values.Count == 1)
        {
            return new RectOffset(values[0], values[0], values[0], values[0]);
        }

        if (values.Count == 2)
        {
            return new RectOffset(values[1], values[1], values[0], values[0]);
        }

        if (values.Count == 3)
        {
            return new RectOffset(values[1], values[1], values[0], values[2]);
        }

        return new RectOffset(values[3], values[1], values[0], values[2]);
    }

    private static bool ParentUsesLayout(RectTransform parent)
    {
        return parent != null &&
               (parent.GetComponent<HorizontalOrVerticalLayoutGroup>() != null ||
                parent.GetComponent<GridLayoutGroup>() != null);
    }

    private static float ResolvePosition(string leadRaw, string trailRaw, float size, float parentSize)
    {
        var lead = ResolveLength(leadRaw, parentSize);
        if (lead > 0f || string.Equals(leadRaw, "0", StringComparison.Ordinal))
        {
            return lead;
        }

        var trail = ResolveLength(trailRaw, parentSize);
        return trail > 0f ? Mathf.Max(0f, parentSize - trail - size) : 0f;
    }

    private static float ResolveLength(string raw, float parentSize)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0f;
        }

        var normalized = raw.Trim();
        if (normalized.EndsWith("%", StringComparison.Ordinal))
        {
            return parentSize * ParseFloat(normalized.Substring(0, normalized.Length - 1)) * 0.01f;
        }

        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 2);
        }

        return ParseFloat(normalized);
    }

    private static float ParseFloat(string raw)
    {
        return float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
    }

    private static string ReadString(JToken token, params string[] names)
    {
        if (token == null)
        {
            return string.Empty;
        }

        for (var i = 0; i < names.Length; i++)
        {
            var value = token[names[i]];
            if (value != null && value.Type != JTokenType.Null)
            {
                return value.Value<string>() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static float ReadFloat(JToken token, string name, float defaultValue)
    {
        if (token == null || token[name] == null)
        {
            return defaultValue;
        }

        return token[name].Type switch
        {
            JTokenType.Integer => token[name].Value<float>(),
            JTokenType.Float => token[name].Value<float>(),
            _ => ParseFloat(token[name].Value<string>())
        };
    }

    private static List<string> ReadStringArray(JToken token)
    {
        var values = new List<string>();
        if (token is not JArray array)
        {
            return values;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var value = array[i].Value<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string FirstNonEmpty(string first, string second)
    {
        return !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
    }

    private static string SanitizeName(string raw, int siblingIndex)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"Node_{siblingIndex}";
        }

        return raw.Replace('/', '_').Replace('\\', '_');
    }

    private static TextAlignmentOptions ResolveAlignment(string raw)
    {
        return raw?.ToLowerInvariant() switch
        {
            "center" => TextAlignmentOptions.Center,
            "right" => TextAlignmentOptions.Right,
            _ => TextAlignmentOptions.Left
        };
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        if (!gameObject.TryGetComponent<T>(out var component))
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private static GameObject BuildFallback(RectTransform parent, string panelId, string message)
    {
        var root = new GameObject(string.IsNullOrWhiteSpace(panelId) ? "PrototypePanel" : panelId, typeof(RectTransform), typeof(Image));
        var rectTransform = root.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(40f, -40f);
        rectTransform.sizeDelta = new Vector2(420f, 180f);
        root.GetComponent<Image>().color = PanelColor;

        var label = root.AddComponent<TextMeshProUGUI>();
        label.text = message;
        label.color = TextColor;
        label.fontSize = 28f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return root;
    }
}
