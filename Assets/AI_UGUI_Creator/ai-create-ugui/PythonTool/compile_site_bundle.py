#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from export_site_snapshots import export_site_snapshots
from site_package_layout import load_manifest, resolve_site_package_layout, write_preview_entry, write_task_report
from validate_site_package import SitePackageValidator, is_color_literal


VAR_REGEX = re.compile(r"var\((--[\w\-]+)\)")
NUMBER_REGEX = re.compile(r"-?(?:\d*\.\d+|\d+)")
LINEAR_GRADIENT_REGEX = re.compile(r"linear-gradient\((?P<direction>[^,]+),(?P<rest>.+)\)$", re.I)
COLOR_REGEX = re.compile(r"#(?:[0-9a-fA-F]{3,8})|rgba?\([^)]+\)|transparent", re.I)
ROTATE_TRANSFORM_REGEX = re.compile(r"^rotate\(\s*(?P<angle>-?(?:\d+|\d*\.\d+))deg\s*\)$", re.I)
DEGREE_LITERAL_REGEX = re.compile(r"^-?(?:\d+|\d*\.\d+)deg$", re.I)
TRANSLATE_X_REGEX = re.compile(r"^translateX\(\s*(?P<x>-?(?:\d+|\d*\.\d+)(?:px|%))\s*\)$", re.I)
TRANSLATE_Y_REGEX = re.compile(r"^translateY\(\s*(?P<y>-?(?:\d+|\d*\.\d+)(?:px|%))\s*\)$", re.I)
TRANSLATE_REGEX = re.compile(
    r"^translate\(\s*(?P<x>-?(?:\d+|\d*\.\d+)(?:px|%))\s*(?:,\s*(?P<y>-?(?:\d+|\d*\.\d+)(?:px|%))\s*)?\)$",
    re.I,
)
ANIMATION_REGEX = re.compile(
    r"^(?P<name>rotate|float|pulse)\s+(?P<duration>-?(?:\d+|\d*\.\d+))s\s+(?P<timing>linear|ease-in-out)\s+infinite(?:\s+(?P<direction>reverse))?$",
    re.I,
)
PRIMITIVE_ELEMENTS = {"button", "input", "toggle", "slider", "dropdown", "scrollbar", "scrollview", "image", "progress"}
COMPOSITE_COMPONENT_FAMILIES = {"frame/window", "button/compound", "card/item", "header/section", "list/row", "nav/tab"}
DEFAULT_VARIANT_ID = "default"
SUPPORTED_SHAPES = {"roundrect", "per-corner", "capsule", "cut-corner", "plate", "banner"}
SUPPORTED_FRAMES = {"solid", "outline", "hairline", "glow"}

# Maps the warning text fragments that the validator emits into structured downgrade records,
# so the compile report tells AI/Unity exactly which visual information was normalised away.
# Each tuple is (regex, feature, action). "action" options:
#   - "dropped"       : compile/baker ignore this property entirely
#   - "approximated"  : compile will substitute a best-effort Unity-compatible value
#   - "preview-only"  : preview-only property/selector, never reaches Unity (no visual loss)
DOWNGRADE_PATTERNS: Tuple[Tuple[re.Pattern, str, str], ...] = (
    (re.compile(r"Browser-only pattern detected \(grid\)", re.I),                 "grid",                    "dropped"),
    (re.compile(r"Browser-only pattern detected \(flex-wrap\)", re.I),            "flex-wrap",               "dropped"),
    (re.compile(r"Browser-only pattern detected \(calc\(\)\)", re.I),             "calc()",                  "dropped"),
    (re.compile(r"Browser-only pattern detected \(aspect-ratio\)", re.I),         "aspect-ratio",            "dropped"),
    (re.compile(r"Browser-only pattern detected \(filter\)", re.I),               "filter",                  "dropped"),
    (re.compile(r"Browser-only pattern detected \(backdrop-filter\)", re.I),      "backdrop-filter",         "dropped"),
    (re.compile(r"Browser-only pattern detected \(position:fixed\)", re.I),       "position:fixed",          "dropped"),
    (re.compile(r"Browser-only pattern detected \(repeating-linear-gradient\)", re.I), "repeating-linear-gradient", "dropped"),
    (re.compile(r"Browser-only pattern detected \(repeating-radial-gradient\)", re.I), "repeating-radial-gradient", "dropped"),
    (re.compile(r"Browser-only pattern detected \(radial-gradient\)", re.I),      "radial-gradient",         "approximated"),
    (re.compile(r"Browser-only pattern detected \(@keyframes\)", re.I),           "@keyframes",              "dropped"),
    (re.compile(r"Radial gradient may not match Unity", re.I),                    "radial-gradient",         "approximated"),
    (re.compile(r"Unsupported transform value", re.I),                            "transform",               "approximated"),
    (re.compile(r"Unsupported opacity value", re.I),                              "opacity",                 "approximated"),
    (re.compile(r"Unsupported border-style", re.I),                               "border-style",            "approximated"),
    (re.compile(r"Unsupported animation '", re.I),                                "animation",               "dropped"),
    (re.compile(r"Unsupported animation-delay", re.I),                            "animation-delay",         "dropped"),
    (re.compile(r"Unsupported background pattern", re.I),                         "background",             "dropped"),
    (re.compile(r"Unsupported background value", re.I),                           "background",             "dropped"),
    (re.compile(r"Unsupported value '.*' for '(?P<prop>[^']+)'", re.I),            None,                      "approximated"),
    (re.compile(r"Discouraged unit pattern \((?P<label>[^)]+)\)", re.I),           None,                      "approximated"),
    (re.compile(r"Unsupported selector \(preview-only", re.I),                    None,                      "preview-only"),
    (re.compile(r"Unsupported CSS property '(?P<prop>[^']+)'", re.I),              None,                      "dropped"),
)

LOCATION_REGEX = re.compile(r"^\[(?P<loc>[^\]]+)\]\s*")
SELECTOR_REGEX = re.compile(r"in selector '(?P<selector>[^']+)'")

LAYOUT_KEYS = (
    "display",
    "position",
    "left",
    "right",
    "top",
    "bottom",
    "width",
    "height",
    "min-width",
    "max-width",
    "min-height",
    "max-height",
    "padding",
    "margin",
    "margin-left",
    "margin-right",
    "margin-top",
    "margin-bottom",
    "gap",
    "justify-content",
    "align-items",
    "flex-direction",
    "overflow",
    "overflow-x",
    "overflow-y",
    "box-sizing",
)

TEXT_STYLE_KEYS = (
    "color",
    "font-size",
    "font-family",
    "font-weight",
    "line-height",
    "text-align",
    "letter-spacing",
    "text-transform",
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Compile an AIToUGUI site package into compiled_site_bundle.json.")
    parser.add_argument("site_root", type=Path, help="Path to the AIToUGUI site package root.")
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=None,
        help="Optional output directory. Defaults to <site_root>/reports.",
    )
    parser.add_argument(
        "--allowlist",
        type=Path,
        default=None,
        help="Optional path to the AIToUGUI allowlist JSON.",
    )
    parser.add_argument(
        "--allow-legacy-metadata",
        action="store_true",
        help="Treat missing ui_contract.json / ui_self_check_report.json as warnings.",
    )
    parser.add_argument(
        "--force-compile",
        action="store_true",
        help=(
            "Diagnostic-only: compile a bundle even when strict validation reports contract-level "
            "errors. The errors still appear in compile_report.json and the bundle is marked with "
            "`compiled_with_force=true`. Intended for inspecting how the resolver handles bold or "
            "non-compliant HTML in Unity; not part of the production pipeline."
        ),
    )
    parser.add_argument(
        "--reuse-existing-snapshots",
        action="store_true",
        help="Reuse <site_root>/snapshots if present instead of regenerating snapshots during compile.",
    )
    return parser


def resolve_tokens_in_value(value: str, tokens: Dict[str, str]) -> str:
    if not value:
        return value

    resolved = value
    for _ in range(8):
        updated = VAR_REGEX.sub(lambda match: tokens.get(match.group(1), match.group(0)), resolved)
        if updated == resolved:
            break
        resolved = updated
    return resolved.strip()


def resolve_style_dict(style: Dict[str, str], tokens: Dict[str, str]) -> Dict[str, str]:
    return {
        key: resolve_tokens_in_value(str(value), tokens)
        for key, value in style.items()
        if value not in (None, "")
    }


