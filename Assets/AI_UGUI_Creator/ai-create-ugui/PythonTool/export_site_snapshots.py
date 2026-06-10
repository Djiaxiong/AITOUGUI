#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple

from site_package_layout import resolve_site_package_layout
from validate_site_package import (
    CssRule,
    GeometryFrame,
    HtmlNode,
    LimitedHtmlTreeBuilder,
    PageValidationReport,
    SitePackageValidator,
    normalize_whitespace,
    parse_px_value,
)


TEXT_TAGS = {"span", "p", "h1", "h2", "h3", "h4", "h5", "h6", "label"}
LAYOUT_STYLE_KEYS = (
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
    "padding-left",
    "padding-right",
    "padding-top",
    "padding-bottom",
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
VISUAL_STYLE_KEYS = (
    "background",
    "background-color",
    "border",
    "border-radius",
    "box-shadow",
    "opacity",
)
PX_VALUE_REGEX = re.compile(r"^-?(?:\d+|\d*\.\d+)px$")
PERCENT_VALUE_REGEX = re.compile(r"^-?(?:\d+|\d*\.\d+)%$")

# Roles/role-prefixes whose nodes are load-bearing for layout -- Unity should honor the
# snapshot-measured geometry exactly (stabilityLevel="locked") rather than re-running
# flex/auto-layout at bake time. Everything else defaults to "suggested" which preserves
# the existing LayoutGroup + ContentSizeFitter behavior.
LOCKED_ROLE_PREFIXES: Tuple[str, ...] = (
    "panel/",
    "card/",
    "hud/",
    "modal/",
    "dialog/",
    "progress/",
    "stat/",
    "resource/",
    "title/",
    "title-bar/",
    "chip/",
    "toolbar/",
    "banner/",
    "sidebar/",
    "header/",
    "footer/",
    "nav/",
)
LOCKED_ROLES_EXACT: Set[str] = {
    "screen-root",
    "main-panel",
    "hud-block",
    "modal-panel",
    "resource-block",
    "stat-row",
    "progress-track",
    "progress-fill",
    "chip",
}


def _compute_stability_level(
    node: HtmlNode,
    resolved_style: Dict[str, str],
    primitive_id: str,
    is_page_root: bool,
    has_measured_rect: bool,
) -> str:
    """Classify whether Unity should treat this node's measured geometry as locked ground
    truth or as a suggestion that may be re-flowed by LayoutGroup/ContentSizeFitter.

    locked    -- page root, primitive controls (button/input/toggle/slider/...),
                 known sizing-critical roles, any node with an explicit px size, or any node
                 whose geometry was fully resolved by the Python flex/block resolver and
                 therefore carries a measured absoluteRect.
    suggested -- text leaves, chip text children, implicit containers whose geometry comes
                 from content flow. Unity keeps its current behavior.
    """
    if is_page_root:
        return "locked"
    if node.tag == "button" or (node.role and node.role.startswith("button/")):
        return "locked"
    if primitive_id:
        return "locked"
    role = node.role or ""
    if role in LOCKED_ROLES_EXACT:
        return "locked"
    for prefix in LOCKED_ROLE_PREFIXES:
        if role.startswith(prefix):
            return "locked"
    width_px = parse_px_value(resolved_style.get("width", ""))
    height_px = parse_px_value(resolved_style.get("height", ""))
    if width_px is not None and height_px is not None:
        return "locked"
    # Promote nodes whose geometry the resolver fully determined (e.g. flex:1 form-panel
    # whose width comes from flex distribution, or flex-centered cards). Text leaves stay
    # suggested even when measured because their height derives from line-height metrics.
    if has_measured_rect and node.tag not in TEXT_TAGS and not node.direct_text:
        return "locked"
    return "suggested"


def _parse_length_with_parent(value: str, parent_size: float) -> Optional[float]:
    """Parse a CSS length token (px, %, 0) against a known parent axis size. Returns None
    when the token is not convertible (auto, keywords, vars, calc...)."""
    if value is None:
        return None
    v = value.strip().lower()
    if not v:
        return None
    if v in {"0", "0px"}:
        return 0.0
    if v.endswith("px"):
        try:
            return float(v[:-2])
        except ValueError:
            return None
    if v.endswith("%"):
        try:
            return float(v[:-1]) * parent_size / 100.0
        except ValueError:
            return None
    return None


def _parse_box_sides(value: str, parent_width: float, parent_height: float) -> Tuple[float, float, float, float]:
    """Parse a CSS shorthand box value (padding / margin) as (top, right, bottom, left).
    Percentages in CSS boxes resolve against the parent's width on every side, mirroring
    browser behaviour."""
    if not value:
        return (0.0, 0.0, 0.0, 0.0)
    parts = [p for p in value.strip().split() if p]

    def _px(token: str) -> float:
        parsed = _parse_length_with_parent(token, parent_width)
        return parsed if parsed is not None else 0.0

    if len(parts) == 1:
        v = _px(parts[0])
        return (v, v, v, v)
    if len(parts) == 2:
        a, b = _px(parts[0]), _px(parts[1])
        return (a, b, a, b)
    if len(parts) == 3:
        return (_px(parts[0]), _px(parts[1]), _px(parts[2]), _px(parts[1]))
    return (_px(parts[0]), _px(parts[1]), _px(parts[2]), _px(parts[3]))


def _parse_flex_grow(style: Dict[str, str]) -> float:
    """Return the flex-grow factor, considering explicit `flex-grow` and the shorthand
    `flex: <grow> <shrink> <basis>` form."""
    raw_grow = style.get("flex-grow", "").strip()
    if raw_grow:
        try:
            return max(0.0, float(raw_grow))
        except ValueError:
            pass
    flex = style.get("flex", "").strip().lower()
    if not flex or flex == "none":
        return 0.0
    if flex == "auto":
        return 1.0
    first = flex.split()[0]
    try:
        return max(0.0, float(first))
    except ValueError:
        return 0.0


def _resolve_geometry_tree(
    page_root: Optional[HtmlNode],
    resolved_styles: Dict[int, Dict[str, str]],
    design_width: int,
    design_height: int,
) -> Dict[int, GeometryFrame]:
    """Compute absolute page-relative frames for every node whose geometry is statically
    derivable from CSS: page root, absolute-positioned nodes (pixel OR percentage offsets),
    flex children (including flex:1 growth, justify-content, align-items), and block
    children with explicit sizes. This is the ground truth the Unity baker pins locked
    nodes to -- nothing needs to be re-measured at bake time.

    Nodes whose size can't be resolved (implicit content-sized containers, text leaves,
    flex children without explicit size or flex-grow) are simply omitted; Unity keeps
    its current LayoutGroup/flex fallback for them.
    """
    frames: Dict[int, GeometryFrame] = {}
    if page_root is None:
        return frames
    frames[id(page_root)] = GeometryFrame(
        left=0.0,
        top=0.0,
        width=float(design_width),
        height=float(design_height),
    )
    _resolve_children(page_root, frames, resolved_styles)
    return frames


def _resolve_children(parent: HtmlNode, frames: Dict[int, GeometryFrame], resolved_styles: Dict[int, Dict[str, str]]) -> None:
    parent_frame = frames.get(id(parent))
    if parent_frame is None:
        return
    parent_style = resolved_styles.get(id(parent), {})
    padding = _parse_box_sides(parent_style.get("padding", ""), parent_frame.width, parent_frame.height)
    inner = GeometryFrame(
        left=parent_frame.left + padding[3],
        top=parent_frame.top + padding[0],
        width=max(0.0, parent_frame.width - padding[1] - padding[3]),
        height=max(0.0, parent_frame.height - padding[0] - padding[2]),
    )

    abs_children: List[HtmlNode] = []
    flow_children: List[HtmlNode] = []
    for child in parent.children:
        if child.tag in {"#document", "br"}:
            continue
        cs = resolved_styles.get(id(child), {})
        if cs.get("position", "").strip().lower() in {"absolute", "fixed"}:
            abs_children.append(child)
        else:
            flow_children.append(child)

    for child in abs_children:
        frame = _resolve_absolute_frame(resolved_styles.get(id(child), {}), parent_frame)
        if frame is not None:
            frames[id(child)] = frame

    display = parent_style.get("display", "").strip().lower()
    if display == "flex":
        _resolve_flex_frames(parent, flow_children, inner, frames, resolved_styles)
    elif flow_children:
        _resolve_block_frames(flow_children, inner, frames, resolved_styles)

    for child in abs_children + flow_children:
        _resolve_children(child, frames, resolved_styles)


def _resolve_absolute_frame(style: Dict[str, str], parent_frame: GeometryFrame) -> Optional[GeometryFrame]:
    """Compute an absolute-positioned node's frame from left/right/top/bottom/width/height
    against the nearest positioned ancestor (approximated here as the direct parent frame,
    which is correct when intermediate ancestors are static/relative flow containers)."""
    inset = style.get("inset", "").strip()
    if inset and all(not style.get(key, "") for key in ("left", "right", "top", "bottom")):
        box = _parse_box_sides(inset, parent_frame.width, parent_frame.height)
        # inset: top right bottom left
        return GeometryFrame(
            left=parent_frame.left + box[3],
            top=parent_frame.top + box[0],
            width=max(0.0, parent_frame.width - box[1] - box[3]),
            height=max(0.0, parent_frame.height - box[0] - box[2]),
        )
    left = _parse_length_with_parent(style.get("left", ""), parent_frame.width)
    right = _parse_length_with_parent(style.get("right", ""), parent_frame.width)
    top = _parse_length_with_parent(style.get("top", ""), parent_frame.height)
    bottom = _parse_length_with_parent(style.get("bottom", ""), parent_frame.height)
    width = _parse_length_with_parent(style.get("width", ""), parent_frame.width)
    height = _parse_length_with_parent(style.get("height", ""), parent_frame.height)
    if width is None and left is not None and right is not None:
        width = max(0.0, parent_frame.width - left - right)
    if height is None and top is not None and bottom is not None:
        height = max(0.0, parent_frame.height - top - bottom)
    if width is None or height is None:
        return None
    if left is None:
        if right is None:
            return None
        left = parent_frame.width - right - width
    if top is None:
        if bottom is None:
            return None
        top = parent_frame.height - bottom - height
    return GeometryFrame(
        left=parent_frame.left + left,
        top=parent_frame.top + top,
        width=width,
        height=height,
    )


def _resolve_flex_frames(
    parent: HtmlNode,
    flow_children: List[HtmlNode],
    inner: GeometryFrame,
    frames: Dict[int, GeometryFrame],
    resolved_styles: Dict[int, Dict[str, str]],
) -> None:
    """Resolve a flex container's children. Handles row + column direction, gap,
    justify-content (flex-start/center/flex-end/space-between), align-items
    (flex-start/center/flex-end/stretch), and flex-grow distribution of remaining space.
    Children whose main-axis size is neither explicit nor expanded via flex-grow are
    skipped -- they remain unmeasured and Unity falls back to its runtime flex calc for them.
    """
    if not flow_children:
        return
    parent_style = resolved_styles.get(id(parent), {})
    direction = parent_style.get("flex-direction", "row").strip().lower() or "row"
    is_row = direction == "row"
    main_size = inner.width if is_row else inner.height
    cross_size = inner.height if is_row else inner.width
    gap = _parse_length_with_parent(parent_style.get("gap", ""), main_size) or 0.0
    justify = parent_style.get("justify-content", "").strip().lower()
    align = parent_style.get("align-items", "").strip().lower()

    child_mains: List[Optional[float]] = []
    child_crosses: List[Optional[float]] = []
    child_grows: List[float] = []
    for child in flow_children:
        cs = resolved_styles.get(id(child), {})
        main_token = cs.get("width" if is_row else "height", "")
        cross_token = cs.get("height" if is_row else "width", "")
        child_mains.append(_parse_length_with_parent(main_token, main_size))
        child_crosses.append(_parse_length_with_parent(cross_token, cross_size))
        child_grows.append(_parse_flex_grow(cs))

    fixed_main = sum(m for m in child_mains if m is not None)
    total_gap = gap * max(0, len(flow_children) - 1)
    remaining = max(0.0, main_size - fixed_main - total_gap)
    grow_sum = sum(
        g for g, m in zip(child_grows, child_mains) if m is None and g > 0.0
    )

    resolved_mains: List[Optional[float]] = []
    for cm, grow in zip(child_mains, child_grows):
        if cm is not None:
            resolved_mains.append(cm)
        elif grow > 0.0 and grow_sum > 0.0:
            resolved_mains.append(remaining * grow / grow_sum)
        else:
            resolved_mains.append(None)

    resolved_crosses: List[Optional[float]] = []
    for cc in child_crosses:
        if cc is not None:
            resolved_crosses.append(cc)
        elif align in {"", "stretch"}:
            resolved_crosses.append(cross_size)
        else:
            resolved_crosses.append(None)

    mains_total = sum(m for m in resolved_mains if m is not None) + total_gap
    start_main = 0.0
    actual_gap = gap
    if justify == "center":
        start_main = max(0.0, (main_size - mains_total) / 2.0)
    elif justify in {"flex-end", "end"}:
        start_main = max(0.0, main_size - mains_total)
    elif justify == "space-between" and len(flow_children) > 1:
        fixed_sum = sum(m for m in resolved_mains if m is not None)
        actual_gap = max(gap, (main_size - fixed_sum) / max(1, len(flow_children) - 1))

    cursor = start_main
    for child, main_len, cross_len in zip(flow_children, resolved_mains, resolved_crosses):
        if main_len is None or cross_len is None:
            # Keep the cursor advancing conservatively so siblings still land; skip framing.
            cursor += 0.0 + actual_gap
            continue
        if align == "center":
            cross_offset = max(0.0, (cross_size - cross_len) / 2.0)
        elif align in {"flex-end", "end"}:
            cross_offset = max(0.0, cross_size - cross_len)
        else:
            cross_offset = 0.0
        if is_row:
            frames[id(child)] = GeometryFrame(
                left=inner.left + cursor,
                top=inner.top + cross_offset,
                width=main_len,
                height=cross_len,
            )
        else:
            frames[id(child)] = GeometryFrame(
                left=inner.left + cross_offset,
                top=inner.top + cursor,
                width=cross_len,
                height=main_len,
            )
        cursor += main_len + actual_gap


def _resolve_block_frames(
    flow_children: List[HtmlNode],
    inner: GeometryFrame,
    frames: Dict[int, GeometryFrame],
    resolved_styles: Dict[int, Dict[str, str]],
) -> None:
    """Stack flow children top-down with explicit sizes. Children missing size info are
    simply skipped rather than approximated, since block layout without explicit height is
    inherently content-dependent."""
    y = 0.0
    for child in flow_children:
        cs = resolved_styles.get(id(child), {})
        width = _parse_length_with_parent(cs.get("width", ""), inner.width)
        height = _parse_length_with_parent(cs.get("height", ""), inner.height)
        margin_top = _parse_length_with_parent(cs.get("margin-top", ""), inner.height) or 0.0
        margin_bottom = _parse_length_with_parent(cs.get("margin-bottom", ""), inner.height) or 0.0
        if width is None:
            width = inner.width
        y += margin_top
        if height is not None:
            frames[id(child)] = GeometryFrame(
                left=inner.left,
                top=inner.top + y,
                width=width,
                height=height,
            )
            y += height
        y += margin_bottom


def _compute_absolute_rect(
    validator: SitePackageValidator,
    node: HtmlNode,
    page_root: Optional[HtmlNode],
    resolved_styles: Dict[int, Dict[str, str]],
    frame_cache: Dict[int, GeometryFrame],
    design_width: int,
    design_height: int,
) -> Optional[Dict[str, object]]:
    """Look up the pre-resolved frame for `node` in frame_cache (built by
    _resolve_geometry_tree at page start) and project it into the snapshot payload."""
    frame = frame_cache.get(id(node))
    if frame is None:
        return None
    source = "page-root" if page_root is not None and node is page_root else "resolved-layout"
    return {
        "x": frame.left,
        "y": frame.top,
        "width": frame.width,
        "height": frame.height,
        "measured": True,
        "source": source,
    }


def _clean_attrs(attrs: Dict[str, str]) -> Dict[str, str]:
    return {key: value for key, value in attrs.items() if value}


def _parse_numeric_style(value: str) -> Optional[Dict[str, object]]:
    if not value:
        return None
    normalized = value.strip().lower()
    if normalized in {"0", "0px"}:
        return {"kind": "px", "value": 0.0}
    if PX_VALUE_REGEX.match(normalized):
        return {"kind": "px", "value": float(normalized[:-2])}
    if PERCENT_VALUE_REGEX.match(normalized):
        return {"kind": "percent", "value": float(normalized[:-1])}
    if normalized == "auto":
        return {"kind": "keyword", "value": "auto"}
    if normalized.startswith("var("):
        return {"kind": "var", "value": value.strip()}
    return {"kind": "raw", "value": value.strip()}


def _extract_subset(style: Dict[str, str], keys: Tuple[str, ...]) -> Dict[str, str]:
    return {key: style[key] for key in keys if key in style and style[key]}


def _infer_anchor_hint(style: Dict[str, str]) -> Dict[str, str]:
    horizontal = "center"
    vertical = "center"

    if style.get("left") and style.get("right"):
        horizontal = "stretch"
    elif style.get("left"):
        horizontal = "left"
    elif style.get("right"):
        horizontal = "right"

    if style.get("top") and style.get("bottom"):
        vertical = "stretch"
    elif style.get("top"):
        vertical = "top"
    elif style.get("bottom"):
        vertical = "bottom"

    return {"horizontal": horizontal, "vertical": vertical}


def _node_content_type(node: HtmlNode) -> str:
    role = node.role or ""
    if node.tag == "button" or role.startswith("button/"):
        return "button"
    if node.tag == "img":
        return "image"
    if node.direct_text or node.tag in TEXT_TAGS or role.startswith("text/"):
        return "text"
    return "container"


def _aggregate_text(node: HtmlNode) -> str:
    parts: List[str] = []
    if node.direct_text:
        parts.append(node.direct_text)
    for child in node.children:
        child_text = _aggregate_text(child)
        if child_text:
            parts.append(child_text)
    return normalize_whitespace(" ".join(parts))


def _child_token(node: HtmlNode, sibling_index: int) -> str:
    if node.name:
        return node.name
    if node.role:
        safe_role = node.role.replace("/", "_")
        return f"{node.tag}[{safe_role}:{sibling_index}]"
    return f"{node.tag}[{sibling_index}]"


def _serialize_named_node(
    node: HtmlNode,
    node_path: str,
    resolved_style: Dict[str, str],
    child_count: int,
) -> Dict[str, object]:
    return {
        "path": node_path,
        "tag": node.tag,
        "role": node.role,
        "classes": list(node.class_list),
        "contentType": _node_content_type(node),
        "directText": node.direct_text,
        "aggregateText": _aggregate_text(node),
        "layout": _extract_subset(resolved_style, LAYOUT_STYLE_KEYS),
        "textStyle": _extract_subset(resolved_style, TEXT_STYLE_KEYS),
        "visualStyle": _extract_subset(resolved_style, VISUAL_STYLE_KEYS),
        "childCount": child_count,
    }


def _build_tree_and_layout(
    validator: SitePackageValidator,
    node: HtmlNode,
    rules: List[CssRule],
    node_path: str,
    layout_entries: List[Dict[str, object]],
    named_index: Dict[str, Dict[str, object]],
    resolved_styles: Dict[int, Dict[str, str]],
    frame_cache: Dict[int, GeometryFrame],
    page_root: Optional[HtmlNode],
    design_width: int,
    design_height: int,
) -> Dict[str, object]:
    resolved_style = resolved_styles.get(id(node))
    if resolved_style is None:
        resolved_style = validator._resolve_styles(node, rules)
        resolved_styles[id(node)] = resolved_style
    layout_style = _extract_subset(resolved_style, LAYOUT_STYLE_KEYS)
    numeric_layout = {
        key: parsed
        for key, parsed in (
            (style_key, _parse_numeric_style(layout_style.get(style_key, "")))
            for style_key in (
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
                "gap",
            )
        )
        if parsed is not None
    }

    primitive_id = validator._infer_standard_primitive(node) if hasattr(validator, "_infer_standard_primitive") else ""
    is_page_root = page_root is not None and node is page_root
    absolute_rect = _compute_absolute_rect(
        validator=validator,
        node=node,
        page_root=page_root,
        resolved_styles=resolved_styles,
        frame_cache=frame_cache,
        design_width=design_width,
        design_height=design_height,
    )
    stability_level = _compute_stability_level(
        node,
        resolved_style,
        primitive_id or "",
        is_page_root,
        absolute_rect is not None,
    )

    entry = {
        "path": node_path,
        "name": node.name,
        "tag": node.tag,
        "role": node.role,
        "classes": list(node.class_list),
        "contentType": _node_content_type(node),
        "directText": node.direct_text,
        "aggregateText": _aggregate_text(node),
        "anchorHint": _infer_anchor_hint(layout_style),
        "layout": layout_style,
        "layoutNumeric": numeric_layout,
        "stabilityLevel": stability_level,
        "absoluteRect": absolute_rect,
        "textStyle": _extract_subset(resolved_style, TEXT_STYLE_KEYS),
        "visualStyle": _extract_subset(resolved_style, VISUAL_STYLE_KEYS),
        "attrs": _clean_attrs(node.attrs),
    }
    layout_entries.append(entry)

    child_name_counts: Dict[str, int] = {}
    children_payload: List[Dict[str, object]] = []
    for child in node.children:
        token_key = child.name or child.role or child.tag
        child_name_counts[token_key] = child_name_counts.get(token_key, 0) + 1
        child_token = _child_token(child, child_name_counts[token_key])
        child_path = f"{node_path}/{child_token}" if node_path else child_token
        children_payload.append(
            _build_tree_and_layout(
                validator=validator,
                node=child,
                rules=rules,
                node_path=child_path,
                layout_entries=layout_entries,
                named_index=named_index,
                resolved_styles=resolved_styles,
                frame_cache=frame_cache,
                page_root=page_root,
                design_width=design_width,
                design_height=design_height,
            )
        )

    if node.name:
        named_index[node.name] = _serialize_named_node(node, node_path, resolved_style, len(children_payload))

    return {
        "path": node_path,
        "tag": node.tag,
        "name": node.name,
        "role": node.role,
        "classes": list(node.class_list),
        "contentType": _node_content_type(node),
        "directText": node.direct_text,
        "aggregateText": _aggregate_text(node),
        "attrs": _clean_attrs(node.attrs),
        "resolvedStyle": resolved_style,
        "layout": layout_style,
        "stabilityLevel": stability_level,
        "absoluteRect": absolute_rect,
        "textStyle": _extract_subset(resolved_style, TEXT_STYLE_KEYS),
        "visualStyle": _extract_subset(resolved_style, VISUAL_STYLE_KEYS),
        "children": children_payload,
    }


def _compile_page_snapshot(
    validator: SitePackageValidator,
    manifest: Dict[str, object],
    contract_pages: Dict[str, Dict[str, object]],
    self_check_pages: Dict[str, Dict[str, object]],
    manifest_page: Dict[str, object],
    theme_sheet,
    widgets_sheet,
) -> Dict[str, object]:
    page_id = str(manifest_page.get("pageId", "")).strip()
    html_rel = str(manifest_page.get("html", "")).strip()
    html_path = validator.source_root / html_rel if html_rel else None
    snapshot_warnings: List[str] = []
    design_width = int(manifest.get("designWidth", 1920) or 1920)
    design_height = int(manifest.get("designHeight", 1080) or 1080)

    if not html_path or not html_path.exists():
        return {
            "pageId": page_id or "<missing-page-id>",
            "html": html_rel,
            "manifest": manifest_page,
            "pageRootFound": False,
            "warnings": [f"Missing HTML file: {html_rel or '<missing html path>'}"],
            "tree": None,
            "namedNodeIndex": {},
            "layoutEntries": [],
        }

    html_text = html_path.read_text(encoding="utf-8")
    page_report = PageValidationReport(page_id=page_id, html_path=html_rel)
    local_sheets = validator._load_local_style_sheets(manifest_page, html_text, page_report)
    snapshot_warnings.extend(page_report.error_messages)
    snapshot_warnings.extend(page_report.warning_messages)

    parser = LimitedHtmlTreeBuilder()
    parser.feed(html_text)
    all_nodes = validator._flatten_nodes(parser.document)
    page_root = validator._find_page_root(all_nodes, page_id)

    all_rules: List[CssRule] = list(theme_sheet.rules) + list(widgets_sheet.rules) + [
        rule for sheet in local_sheets for rule in sheet.rules
    ]

    tree_root = page_root
    if tree_root is None:
        snapshot_warnings.append(f"Page root with data-ui-page='{page_id}' was not found; exporting from body/document.")
        tree_root = next((node for node in all_nodes if node.tag == "body"), None)
    if tree_root is None:
        tree_root = parser.document

    # Pre-resolve styles for every node, then run the full geometry resolver so every node
    # whose position can be derived from CSS (flex row/column with justify/align, absolute
    # with px or % offsets, block stacks with explicit sizes) gets an absolute frame that
    # Unity will pin locked nodes to.
    resolved_styles: Dict[int, Dict[str, str]] = {
        id(node): validator._resolve_styles(node, all_rules) for node in all_nodes
    }
    frame_cache: Dict[int, GeometryFrame] = _resolve_geometry_tree(
        page_root=page_root,
        resolved_styles=resolved_styles,
        design_width=design_width,
        design_height=design_height,
    )

    root_path = tree_root.name or (page_id if tree_root.tag == "#document" else _child_token(tree_root, 1))
    layout_entries: List[Dict[str, object]] = []
    named_index: Dict[str, Dict[str, object]] = {}
    tree_payload = _build_tree_and_layout(
        validator=validator,
        node=tree_root,
        rules=all_rules,
        node_path=root_path,
        layout_entries=layout_entries,
        named_index=named_index,
        resolved_styles=resolved_styles,
        frame_cache=frame_cache,
        page_root=page_root,
        design_width=design_width,
        design_height=design_height,
    )

    defined_classes = sorted(
        set(theme_sheet.classes)
        | set(widgets_sheet.classes)
        | {class_name for sheet in local_sheets for class_name in sheet.classes}
    )
    defined_roles = sorted(
        set(theme_sheet.roles)
        | set(widgets_sheet.roles)
        | {role_name for sheet in local_sheets for role_name in sheet.roles}
    )

    page_contract = contract_pages.get(page_id)
    page_self_check = self_check_pages.get(page_id)
    linked_styles = sorted(validator._extract_link_hrefs(all_nodes, html_rel))

    return {
        "pageId": page_id,
        "html": html_rel,
        "manifest": manifest_page,
        "pageRootFound": page_root is not None,
        "pageRootName": page_root.name if page_root else "",
        "linkedStyles": linked_styles,
        "localStyleSources": [sheet.source for sheet in local_sheets],
        "definedClasses": defined_classes,
        "definedRoles": defined_roles,
        "contract": page_contract or {},
        "selfCheck": page_self_check or {},
        "warnings": snapshot_warnings,
        "tree": tree_payload,
        "namedNodeIndex": named_index,
        "layoutEntries": layout_entries,
    }


def export_site_snapshots(
    site_root: Path,
    output_root: Path,
    allowlist_path: Optional[Path] = None,
    allow_legacy_metadata: bool = False,
) -> Dict[str, Path]:
    layout = resolve_site_package_layout(site_root)
    validator = SitePackageValidator(
        site_root=site_root,
        allowlist_path=allowlist_path,
        allow_legacy_metadata=allow_legacy_metadata,
    )

    manifest = validator._load_json(layout.source_root / "site.json")
    contract = validator._load_json(layout.source_root / "ui_contract.json")
    self_check = validator._load_json(layout.source_root / "ui_self_check_report.json")
    theme_sheet = validator._parse_css_sheet(layout.source_root / "theme.css", "theme")
    widgets_sheet = validator._parse_css_sheet(layout.source_root / "shared" / "widgets.css", "widgets")

    output_root.mkdir(parents=True, exist_ok=True)
    compiled_pages_dir = output_root / "compiled_pages"
    layout_snapshots_dir = output_root / "layout_snapshots"
    compiled_pages_dir.mkdir(parents=True, exist_ok=True)
    layout_snapshots_dir.mkdir(parents=True, exist_ok=True)

    contract_pages = {
        str(page.get("pageId", "")).strip(): page
        for page in contract.get("pages", [])
        if isinstance(page, dict) and str(page.get("pageId", "")).strip()
    }
    self_check_pages = {
        str(page.get("pageId", "")).strip(): page
        for page in self_check.get("pageReports", [])
        if isinstance(page, dict) and str(page.get("pageId", "")).strip()
    }

    pages_index: List[Dict[str, object]] = []
    for manifest_page in manifest.get("pages", []) if isinstance(manifest, dict) else []:
        if not isinstance(manifest_page, dict):
            continue
        page_snapshot = _compile_page_snapshot(
            validator=validator,
            manifest=manifest,
            contract_pages=contract_pages,
            self_check_pages=self_check_pages,
            manifest_page=manifest_page,
            theme_sheet=theme_sheet,
            widgets_sheet=widgets_sheet,
        )
        page_id = str(page_snapshot.get("pageId", "")).strip() or "unknown-page"
        compiled_page_path = compiled_pages_dir / f"{page_id}.compiled_page.json"
        layout_snapshot_path = layout_snapshots_dir / f"{page_id}.layout_snapshot.json"

        compiled_payload = {
            "siteId": manifest.get("siteId", ""),
            "designWidth": manifest.get("designWidth", 1920),
            "designHeight": manifest.get("designHeight", 1080),
            **page_snapshot,
        }
        layout_payload = {
            "siteId": manifest.get("siteId", ""),
            "pageId": page_id,
            "designWidth": manifest.get("designWidth", 1920),
            "designHeight": manifest.get("designHeight", 1080),
            "pageRootFound": page_snapshot.get("pageRootFound", False),
            "pageRootName": page_snapshot.get("pageRootName", ""),
            "entries": page_snapshot.get("layoutEntries", []),
            "warnings": page_snapshot.get("warnings", []),
        }

        compiled_page_path.write_text(json.dumps(compiled_payload, ensure_ascii=False, indent=2), encoding="utf-8")
        layout_snapshot_path.write_text(json.dumps(layout_payload, ensure_ascii=False, indent=2), encoding="utf-8")

        pages_index.append(
            {
                "pageId": page_id,
                "html": page_snapshot.get("html", ""),
                "compiledPage": compiled_page_path.relative_to(output_root).as_posix(),
                "layoutSnapshot": layout_snapshot_path.relative_to(output_root).as_posix(),
                "warningCount": len(page_snapshot.get("warnings", [])),
                "pageRootFound": bool(page_snapshot.get("pageRootFound", False)),
            }
        )

    compiled_site_path = output_root / "compiled_site.json"
    compiled_site_payload = {
        "siteId": manifest.get("siteId", ""),
        "displayName": manifest.get("displayName", ""),
        "designWidth": manifest.get("designWidth", 1920),
        "designHeight": manifest.get("designHeight", 1080),
        "themeCss": manifest.get("themeCss", ""),
        "sharedStyles": manifest.get("sharedStyles", []),
        "themeRoles": sorted(theme_sheet.roles),
        "themeClasses": sorted(theme_sheet.classes),
        "widgetClasses": sorted(widgets_sheet.classes),
        "widgetRoles": sorted(widgets_sheet.roles),
        "pages": pages_index,
    }
    compiled_site_path.write_text(json.dumps(compiled_site_payload, ensure_ascii=False, indent=2), encoding="utf-8")

    return {
        "compiled_site": compiled_site_path,
        "compiled_pages_dir": compiled_pages_dir,
        "layout_snapshots_dir": layout_snapshots_dir,
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Export compiled AIToUGUI page and layout snapshots.")
    parser.add_argument("site_root", type=Path, help="Path to the AIToUGUI site package root.")
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=None,
        help="Optional output directory. Defaults to <site_root>/snapshots.",
    )
    parser.add_argument(
        "--allowlist",
        type=Path,
        default=None,
        help="Optional path to AIToUGUI_Allowlist参考.json.",
    )
    parser.add_argument(
        "--allow-legacy-metadata",
        action="store_true",
        help="Treat missing ui_contract.json / ui_self_check_report.json as warnings.",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    site_root = args.site_root.resolve()
    if not site_root.exists() or not site_root.is_dir():
        parser.error(f"site_root does not exist or is not a directory: {site_root}")

    layout = resolve_site_package_layout(site_root)
    output_root = args.output_dir.resolve() if args.output_dir else layout.snapshots_root
    outputs = export_site_snapshots(
        site_root=site_root,
        output_root=output_root,
        allowlist_path=args.allowlist.resolve() if args.allowlist else None,
        allow_legacy_metadata=args.allow_legacy_metadata,
    )

    print(f"[AIToUGUI Snapshots] compiled_site={outputs['compiled_site']}")
    print(f"[AIToUGUI Snapshots] compiled_pages={outputs['compiled_pages_dir']}")
    print(f"[AIToUGUI Snapshots] layout_snapshots={outputs['layout_snapshots_dir']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