def parse_px(value: str, default: float = 0.0) -> float:
    if not value:
        return default
    lowered = value.strip().lower()
    if lowered in {"0", "0px"}:
        return 0.0
    match = re.match(r"^-?(?:\d+|\d*\.\d+)px$", lowered)
    if match:
        return float(lowered[:-2])
    return default


def parse_number(value: str, default: float = 0.0) -> float:
    if not value:
        return default
    match = NUMBER_REGEX.search(str(value))
    return float(match.group(0)) if match else default


def parse_int(value: str, default: int = 0) -> int:
    if not value:
        return default
    match = re.search(r"-?\d+", str(value))
    return int(match.group(0)) if match else default


def parse_bool(value: object, default: bool = False) -> bool:
    if value is None:
        return default
    lowered = str(value).strip().lower()
    if not lowered:
        return default
    if lowered in {"1", "true", "yes", "on"}:
        return True
    if lowered in {"0", "false", "no", "off"}:
        return False
    return default


def parse_border(border_value: str) -> Tuple[float, str]:
    if not border_value:
        return 0.0, ""
    numbers = NUMBER_REGEX.findall(border_value)
    width = float(numbers[0]) if numbers else 0.0
    colors = COLOR_REGEX.findall(border_value)
    return width, colors[-1] if colors else ""


def parse_shadow(box_shadow: str) -> Tuple[float, float, float, str]:
    if not box_shadow:
        return 0.0, 0.0, 0.0, ""
    numbers = [float(value) for value in NUMBER_REGEX.findall(box_shadow)]
    offset_x = numbers[0] if len(numbers) >= 1 else 0.0
    offset_y = numbers[1] if len(numbers) >= 2 else 0.0
    blur = numbers[2] if len(numbers) >= 3 else 0.0
    colors = COLOR_REGEX.findall(box_shadow)
    return offset_x, offset_y, blur, colors[0] if colors else ""


def parse_rotation_z(value: str) -> float:
    if not value:
        return 0.0
    trimmed = value.strip()
    match = ROTATE_TRANSFORM_REGEX.match(trimmed)
    if match:
        return parse_number(match.group("angle"), 0.0)
    if DEGREE_LITERAL_REGEX.match(trimmed):
        return parse_number(trimmed[:-3], 0.0)
    return 0.0


def parse_static_translate(value: str) -> Tuple[str, str]:
    if not value:
        return "", ""

    trimmed = value.strip()
    match = TRANSLATE_X_REGEX.match(trimmed)
    if match:
        return match.group("x"), ""

    match = TRANSLATE_Y_REGEX.match(trimmed)
    if match:
        return "", match.group("y")

    match = TRANSLATE_REGEX.match(trimmed)
    if match:
        return match.group("x") or "", match.group("y") or ""

    return "", ""


def parse_inset_edges(value: str) -> Tuple[str, str, str, str]:
    if not value:
        return "", "", "", ""

    parts = [part.strip() for part in value.replace(",", " ").split() if part.strip()]
    if not parts:
        return "", "", "", ""
    if len(parts) == 1:
        top = right = bottom = left = parts[0]
    elif len(parts) == 2:
        top = bottom = parts[0]
        right = left = parts[1]
    elif len(parts) == 3:
        top = parts[0]
        right = left = parts[1]
        bottom = parts[2]
    else:
        top, right, bottom, left = parts[0], parts[1], parts[2], parts[3]

    return left, right, top, bottom


def normalize_border_style(resolved_style: Dict[str, str]) -> str:
    border_style = str(resolved_style.get("border-style", "")).strip().lower()
    if border_style in {"solid", "dashed"}:
        return border_style

    border = str(resolved_style.get("border", "")).strip().lower()
    if "dashed" in border:
        return "dashed"
    if "solid" in border:
        return "solid"
    return ""


def resolve_loop_motion_preset(attrs: Dict[str, object], resolved_style: Dict[str, str]) -> Tuple[str, float]:
    explicit = str(attrs.get("data-ui-loop-motion", "")).strip()
    delay = parse_number(str(resolved_style.get("animation-delay", "")).strip(), 0.0)
    if explicit:
        return explicit, delay

    animation = str(resolved_style.get("animation", "")).strip()
    if not animation:
        return "", delay

    match = ANIMATION_REGEX.match(animation)
    if not match:
        return "", delay

    name = match.group("name").lower()
    direction = (match.group("direction") or "").lower()
    if name == "rotate":
        return ("loop/rotate-slow-reverse" if direction == "reverse" else "loop/rotate-slow"), delay
    if name == "float":
        return "loop/float-soft", delay
    if name == "pulse":
        return "loop/pulse-soft", delay
    return "", delay


def parse_gradient(background: str) -> Tuple[bool, str, str, str]:
    if not background:
        return False, "", "", "None"

    raw = background.strip()
    if is_color_literal(raw):
        return False, raw, "", "None"

    match = LINEAR_GRADIENT_REGEX.match(raw)
    if not match:
        return False, "", "", "None"

    direction = match.group("direction").strip().lower()
    colors = COLOR_REGEX.findall(match.group("rest"))
    if not colors:
        return False, "", "", "None"

    primary = colors[0]
    secondary = colors[1] if len(colors) > 1 else colors[0]

    if "90" in direction or "270" in direction or "left" in direction or "right" in direction:
        gradient_direction = "Horizontal"
    elif "45" in direction:
        gradient_direction = "DiagonalBottomLeftToTopRight"
    elif "315" in direction or "135" in direction:
        gradient_direction = "DiagonalTopLeftToBottomRight"
    else:
        gradient_direction = "Vertical"

    return True, primary, secondary, gradient_direction


def normalize_element_identity(attrs: Dict[str, object]) -> Tuple[str, str]:
    raw_element = str(attrs.get("data-ui-element", "")).strip().lower()
    raw_variant = str(attrs.get("data-ui-variant", "")).strip().lower()
    if not raw_element:
        return "", ""

    if raw_element in PRIMITIVE_ELEMENTS:
        return raw_element, raw_variant or DEFAULT_VARIANT_ID

    if "/" in raw_element:
        base, variant = raw_element.split("/", 1)
        base = base.strip().lower()
        variant = variant.strip().lower()
        if base in PRIMITIVE_ELEMENTS:
            return base, raw_variant or variant or DEFAULT_VARIANT_ID

    return raw_element, raw_variant


def normalize_shape_id(attrs: Dict[str, object]) -> str:
    raw_shape = str(attrs.get("data-ui-shape", "")).strip().lower()
    return raw_shape if raw_shape in SUPPORTED_SHAPES else raw_shape


def normalize_frame_id(attrs: Dict[str, object]) -> str:
    raw_frame = str(attrs.get("data-ui-frame", "")).strip().lower()
    return raw_frame if raw_frame in SUPPORTED_FRAMES else raw_frame


def normalize_component_family(value: object) -> str:
    return str(value or "").strip().lower()


def normalize_component_variant(value: object) -> str:
    normalized = str(value or "").strip().lower()
    return normalized or "default"


def normalize_render_strategy(value: object) -> str:
    normalized = str(value or "").strip().lower()
    if normalized == "hybrid":
        return "hybrid"
    if normalized == "raster":
        return "raster"
    return "procedural"


def build_semantic_fingerprint(node: Dict[str, object], attrs: Dict[str, object]) -> str:
    classes = node.get("classes", []) if isinstance(node.get("classes"), list) else []
    return " ".join(
        [
            str(node.get("name", "")),
            str(node.get("role", "")),
            str(attrs.get("data-ui-element", "")),
            str(attrs.get("data-ui-template", "")),
            " ".join(str(item) for item in classes),
        ]
    ).lower()


def contains_semantic_token(semantic: str, *tokens: str) -> bool:
    semantic = semantic.lower()
    return any(token and token.lower() in semantic for token in tokens)


def infer_component_family(node: Dict[str, object], attrs: Dict[str, object], resolved_style: Dict[str, str]) -> str:
    explicit = normalize_component_family(attrs.get("data-ui-component-family", ""))
    if explicit:
        return explicit

    element_id = str(attrs.get("data-ui-element", "")).strip().lower()
    if element_id in {"frame/window", "button/compound", "card/item", "header/section", "list/row", "nav/tab"}:
        return element_id

    semantic = build_semantic_fingerprint(node, attrs)
    if contains_semantic_token(semantic, "tab", "nav", "navigation", "sidebar-item", "menu-item"):
        return "nav/tab"
    if contains_semantic_token(semantic, "window", "modal", "dialog", "sidebar", "split-panel", "frame"):
        return "frame/window"
    if contains_semantic_token(semantic, "card", "item", "reward", "inventory", "slot"):
        return "card/item"
    if contains_semantic_token(semantic, "header", "section", "title-bar", "hero"):
        return "header/section"
    if contains_semantic_token(semantic, "list-row", "row", "entry", "shop-item", "task-item"):
        return "list/row"

    has_icon_intent = bool(str(attrs.get("data-ui-icon", "")).strip() or str(attrs.get("data-ui-asset-id", "")).strip())
    if infer_control_type(node) == "Button" and (has_icon_intent or len(node.get("children", [])) > 1):
        return "button/compound"

    return ""


def infer_component_variant(
    component_family: str,
    node: Dict[str, object],
    attrs: Dict[str, object],
    resolved_style: Dict[str, str],
) -> str:
    explicit = str(attrs.get("data-ui-component-variant", "")).strip().lower()
    if explicit:
        return explicit

    semantic = build_semantic_fingerprint(node, attrs)
    if component_family == "frame/window":
        if contains_semantic_token(semantic, "modal", "dialog", "popup"):
            return "modal"
        if contains_semantic_token(semantic, "split", "sidebar"):
            return "split"
    if component_family == "button/compound":
        if contains_semantic_token(semantic, "badge", "count", "notification") or str(attrs.get("data-ui-value", "")).strip():
            return "badge"
        if str(attrs.get("data-ui-icon", "")).strip():
            return "icon"
    if component_family == "card/item":
        if contains_semantic_token(semantic, "reward", "loot", "prize"):
            return "reward"
        if contains_semantic_token(semantic, "empty", "slot"):
            return "empty-slot"
    if component_family == "header/section" and contains_semantic_token(semantic, "hero", "banner"):
        return "hero"
    if component_family == "list/row" and contains_semantic_token(semantic, "shop", "store", "vendor"):
        return "shop"
    if component_family == "nav/tab" and contains_semantic_token(semantic, "sidebar", "vertical"):
        return "sidebar"

    return "default"


def infer_render_strategy(
    node: Dict[str, object],
    attrs: Dict[str, object],
    resolved_style: Dict[str, str],
) -> str:
    explicit = str(attrs.get("data-ui-render-strategy", "")).strip().lower()
    if explicit:
        return normalize_render_strategy(explicit)

    background = str(resolved_style.get("background", "")).strip().lower()
    has_url_background = "url(" in background
    has_image_source = bool(str(attrs.get("src", "")).strip())
    has_icon_intent = bool(str(attrs.get("data-ui-icon", "")).strip() or str(attrs.get("data-ui-asset-id", "")).strip())
    has_raster_effect = any(
        str(resolved_style.get(key, "")).strip()
        for key in ("backdrop-filter", "filter", "mask-image", "mix-blend-mode")
    )

    if has_raster_effect:
        return "raster"
    if has_url_background or has_image_source or has_icon_intent:
        return "hybrid"
    return "procedural"


def build_generated_asset_id(node: Dict[str, object], suffix: str) -> str:
    base_name = str(node.get("name", "")).strip() or str(node.get("tag", "node")).strip() or "node"
    base_name = re.sub(r"\s+", "-", base_name).strip("-").lower()
    suffix = str(suffix or "asset").strip().lower() or "asset"
    return f"{base_name}/{suffix}"


def resolve_image_asset_type(node: Dict[str, object], resolved_style: Dict[str, str], render_strategy: str) -> str:
    width = parse_number(str(resolved_style.get("width", "")).strip(), 0.0)
    height = parse_number(str(resolved_style.get("height", "")).strip(), 0.0)
    if width and width <= 64.0 and height and height <= 64.0:
        return "icon"
    return "snapshot" if render_strategy == "raster" else "ornament"


def append_asset_ref(asset_refs: List[Dict[str, object]], asset_ref: Dict[str, object]) -> None:
    asset_id = str(asset_ref.get("assetId", "")).strip()
    if not asset_id:
        return
    if any(str(existing.get("assetId", "")).strip().lower() == asset_id.lower() for existing in asset_refs):
        return
    asset_refs.append(asset_ref)


def parse_asset_slice(raw_value: str) -> List[float]:
    parts = [part.strip() for part in re.split(r"[\s,]+", raw_value or "") if part.strip()]
    if len(parts) != 4:
        return [0.0, 0.0, 0.0, 0.0]
    values: List[float] = []
    for part in parts:
        try:
            values.append(float(part))
        except ValueError:
            return [0.0, 0.0, 0.0, 0.0]
    return values


def parse_asset_float(raw_value: str, fallback: float) -> float:
    try:
        parsed = float((raw_value or "").strip())
        return parsed if parsed > 0 else fallback
    except ValueError:
        return fallback


def resolve_explicit_asset_source(attrs: Dict[str, object], resolved_style: Dict[str, str]) -> str:
    src = str(attrs.get("src", "")).strip()
    if src:
        return src
    background = str(resolved_style.get("background", "")).strip()
    if "url(" in background.lower():
        return background
    return ""


def collect_asset_refs(
    node: Dict[str, object],
    attrs: Dict[str, object],
    resolved_style: Dict[str, str],
    render_strategy: str,
) -> List[Dict[str, object]]:
    asset_refs: List[Dict[str, object]] = []
    explicit_asset_id = str(attrs.get("data-ui-asset-id", "")).strip()
    if explicit_asset_id:
        append_asset_ref(
            asset_refs,
            {
                "assetId": explicit_asset_id,
                "assetType": str(attrs.get("data-ui-asset-type", "")).strip().lower() or "icon",
                "usage": str(attrs.get("data-ui-asset-usage", "")).strip() or "explicit-asset",
                "importMode": str(attrs.get("data-ui-asset-import", "")).strip().lower() or "auto",
                "source": resolve_explicit_asset_source(attrs, resolved_style),
                "sliceBorder": parse_asset_slice(str(attrs.get("data-ui-asset-slice", "")).strip()),
                "pixelsPerUnit": parse_asset_float(str(attrs.get("data-ui-asset-ppu", "")).strip(), 100.0),
                "preferredWidth": parse_asset_float(str(attrs.get("data-ui-asset-width", "")).strip(), 0.0),
                "preferredHeight": parse_asset_float(str(attrs.get("data-ui-asset-height", "")).strip(), 0.0),
                "tintPolicy": str(attrs.get("data-ui-asset-tint", "")).strip().lower(),
                "atlasGroup": str(attrs.get("data-ui-asset-atlas", "")).strip(),
                "notes": "",
            },
        )

    icon_id = str(attrs.get("data-ui-icon", "")).strip()
    if icon_id:
        append_asset_ref(
            asset_refs,
            {
                "assetId": icon_id,
                "assetType": "icon",
                "usage": "icon-slot",
                "importMode": "sprite",
                "source": icon_id,
                "sliceBorder": [0.0, 0.0, 0.0, 0.0],
                "pixelsPerUnit": 100.0,
                "preferredWidth": 0.0,
                "preferredHeight": 0.0,
                "tintPolicy": "",
                "atlasGroup": "",
                "notes": "Semantic icon request.",
            },
        )

    image_source = str(attrs.get("src", "")).strip()
    if str(node.get("tag", "")).strip().lower() == "img" and image_source:
        asset_type = resolve_image_asset_type(node, resolved_style, render_strategy)
        append_asset_ref(
            asset_refs,
            {
                "assetId": build_generated_asset_id(node, "image"),
                "assetType": asset_type,
                "usage": "image-node",
                "importMode": "read-only-overlay" if asset_type == "snapshot" else "sprite",
                "source": image_source,
                "sliceBorder": parse_asset_slice(str(attrs.get("data-ui-asset-slice", "")).strip()),
                "pixelsPerUnit": parse_asset_float(str(attrs.get("data-ui-asset-ppu", "")).strip(), 100.0),
                "preferredWidth": parse_asset_float(str(attrs.get("data-ui-asset-width", "")).strip(), 0.0),
                "preferredHeight": parse_asset_float(str(attrs.get("data-ui-asset-height", "")).strip(), 0.0),
                "tintPolicy": str(attrs.get("data-ui-asset-tint", "")).strip().lower(),
                "atlasGroup": str(attrs.get("data-ui-asset-atlas", "")).strip(),
                "notes": "HTML image source.",
            },
        )

    background = str(resolved_style.get("background", "")).strip()
    if "url(" in background.lower():
        asset_type = "snapshot" if render_strategy == "raster" else "ornament"
        append_asset_ref(
            asset_refs,
            {
                "assetId": build_generated_asset_id(node, asset_type),
                "assetType": asset_type,
                "usage": "background-effect",
                "importMode": "read-only-overlay" if asset_type == "snapshot" else "sprite",
                "source": background,
                "sliceBorder": parse_asset_slice(str(attrs.get("data-ui-asset-slice", "")).strip()),
                "pixelsPerUnit": parse_asset_float(str(attrs.get("data-ui-asset-ppu", "")).strip(), 100.0),
                "preferredWidth": parse_asset_float(str(attrs.get("data-ui-asset-width", "")).strip(), 0.0),
                "preferredHeight": parse_asset_float(str(attrs.get("data-ui-asset-height", "")).strip(), 0.0),
                "tintPolicy": str(attrs.get("data-ui-asset-tint", "")).strip().lower(),
                "atlasGroup": str(attrs.get("data-ui-asset-atlas", "")).strip(),
                "notes": "Background image or effect source.",
            },
        )

    return asset_refs


def collect_fidelity_notes(attrs: Dict[str, object], resolved_style: Dict[str, str], render_strategy: str) -> List[str]:
    notes: List[str] = []

    explicit_note = str(attrs.get("data-ui-fidelity-note", "")).strip()
    if explicit_note:
        notes.append(explicit_note)

    if str(attrs.get("data-ui-icon", "")).strip():
        notes.append("Replace semantic icon placeholder with a real Unity sprite.")

    if render_strategy == "raster":
        notes.append("This node requires raster or snapshot-backed local effect restoration.")

    if str(resolved_style.get("-ai-glow", "")).strip() or str(resolved_style.get("-ai-glow-color", "")).strip():
        notes.append("Keep local glow/highlight treatment during Unity restoration.")

    deduped: List[str] = []
    for note in notes:
        if note and note not in deduped:
            deduped.append(note)
    return deduped


def upsert_node_attribute(node_payload: Dict[str, object], name: str, value: str) -> None:
    if not name or not value:
        return
    attributes = node_payload.get("attributes", [])
    if not isinstance(attributes, list):
        return
    for item in attributes:
        if isinstance(item, dict) and str(item.get("name", "")).strip() == name:
            item["value"] = value
            return
    attributes.append({"name": name, "value": value})


def child_semantic_text(child: Dict[str, object]) -> str:
    classes = child.get("classes", []) if isinstance(child.get("classes"), list) else []
    return " ".join(
        [
            str(child.get("name", "")),
            str(child.get("role", "")),
            str(child.get("elementId", "")),
            str(child.get("templateId", "")),
            str(child.get("text", "")),
            " ".join(str(item) for item in classes),
        ]
    ).lower()


def child_has_asset_type(child: Dict[str, object], asset_type: str) -> bool:
    for asset_ref in child.get("assetRefs", []) if isinstance(child.get("assetRefs"), list) else []:
        if isinstance(asset_ref, dict) and str(asset_ref.get("assetType", "")).strip().lower() == asset_type:
            return True
    return False


def is_text_child(child: Dict[str, object]) -> bool:
    return str(child.get("controlType", "")).strip().lower() == "text" or bool(str(child.get("text", "")).strip())


def is_image_child(child: Dict[str, object]) -> bool:
    control_type = str(child.get("controlType", "")).strip().lower()
    tag = str(child.get("tag", "")).strip().lower()
    return control_type == "image" or tag == "img" or child_has_asset_type(child, "icon") or child_has_asset_type(child, "ornament")


def auto_assign_composite_child_slots(node_payload: Dict[str, object]) -> None:
    component_family = str(node_payload.get("componentFamily", "")).strip().lower()
    children = node_payload.get("children", [])
    if not component_family or not isinstance(children, list):
        return

    attributes = node_payload.get("attributes", [])
    if not isinstance(attributes, list):
        return

    explicit_attribute_names = {
        str(item.get("name", "")).strip()
        for item in attributes
        if isinstance(item, dict)
    }
    has_explicit_template_id = bool(str(node_payload.get("templateId", "")).strip()) and "data-ui-template" in explicit_attribute_names
    has_explicit_component_family = "data-ui-component-family" in explicit_attribute_names
    has_explicit_composite_element = (
        "data-ui-element" in explicit_attribute_names and
        str(node_payload.get("elementId", "")).strip().lower() in COMPOSITE_COMPONENT_FAMILIES
    )
    if not (has_explicit_template_id or has_explicit_component_family or has_explicit_composite_element):
        return

    if component_family not in COMPOSITE_COMPONENT_FAMILIES:
        return

    text_count = 0
    for child in children:
        if not isinstance(child, dict):
            continue
        if str(child.get("slotId", "")).strip():
            continue

        semantic = child_semantic_text(child)
        control_type = str(child.get("controlType", "")).strip().lower()
        slot_id = ""

        if component_family == "frame/window":
            if contains_semantic_token(semantic, "header", "title-bar", "top", "caption"):
                slot_id = "Header"
            elif contains_semantic_token(semantic, "footer", "bottom", "actions"):
                slot_id = "Footer"
            elif contains_semantic_token(semantic, "deco", "decoration", "corner", "ornament", "flare"):
                slot_id = "Decoration"
            else:
                slot_id = "Content"
        elif component_family == "button/compound":
            if contains_semantic_token(semantic, "badge", "count", "notification"):
                slot_id = "Badge"
            elif is_image_child(child) or contains_semantic_token(semantic, "icon"):
                slot_id = "Icon"
            elif is_text_child(child):
                slot_id = "Label" if text_count == 0 else "SecondaryText"
                text_count += 1
            elif contains_semantic_token(semantic, "content", "body"):
                slot_id = "Content"
            else:
                slot_id = "Decoration"
        elif component_family == "card/item":
            if contains_semantic_token(semantic, "badge", "count", "tag", "reward"):
                slot_id = "Badge"
            elif contains_semantic_token(semantic, "footer", "bottom", "actions"):
                slot_id = "Footer"
            elif is_image_child(child) or contains_semantic_token(semantic, "icon", "thumb", "avatar"):
                slot_id = "Icon"
            elif is_text_child(child):
                slot_id = "PrimaryText" if text_count == 0 else "SecondaryText"
                text_count += 1
            elif contains_semantic_token(semantic, "content", "body", "desc", "description"):
                slot_id = "Content"
            else:
                slot_id = "Decoration"
        elif component_family == "header/section":
            if control_type in {"button", "toggle", "dropdown"} or contains_semantic_token(semantic, "action", "button", "cta"):
                slot_id = "Action"
            elif is_image_child(child) or contains_semantic_token(semantic, "icon"):
                slot_id = "Icon"
            elif is_text_child(child):
                slot_id = "Title" if text_count == 0 else "Subtitle"
                text_count += 1
            else:
                slot_id = "Decoration"
        elif component_family == "list/row":
            if contains_semantic_token(semantic, "badge", "count", "tag"):
                slot_id = "Badge"
            elif control_type in {"button", "toggle", "dropdown"} or contains_semantic_token(semantic, "trailing", "meta", "right", "arrow", "chevron"):
                slot_id = "Trailing"
            elif is_image_child(child) or contains_semantic_token(semantic, "leading", "icon", "avatar", "thumb"):
                slot_id = "Leading"
            else:
                slot_id = "Content"
        elif component_family == "nav/tab":
            if contains_semantic_token(semantic, "indicator", "underline", "active-line"):
                slot_id = "Indicator"
            elif contains_semantic_token(semantic, "badge", "count", "notification"):
                slot_id = "Badge"
            elif is_image_child(child) or contains_semantic_token(semantic, "icon"):
                slot_id = "Icon"
            elif is_text_child(child):
                slot_id = "Label"
            else:
                slot_id = "Decoration"

        if slot_id:
            child["slotId"] = slot_id
            upsert_node_attribute(child, "data-ui-slot", slot_id)


def infer_control_type(node: Dict[str, object]) -> str:
    attrs = node.get("attrs", {}) if isinstance(node.get("attrs"), dict) else {}
    element_id, _ = normalize_element_identity(attrs)
    explicit = str(attrs.get("data-ui-type", "")).strip()
    if explicit:
        mapping = {
            "div": "Div",
            "text": "Text",
            "button": "Button",
            "input": "Input",
            "scroll": "Scroll",
            "scrollview": "Scroll",
            "scrollbar": "Scrollbar",
            "toggle": "Toggle",
            "slider": "Slider",
            "dropdown": "Dropdown",
            "image": "Image",
            "progress": "Progress",
        }
        return mapping.get(explicit.lower(), "Div")

    if element_id:
        mapping = {
            "button": "Button",
            "input": "Input",
            "scrollview": "Scroll",
            "scrollbar": "Scrollbar",
            "toggle": "Toggle",
            "slider": "Slider",
            "dropdown": "Dropdown",
            "image": "Image",
            "progress": "Progress",
        }
        inferred = mapping.get(element_id.lower())
        if inferred:
            return inferred

    tag = str(node.get("tag", "div")).lower()
    if tag == "button":
        return "Button"
    if tag in {"input", "textarea"}:
        return "Input"
    if tag == "img":
        return "Image"
    if tag in {"span", "p", "label", "h1", "h2", "h3", "h4", "h5", "h6"}:
        return "Text"
    return "Div"


def parse_curve_points(raw_value: object) -> List[Dict[str, object]]:
    text = str(raw_value or "").strip()
    if not text:
        return []

    try:
        parsed = json.loads(text)
    except json.JSONDecodeError:
        return []

    if not isinstance(parsed, list):
        return []

    points: List[Dict[str, object]] = []
    for item in parsed:
        if not isinstance(item, dict):
            continue
        points.append(
            {
                "x": parse_number(str(item.get("x", "")), 0.0),
                "y": parse_number(str(item.get("y", "")), 0.0),
                "tangentX": parse_number(str(item.get("tangentX", item.get("tx", ""))), 0.0),
                "tangentY": parse_number(str(item.get("tangentY", item.get("ty", ""))), 0.0),
                "tangentMode": str(item.get("tangentMode", "Manual")).strip() or "Manual",
            }
        )
    return points


def resolve_layout_mode(attrs: Dict[str, object], display: str) -> str:
    explicit = str(attrs.get("data-ui-layout", "")).strip().lower()
    if explicit in {"flex", "grid", "curve"}:
        return explicit
    if display == "flex":
        return "flex"
    return ""


def build_grid_layout(attrs: Dict[str, object], resolved_style: Dict[str, str]) -> Optional[Dict[str, object]]:
    if str(attrs.get("data-ui-layout", "")).strip().lower() != "grid":
        return None

    return {
        "columns": parse_int(str(attrs.get("data-ui-grid-columns", "")), 0),
        "rows": parse_int(str(attrs.get("data-ui-grid-rows", "")), 0),
        "layers": parse_int(str(attrs.get("data-ui-grid-layers", "")), 1),
        "cellType": str(attrs.get("data-ui-grid-cell-type", "")).strip(),
        "cellWidth": str(attrs.get("data-ui-grid-cell-width", "")).strip(),
        "cellHeight": str(attrs.get("data-ui-grid-cell-height", "")).strip(),
        "gapX": str(attrs.get("data-ui-grid-gap-x", "")).strip() or resolved_style.get("gap", ""),
        "gapY": str(attrs.get("data-ui-grid-gap-y", "")).strip() or resolved_style.get("gap", ""),
        "columnDirection": str(attrs.get("data-ui-grid-column-direction", "")).strip(),
        "rowDirection": str(attrs.get("data-ui-grid-row-direction", "")).strip(),
        "horizontalAlign": str(attrs.get("data-ui-grid-align-x", "")).strip(),
        "verticalAlign": str(attrs.get("data-ui-grid-align-y", "")).strip(),
    }


def build_curve_layout(attrs: Dict[str, object]) -> Optional[Dict[str, object]]:
    if str(attrs.get("data-ui-layout", "")).strip().lower() != "curve":
        return None

    return {
        "spacingMode": str(attrs.get("data-ui-curve-spacing-mode", "")).strip() or "Evenly",
        "spacing": parse_number(str(attrs.get("data-ui-curve-spacing", "")), 0.0),
        "startAt": parse_number(str(attrs.get("data-ui-curve-start-at", "")), 0.0),
        "rotation": str(attrs.get("data-ui-curve-rotation", "")).strip() or "None",
        "extendBefore": str(attrs.get("data-ui-curve-extend-before", "")).strip() or "Stop",
        "extendAfter": str(attrs.get("data-ui-curve-extend-after", "")).strip() or "Stop",
        "lockTangents": parse_bool(attrs.get("data-ui-curve-lock-tangents", False), False),
        "lockPositions": parse_bool(attrs.get("data-ui-curve-lock-positions", False), False),
        "points": parse_curve_points(attrs.get("data-ui-curve-points", "")),
    }


def build_layout(attrs: Dict[str, object], resolved_style: Dict[str, str]) -> Dict[str, object]:
    inset_left, inset_right, inset_top, inset_bottom = parse_inset_edges(resolved_style.get("inset", ""))
    translate_x, translate_y = parse_static_translate(resolved_style.get("transform", ""))
    display = resolved_style.get("display", "")
    if display == "inline-flex":
        display = "flex"
    layout_mode = resolve_layout_mode(attrs, display)
    return {
        "display": display,
        "layoutMode": layout_mode,
        "position": resolved_style.get("position", ""),
        "anchorHorizontal": "",
        "anchorVertical": "",
        "left": resolved_style.get("left", "") or inset_left,
        "right": resolved_style.get("right", "") or inset_right,
        "top": resolved_style.get("top", "") or inset_top,
        "bottom": resolved_style.get("bottom", "") or inset_bottom,
        "width": resolved_style.get("width", ""),
        "height": resolved_style.get("height", ""),
        "minWidth": resolved_style.get("min-width", ""),
        "maxWidth": resolved_style.get("max-width", ""),
        "minHeight": resolved_style.get("min-height", ""),
        "maxHeight": resolved_style.get("max-height", ""),
        "padding": resolved_style.get("padding", ""),
        "margin": resolved_style.get("margin", ""),
        "marginLeft": resolved_style.get("margin-left", ""),
        "marginRight": resolved_style.get("margin-right", ""),
        "marginTop": resolved_style.get("margin-top", ""),
        "marginBottom": resolved_style.get("margin-bottom", ""),
        "flex": resolved_style.get("flex", ""),
        "flexGrow": resolved_style.get("flex-grow", ""),
        "flexShrink": resolved_style.get("flex-shrink", ""),
        "gap": resolved_style.get("gap", ""),
        "justifyContent": resolved_style.get("justify-content", ""),
        "alignItems": resolved_style.get("align-items", ""),
        "flexDirection": resolved_style.get("flex-direction", ""),
        "flexWrap": resolved_style.get("flex-wrap", ""),
        "overflow": resolved_style.get("overflow", ""),
        "overflowX": resolved_style.get("overflow-x", ""),
        "overflowY": resolved_style.get("overflow-y", ""),
        "boxSizing": resolved_style.get("box-sizing", ""),
        "translateX": translate_x,
        "translateY": translate_y,
        "rotationZ": parse_rotation_z(
            str(resolved_style.get("data-ui-rotation", "")).strip() or str(resolved_style.get("transform", "")).strip()
        ),
        "gridLayout": build_grid_layout(attrs, resolved_style),
        "curveLayout": build_curve_layout(attrs),
    }


def build_text_style(resolved_style: Dict[str, str]) -> Dict[str, object]:
    return {
        "color": resolved_style.get("color", ""),
        "fontSize": resolved_style.get("font-size", ""),
        "fontFamily": resolved_style.get("font-family", ""),
        "fontWeight": resolved_style.get("font-weight", ""),
        "lineHeight": resolved_style.get("line-height", ""),
        "textAlign": resolved_style.get("text-align", ""),
        "letterSpacing": resolved_style.get("letter-spacing", ""),
        "textTransform": resolved_style.get("text-transform", ""),
    }


def build_visual(resolved_style: Dict[str, str]) -> Dict[str, object]:
    background = resolved_style.get("background", "")
    background_color = resolved_style.get("background-color", "")
    border = resolved_style.get("border", "")
    border_radius = resolved_style.get("border-radius", "")
    box_shadow = resolved_style.get("box-shadow", "")
    opacity = resolved_style.get("opacity", "")

    use_gradient, primary_color, gradient_color, gradient_direction = parse_gradient(background)
    fill_color = background_color or primary_color
    if not fill_color and is_color_literal(background):
        fill_color = background

    outline_width, outline_color = parse_border(border)
    shadow_offset_x, shadow_offset_y, shadow_blur, shadow_color = parse_shadow(box_shadow)

    return {
        "background": background,
        "backgroundColor": background_color,
        "border": border,
        "borderStyle": normalize_border_style(resolved_style),
        "borderRadius": border_radius,
        "boxShadow": box_shadow,
        "opacity": opacity,
        "enableFill": bool(fill_color and fill_color.lower() != "transparent"),
        "fillColor": fill_color,
        "useGradient": use_gradient,
        "gradientColor": gradient_color,
        "gradientDirection": gradient_direction,
        "cornerRadius": parse_px(border_radius, 0.0),
        "useMaxRoundness": False,
        "outlineWidth": outline_width,
        "outlineColor": outline_color,
        "shadowOffsetX": shadow_offset_x,
        "shadowOffsetY": shadow_offset_y,
        "shadowBlur": shadow_blur,
        "shadowColor": shadow_color,
        "enableGlow": str(resolved_style.get("-ai-glow", "")).strip().lower() not in {"", "false", "0"},
        "glowColor": resolved_style.get("-ai-glow-color", ""),
        "glowBlur": parse_number(resolved_style.get("-ai-glow-blur", ""), 0.0),
        "glowIntensity": parse_number(resolved_style.get("-ai-glow-intensity", ""), 1.0),
    }


def build_motion(node: Dict[str, object], resolved_style: Dict[str, str]) -> Tuple[str, Dict[str, object]]:
    attrs = node.get("attrs", {}) if isinstance(node.get("attrs"), dict) else {}
    motion_id = str(attrs.get("data-ui-motion", "")).strip()
    loop_preset_id, loop_delay = resolve_loop_motion_preset(attrs, resolved_style)
    if not motion_id:
        return "", {
            "presetId": "",
            "enterMotion": "",
            "hoverMotion": "",
            "pressMotion": "",
            "duration": 0.0,
            "distance": 0.0,
            "scale": 1.0,
            "ease": "",
            "loopPresetId": loop_preset_id,
            "loopDelay": loop_delay,
        }

    return motion_id, {
        "presetId": motion_id,
        "enterMotion": "",
        "hoverMotion": "",
        "pressMotion": "",
        "duration": 0.0,
        "distance": 0.0,
        "scale": 1.0,
        "ease": "",
        "loopPresetId": loop_preset_id,
        "loopDelay": loop_delay,
    }


def build_node(node: Dict[str, object], tokens: Dict[str, str]) -> Dict[str, object]:
    attrs = node.get("attrs", {}) if isinstance(node.get("attrs"), dict) else {}
    element_id, variant_id = normalize_element_identity(attrs)
    shape_id = normalize_shape_id(attrs)
    frame_id = normalize_frame_id(attrs)
    raw_style = node.get("resolvedStyle", {}) if isinstance(node.get("resolvedStyle"), dict) else {}
    resolved_style = resolve_style_dict(raw_style, tokens)
    resolved_style["data-ui-rotation"] = str(attrs.get("data-ui-rotation", "")).strip()
    motion_id, motion = build_motion(node, resolved_style)
    component_family = infer_component_family(node, attrs, resolved_style)
    component_variant = infer_component_variant(component_family, node, attrs, resolved_style) if component_family else ""
    render_strategy = infer_render_strategy(node, attrs, resolved_style)
    asset_refs = collect_asset_refs(node, attrs, resolved_style, render_strategy)
    fidelity_notes = collect_fidelity_notes(attrs, resolved_style, render_strategy)

    # Propagate geometry-lock signals emitted by export_site_snapshots.py so the Unity
    # baker can honor measured absolute rects for "locked" nodes (Track C). Nodes whose
    # stabilityLevel is "suggested" keep the current LayoutGroup + ContentSizeFitter path
    # on the Unity side.
    stability_level = str(node.get("stabilityLevel", "")).strip() or "suggested"
    absolute_rect_raw = node.get("absoluteRect") if isinstance(node.get("absoluteRect"), dict) else None
    if absolute_rect_raw:
        absolute_rect = {
            "x": float(absolute_rect_raw.get("x", 0.0) or 0.0),
            "y": float(absolute_rect_raw.get("y", 0.0) or 0.0),
            "width": float(absolute_rect_raw.get("width", 0.0) or 0.0),
            "height": float(absolute_rect_raw.get("height", 0.0) or 0.0),
            "measured": bool(absolute_rect_raw.get("measured", False)),
            "source": str(absolute_rect_raw.get("source", "")).strip(),
        }
    else:
        absolute_rect = {
            "x": 0.0,
            "y": 0.0,
            "width": 0.0,
            "height": 0.0,
            "measured": False,
            "source": "",
        }

    children = [build_node(child, tokens) for child in node.get("children", []) if isinstance(child, dict)]

    payload = {
        "name": str(node.get("name", "")).strip() or "Node",
        "tag": str(node.get("tag", "div")).strip() or "div",
        "controlType": infer_control_type(node),
        "role": str(node.get("role", "")).strip(),
        "elementId": element_id,
        "variantId": variant_id,
        "shapeId": shape_id,
        "frameId": frame_id,
        "slotId": str(attrs.get("data-ui-slot", "")).strip(),
        "containerId": str(attrs.get("data-ui-container", "")).strip(),
        "templateId": str(attrs.get("data-ui-template", "")).strip(),
        "componentFamily": component_family,
        "componentVariant": component_variant,
        "renderStrategy": render_strategy,
        "motionId": motion_id,
        "text": str(node.get("directText", "")).strip(),
        "assetRefs": asset_refs,
        "fidelityNotes": fidelity_notes,
        "classes": list(node.get("classes", [])) if isinstance(node.get("classes"), list) else [],
        "attributes": [
            {
                "name": str(name),
                "value": (
                    element_id if str(name) == "data-ui-element" and element_id else
                    variant_id if str(name) == "data-ui-variant" and variant_id else
                    shape_id if str(name) == "data-ui-shape" and shape_id else
                    frame_id if str(name) == "data-ui-frame" and frame_id else
                    component_family if str(name) == "data-ui-component-family" and component_family else
                    component_variant if str(name) == "data-ui-component-variant" and component_variant else
                    render_strategy if str(name) == "data-ui-render-strategy" and render_strategy else
                    str(value)
                ),
            }
            for name, value in attrs.items()
            if value not in (None, "")
        ],
        "stabilityLevel": stability_level,
        "absoluteRect": absolute_rect,
        "layout": build_layout(attrs, resolved_style),
        "visual": build_visual(resolved_style),
        "textStyle": build_text_style(resolved_style),
        "motion": motion,
        "children": children,
    }

    auto_assign_composite_child_slots(payload)
    return payload


def resolve_role_style(validator: SitePackageValidator, rules, selector: str, tokens: Dict[str, str]) -> Dict[str, str]:
    return resolve_style_dict(validator._resolve_rule_block(rules, selector), tokens)


def build_visual_preset(preset_id: str, resolved_style: Dict[str, str]) -> Dict[str, object]:
    visual = build_visual(resolved_style)
    return {
        "presetId": preset_id,
        "enableFill": visual["enableFill"],
        "fillColor": visual["fillColor"],
        "useGradient": visual["useGradient"],
        "gradientColor": visual["gradientColor"],
        "gradientDirection": visual["gradientDirection"],
        "cornerRadius": visual["cornerRadius"],
        "useMaxRoundness": visual["useMaxRoundness"],
        "outlineWidth": visual["outlineWidth"],
        "outlineColor": visual["outlineColor"],
        "shadowOffsetX": visual["shadowOffsetX"],
        "shadowOffsetY": visual["shadowOffsetY"],
        "shadowBlur": visual["shadowBlur"],
        "shadowColor": visual["shadowColor"],
        "enableGlow": visual["enableGlow"],
        "glowColor": visual["glowColor"],
        "glowBlur": visual["glowBlur"],
        "glowIntensity": visual["glowIntensity"],
    }


def build_theme(validator: SitePackageValidator, theme_sheet, widget_sheet, tokens: Dict[str, str]) -> Dict[str, object]:
    all_rules = list(theme_sheet.rules) + list(widget_sheet.rules)
    body_style = resolve_role_style(validator, all_rules, "body", tokens)
    window_main = resolve_role_style(validator, all_rules, '[data-ui-role="window/main"]', tokens)
    panel_primary = resolve_role_style(validator, all_rules, '[data-ui-role="panel/primary"]', tokens)
    card_info = resolve_role_style(validator, all_rules, '[data-ui-role="card/info"]', tokens)
    button_primary = resolve_role_style(validator, all_rules, '[data-ui-role="button/primary"]', tokens)
    text_title = resolve_role_style(validator, all_rules, '[data-ui-role="text/title"]', tokens)
    text_muted = resolve_role_style(validator, all_rules, '[data-ui-role="text/muted"]', tokens)

    page_background = window_main.get("background-color") or body_style.get("background-color") or tokens.get("--page-bg", "#101010")
    panel_fill = panel_primary.get("background-color", tokens.get("--panel-fill", "#1e1e1e"))
    card_fill = card_info.get("background-color", tokens.get("--card-fill", panel_fill))
    button_fill = button_primary.get("background-color", tokens.get("--accent-yellow", panel_fill))
    text_primary = text_title.get("color", body_style.get("color", tokens.get("--text-primary", "#ffffff")))
    text_secondary = text_muted.get("color", tokens.get("--text-muted", text_primary))
    _, outline_color = parse_border(panel_primary.get("border", ""))
    _, _, _, shadow_color = parse_shadow(panel_primary.get("box-shadow", ""))

    return {
        "themeId": "compiled_theme",
        "displayName": "Compiled Theme",
        "pageBackground": page_background,
        "panelFill": panel_fill,
        "cardFill": card_fill,
        "buttonFill": button_fill,
        "accentColor": tokens.get("--accent-yellow", button_fill),
        "textPrimary": text_primary,
        "textSecondary": text_secondary,
        "outlineColor": outline_color or "rgba(255,255,255,0.12)",
        "shadowColor": shadow_color or "rgba(0,0,0,0.25)",
        "tokens": [{"tokenId": key, "value": value} for key, value in sorted(tokens.items())],
        "visualPresets": [
            build_visual_preset("panel/default", panel_primary),
            build_visual_preset("card/default", card_info),
            build_visual_preset("button/default", button_primary),
        ],
        "motionPresets": [
            {
                "presetId": "motion/default",
                "enterMotion": "Fade",
                "hoverMotion": "HoverLift",
                "pressMotion": "ScaleIn",
                "duration": 0.22,
                "distance": 26.0,
                "scale": 0.96,
                "ease": "OutCubic",
            },
            {
                "presetId": "motion/page",
                "enterMotion": "Fade",
                "hoverMotion": "None",
                "pressMotion": "None",
                "duration": 0.28,
                "distance": 24.0,
                "scale": 1.0,
                "ease": "OutCubic",
            },
            {
                "presetId": "motion/button",
                "enterMotion": "None",
                "hoverMotion": "HoverLift",
                "pressMotion": "ScaleIn",
                "duration": 0.18,
                "distance": 14.0,
                "scale": 0.96,
                "ease": "OutCubic",
            },
        ],
        "loopMotionPresets": [
            {
                "presetId": "loop/rotate-slow",
                "loopType": "Rotate",
                "duration": 20.0,
                "amplitude": 1.0,
                "ease": "Linear",
            },
            {
                "presetId": "loop/rotate-slow-reverse",
                "loopType": "RotateReverse",
                "duration": 15.0,
                "amplitude": 1.0,
                "ease": "Linear",
            },
            {
                "presetId": "loop/float-soft",
                "loopType": "Float",
                "duration": 8.0,
                "amplitude": 20.0,
                "ease": "InOutSine",
            },
            {
                "presetId": "loop/pulse-soft",
                "loopType": "Pulse",
                "duration": 3.0,
                "amplitude": 0.06,
                "ease": "InOutSine",
            },
        ],
    }


def write_compile_report(report_path: Path, status: str, stage: str, site_id: str, errors: List[str], warnings: List[str], bundle_path: Optional[Path], downgrades: Optional[List[Dict[str, str]]] = None) -> None:
    payload = {
        "status": status,
        "stage": stage,
        "siteId": site_id,
        "errorCount": len(errors),
        "warningCount": len(warnings),
        "downgradeCount": len(downgrades or []),
        "errors": errors,
        "warnings": warnings,
        "downgrades": downgrades or [],
        "bundlePath": bundle_path.as_posix() if bundle_path else "",
    }
    report_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def extract_downgrades(warnings: List[str]) -> List[Dict[str, str]]:
    """Classify validator warnings into structured downgrade records so AI / Unity can tell
    which visual information the pipeline silently normalised away.

    This does not mutate warnings; it only projects a summary view. Matches everything that
    looks like a CSS vocabulary or browser-only complaint. Visual-loss-free items (e.g.
    preview-only selectors, -webkit-* properties) are still emitted so the report is complete,
    but tagged action=preview-only.
    """
    records: List[Dict[str, str]] = []
    for raw in warnings:
        if not isinstance(raw, str) or not raw:
            continue
        loc_match = LOCATION_REGEX.match(raw)
        location = loc_match.group("loc") if loc_match else ""
        sel_match = SELECTOR_REGEX.search(raw)
        selector = sel_match.group("selector") if sel_match else ""
        for pattern, feature, action in DOWNGRADE_PATTERNS:
            m = pattern.search(raw)
            if not m:
                continue
            resolved_feature = feature
            if resolved_feature is None:
                try:
                    resolved_feature = m.group("prop")
                except (IndexError, KeyError):
                    pass
            if resolved_feature is None:
                try:
                    resolved_feature = m.group("label")
                except (IndexError, KeyError):
                    pass
            resolved_feature = resolved_feature or "unknown"
            records.append({
                "feature": resolved_feature,
                "action": action,
                "location": location,
                "selector": selector,
                "message": raw,
            })
            break
    return records


def collect_bundle_assets(pages: List[Dict[str, object]]) -> List[Dict[str, object]]:
    catalog: Dict[str, Dict[str, object]] = {}

    def walk(node: Dict[str, object]) -> None:
        if not isinstance(node, dict):
            return

        node_name = str(node.get("name", "")).strip() or "Node"
        for asset_ref in node.get("assetRefs", []) if isinstance(node.get("assetRefs"), list) else []:
            if not isinstance(asset_ref, dict):
                continue
            asset_id = str(asset_ref.get("assetId", "")).strip()
            if not asset_id:
                continue

            entry = catalog.setdefault(
                asset_id,
                {
                    "assetId": asset_id,
                    "assetType": str(asset_ref.get("assetType", "")).strip().lower() or "icon",
                    "usage": str(asset_ref.get("usage", "")).strip(),
                    "importMode": str(asset_ref.get("importMode", "")).strip().lower() or "auto",
                    "source": str(asset_ref.get("source", "")).strip(),
                    "linkedNodeNames": [],
                    "notes": str(asset_ref.get("notes", "")).strip(),
                },
            )
            linked_names = entry.get("linkedNodeNames", [])
            if node_name not in linked_names:
                linked_names.append(node_name)

        for child in node.get("children", []) if isinstance(node.get("children"), list) else []:
            walk(child)

    for page in pages:
        if not isinstance(page, dict):
            continue
        root = page.get("root")
        if isinstance(root, dict):
            walk(root)

    return list(catalog.values())


def write_repair_prompt(prompt_path: Path, stage: str, errors: List[str], warnings: List[str], report_path: Path) -> None:
    lines = [
        "# AIToUGUI Compile Repair Prompt",
        "",
        f"Current stage: `{stage}`",
        f"Report file: `{report_path.as_posix()}`",
        "",
        "Fix only the blocking issues below, then rerun `compile_site_bundle.py`.",
        "",
        "## Blocking Errors",
    ]
    if errors:
        lines.extend([f"- {error}" for error in errors])
    else:
        lines.append("- No blocking errors were captured.")

    if warnings:
        lines.extend(["", "## Warnings"])
        lines.extend([f"- {warning}" for warning in warnings])

    prompt_path.write_text("\n".join(lines), encoding="utf-8")


def collect_validation_details(report: Dict[str, object]) -> Tuple[List[str], List[str]]:
    errors: List[str] = []
    warnings: List[str] = []

    for error in report.get("violations", []) if isinstance(report.get("violations"), list) else []:
        if isinstance(error, str):
            errors.append(error)

    for warning in report.get("warnings", []) if isinstance(report.get("warnings"), list) else []:
        if isinstance(warning, str):
            warnings.append(warning)

    for page_report in report.get("pageReports", []) if isinstance(report.get("pageReports"), list) else []:
        if not isinstance(page_report, dict):
            continue
        page_id = str(page_report.get("pageId", "")).strip() or "<unknown-page>"
        # Align with validator's severity model (see PageValidationReport.finalize): only
        # contract-level buckets contribute to errors. Style-level buckets become warnings,
        # where extract_downgrades() will classify them into structured downgrade records.
        for field_name in (
            "errors",
            "missingRequiredAttributes",
            "missingRequiredSizeNodes",
            "contractMismatches",
            "metadataMismatches",
        ):
            values = page_report.get(field_name, [])
            if not isinstance(values, list):
                continue
            for value in values:
                if isinstance(value, str):
                    errors.append(f"[{page_id}] {value}")

        for field_name in (
            "warnings",
            "undefinedClasses",
            "undefinedRoles",
            "unsupportedTags",
            "unsupportedAttributes",
            "unsupportedSelectors",
            "unsupportedProperties",
            "unsupportedValuePatterns",
            "browserOnlyPatterns",
        ):
            values = page_report.get(field_name, [])
            if not isinstance(values, list):
                continue
            for value in values:
                if isinstance(value, str):
                    warnings.append(f"[{page_id}] {value}")

    return errors, warnings


def compile_bundle(
    site_root: Path,
    output_dir: Path,
    allowlist_path: Optional[Path],
    allow_legacy_metadata: bool,
    reuse_existing_snapshots: bool,
    force_compile: bool = False,
) -> int:
    layout = resolve_site_package_layout(site_root)
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / "compile_report.json"
    prompt_path = output_dir / "compile_repair_prompt.md"

    validator = SitePackageValidator(
        site_root=site_root,
        allowlist_path=allowlist_path,
        allow_legacy_metadata=allow_legacy_metadata,
    )
    validation_report = validator.validate()
    site_id = str(validation_report.get("siteId", "site_id"))
    validation_errors, validation_warnings = collect_validation_details(validation_report)

    if validation_report.get("status") != "pass" and not force_compile:
        if not validation_errors:
            validation_errors.append("Validation failed. Fix contract-level errors before compile.")
        downgrades = extract_downgrades(validation_warnings)
        write_compile_report(report_path, "fail", "validation", site_id, validation_errors, validation_warnings, None, downgrades)
        write_repair_prompt(prompt_path, "validation", validation_errors, validation_warnings, report_path)
        return 1

    outputs: Dict[str, Path]
    compiled_pages_dir = layout.snapshots_root / "compiled_pages"
    compiled_site_snapshot = layout.snapshots_root / "compiled_site.json"
    can_reuse_snapshots = reuse_existing_snapshots and compiled_pages_dir.exists() and compiled_site_snapshot.exists()
    if can_reuse_snapshots:
        outputs = {
            "compiled_site": compiled_site_snapshot,
            "compiled_pages_dir": compiled_pages_dir,
            "layout_snapshots_dir": layout.snapshots_root / "layout_snapshots",
        }
    else:
        outputs = export_site_snapshots(
            site_root=site_root,
            output_root=layout.snapshots_root,
            allowlist_path=allowlist_path,
            allow_legacy_metadata=allow_legacy_metadata,
        )

    manifest = load_manifest(layout)
    theme_sheet = validator._parse_css_sheet(layout.source_root / "theme.css", "theme")
    widget_sheet = validator._parse_css_sheet(layout.source_root / "shared" / "widgets.css", "widgets")
    token_source = validator._resolve_rule_block(theme_sheet.rules, ":root")
    tokens = {key: resolve_tokens_in_value(str(value), token_source) for key, value in token_source.items() if key.startswith("--")}

    errors: List[str] = []
    warnings: List[str] = list(theme_sheet.warnings) + list(widget_sheet.warnings)
    warnings.extend(validation_warnings)
    sheet_errors = list(theme_sheet.errors) + list(widget_sheet.errors)
    # Diagnostic path: when --force-compile surfaced past a failing validation, roll the
    # contract-level errors into warnings so the bundle still gets written. Mark the output
    # with `compiled_with_force` so downstream tools know the bundle is not production-ready.
    compiled_with_force = False
    if force_compile and (validation_errors or sheet_errors):
        compiled_with_force = True
        total = len(validation_errors) + len(sheet_errors)
        warnings.append(
            f"[force-compile] Strict validation reported {total} contract-level errors; "
            f"bundle was compiled anyway for diagnostic inspection. Do NOT treat this as a "
            f"passing build -- fix the contract errors before shipping."
        )
        warnings.extend([f"[force-compile] {error}" for error in validation_errors])
        warnings.extend([f"[force-compile] {error}" for error in sheet_errors])
    else:
        # theme/widgets sheet.errors only contain truly blocking issues (missing stylesheet);
        # style-level complaints live on sheet.warnings after the Track A severity rework.
        errors.extend(sheet_errors)

    pages: List[Dict[str, object]] = []
    compiled_pages_dir = outputs["compiled_pages_dir"]
    for manifest_page in manifest.get("pages", []):
        if not isinstance(manifest_page, dict):
            continue

        page_id = str(manifest_page.get("pageId", "")).strip()
        compiled_page_path = compiled_pages_dir / f"{page_id}.compiled_page.json"
        if not compiled_page_path.exists():
            errors.append(f"Missing compiled page snapshot for '{page_id}'.")
            continue

        page_snapshot = json.loads(compiled_page_path.read_text(encoding="utf-8"))
        tree = page_snapshot.get("tree")
        if not isinstance(tree, dict):
            errors.append(f"Compiled page '{page_id}' does not contain a valid tree.")
            continue

        page_warnings = page_snapshot.get("warnings", [])
        warnings.extend([f"[{page_id}] {warning}" for warning in page_warnings if isinstance(warning, str)])

        runtime_page_id = str(manifest_page.get("runtimePageId", "")).strip()
        if not runtime_page_id:
            site_id_for_page = str(manifest.get("siteId", "")).strip()
            runtime_page_id = f"{site_id_for_page}/{page_id}" if site_id_for_page and page_id else page_id

        pages.append(
            {
                "pageId": page_id,
                "runtimePageId": runtime_page_id,
                "displayName": str(manifest_page.get("displayName", "")).strip() or page_id,
                "sourceRelativePath": str(manifest_page.get("html", "")).strip(),
                "prefabName": str(manifest_page.get("prefabName", "")).strip() or "CompiledPage",
                "targetLayer": str(manifest_page.get("targetLayer", "")).strip() or "Normal",
                "logicalPath": f"UI/Generated/{manifest.get('siteId', 'site_id')}/{str(manifest_page.get('prefabName', '')).strip() or 'CompiledPage'}",
                "root": build_node(tree, tokens),
            }
        )

    root_bundle_path = layout.bundle_path
    downgrades = extract_downgrades(warnings)
    if errors:
        write_compile_report(report_path, "fail", "compile", site_id, errors, warnings, None, downgrades)
        write_repair_prompt(prompt_path, "compile", errors, warnings, report_path)
        return 1

    bundle_assets = collect_bundle_assets(pages)
    bundle_payload = {
        "schemaVersion": "1.1",
        "compiledWithForce": compiled_with_force,
        "site": {
            "siteId": str(manifest.get("siteId", "site_id")),
            "displayName": str(manifest.get("displayName", "Compiled Site")),
            "designWidth": int(manifest.get("designWidth", 1920)),
            "designHeight": int(manifest.get("designHeight", 1080)),
            "prefabOutputRoot": str(manifest.get("prefabOutputRoot", "Assets/Prefabs/UI/Generated")),
            "metadataOutputRoot": str(manifest.get("metadataOutputRoot", "Assets/DataConfig/UI/Generated")),
        },
        "theme": build_theme(validator, theme_sheet, widget_sheet, tokens),
        "assets": bundle_assets,
        "downgrades": downgrades,
        "pages": pages,
    }

    bundle_text = json.dumps(bundle_payload, ensure_ascii=False, indent=2)
    root_bundle_path.write_text(bundle_text, encoding="utf-8")
    write_compile_report(report_path, "pass", "compile", site_id, warnings=warnings, errors=[], bundle_path=root_bundle_path, downgrades=downgrades)
    write_preview_entry(layout, manifest)
    write_task_report(layout)
    if prompt_path.exists():
        prompt_path.unlink()
    return 0


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    site_root = args.site_root.resolve()
    if not site_root.exists() or not site_root.is_dir():
        parser.error(f"site_root does not exist or is not a directory: {site_root}")

    layout = resolve_site_package_layout(site_root)
    output_dir = args.output_dir.resolve() if args.output_dir else layout.reports_root
    allowlist = args.allowlist.resolve() if args.allowlist else None
    exit_code = compile_bundle(
        site_root,
        output_dir,
        allowlist,
        args.allow_legacy_metadata,
        args.reuse_existing_snapshots,
        args.force_compile,
    )

    report_path = output_dir / "compile_report.json"
    print(f"[AIToUGUI Compile] report={report_path}")
    if exit_code == 0:
        print(f"[AIToUGUI Compile] bundle={layout.bundle_path}")
    else:
        print(f"[AIToUGUI Compile] repair={(output_dir / 'compile_repair_prompt.md')}")
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
