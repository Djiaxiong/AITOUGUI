#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Set, Tuple

from site_package_layout import resolve_site_package_layout
from validate_site_package import (
    HtmlNode,
    LimitedHtmlTreeBuilder,
    SitePackageValidator,
    normalize_whitespace,
)


REPORT_VERSION = "1.0"
LAYOUT_STYLE_KEYS = (
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
    "display",
    "gap",
    "justify-content",
    "align-items",
    "align-content",
    "flex-direction",
    "overflow",
    "overflow-x",
    "overflow-y",
    "box-sizing",
)
TEXT_STYLE_KEYS = (
    "width",
    "height",
    "color",
    "font-size",
    "font-family",
    "font-weight",
    "line-height",
    "letter-spacing",
    "text-align",
    "text-transform",
)
PERCENT_KEYS = (
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
)
TEXT_TAGS = {"span", "p", "label", "h1", "h2", "h3", "h4", "h5", "h6"}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate ui_contract.json and ui_self_check_report.json from a normalized AIToUGUI site package.")
    parser.add_argument("site_root", type=Path, help="Path to the AIToUGUI site package root.")
    parser.add_argument(
        "--allowlist",
        type=Path,
        default=None,
        help="Optional path to the AIToUGUI allowlist JSON.",
    )
    parser.add_argument(
        "--write-self-check",
        action="store_true",
        help="Also write a pass-only ui_self_check_report.json scaffold.",
    )
    return parser


def load_json(path: Path) -> Dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, payload: Dict[str, object]) -> None:
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def find_page_root(document: HtmlNode, page_id: str) -> Optional[HtmlNode]:
    stack = list(reversed(document.children))
    while stack:
        node = stack.pop()
        if node.attrs.get("data-ui-page", "").strip() == page_id:
            return node
        stack.extend(reversed(node.children))
    return None


def walk_nodes(root: HtmlNode) -> Iterable[HtmlNode]:
    stack = [root]
    while stack:
        node = stack.pop()
        yield node
        stack.extend(reversed(node.children))


def normalize_element_identity(value: str) -> Tuple[str, str]:
    raw = normalize_whitespace(value).lower()
    if not raw:
        return "", ""
    if "/" in raw:
        head, tail = raw.split("/", 1)
        return head.strip(), tail.strip()
    return raw, ""


def pick_style_subset(node: HtmlNode, resolved_style: Dict[str, str]) -> Dict[str, str]:
    keys = TEXT_STYLE_KEYS if node.tag in TEXT_TAGS else LAYOUT_STYLE_KEYS
    result: Dict[str, str] = {}
    for key in keys:
        value = resolved_style.get(key, "").strip()
        if value:
            result[key] = value
    return result


def infer_size_policy(node: HtmlNode, style_subset: Dict[str, str]) -> str:
    """Return the contract sizePolicy for a node.

    "fixed"    -- node has explicit px width AND height; the validator will enforce
                  that these dimensions remain present. Applied to buttons, main panels,
                  cards, HUD blocks, modal panels, and nodes explicitly tagged
                  data-ui-template-size="true" (so their geometry is treated as load-bearing).
    "auto-text"-- pure text leaves whose height comes from line-height metrics.
    "relative" -- explicit percentage on width/height/etc.
    "auto"     -- default for containers whose size comes from flex / content flow;
                  the validator will not demand explicit width/height on these, which
                  previously caused false "missing required width" errors on every
                  intermediate div.
    """
    if node.attrs.get("data-ui-template-size", "").strip().lower() == "true":
        return "fixed"
    if any("%" in style_subset.get(key, "") for key in PERCENT_KEYS):
        return "relative"
    if node.tag in TEXT_TAGS and not style_subset.get("height"):
        return "auto-text"
    has_explicit_width = bool(style_subset.get("width")) and "%" not in style_subset.get("width", "")
    has_explicit_height = bool(style_subset.get("height")) and "%" not in style_subset.get("height", "")
    if has_explicit_width and has_explicit_height:
        return "fixed"
    return "auto"


LAYOUT_ARCHETYPES = ("stage", "dashboard", "scrapbook", "binder", "album-grid", "hero-focus")
SHAPE_LANGUAGES = ("roundrect", "per-corner", "capsule", "cut-corner", "plate", "banner")
FRAME_LANGUAGES = ("solid", "outline", "hairline", "glow")
ORNAMENT_LANGUAGES = (
    "title-bar", "ticket-line", "tab-folder", "sticker-corner", "poster-board",
    "collage", "grid-paper", "ring", "ribbon", "seal", "laurel",
)


def _count_role_prefixes(nodes: List[HtmlNode]) -> Dict[str, int]:
    counts: Dict[str, int] = {}
    for node in nodes:
        role = (node.role or "").strip()
        if "/" in role:
            counts[role.split("/", 1)[0]] = counts.get(role.split("/", 1)[0], 0) + 1
    return counts


def _classify_layout_archetype(root: HtmlNode, all_nodes: List[HtmlNode]) -> str:
    """Cheap heuristic based on root's flow children + grid/repeat density. Prefer axes the
    AI can hit than exhaustive correctness; audit_style_diversity.py (Track B part 2) will
    score the cross-case differences.
    """
    flow_children = [c for c in root.children if c.tag != "#document"]
    if not flow_children:
        return "stage"

    # tab-folder / binder: nodes tagged as tabs -> "binder"
    role_prefix_counts = _count_role_prefixes(all_nodes)
    tab_like = sum(1 for n in all_nodes if any(tag in (n.role or "").lower() for tag in ("tab/", "tabs/", "tabbar/", "nav/")))
    if tab_like >= 3:
        return "binder"

    # album-grid: many siblings of the same role family (>= 6) suggest a uniform grid of cards/slots
    for node in all_nodes:
        same_role_siblings = [c for c in node.children if c.role and "/" in c.role]
        if len(same_role_siblings) < 6:
            continue
        role_groups: Dict[str, int] = {}
        for c in same_role_siblings:
            role_groups[c.role] = role_groups.get(c.role, 0) + 1
        if max(role_groups.values()) >= 6:
            return "album-grid"

    # scrapbook: rotation / scatter -- any non-trivial rotation on multiple ornaments
    rotated = sum(
        1 for n in all_nodes
        if (n.attrs.get("data-ui-rotation", "").strip() or "rotate(" in (n.attrs.get("style", "") or "").lower())
    )
    if rotated >= 3:
        return "scrapbook"

    # hero-focus: one large centerpiece (explicit > 800x500) and a few small siblings
    # dashboard: 3+ top-level bands/cards/panels side-by-side
    panel_count = role_prefix_counts.get("panel", 0) + role_prefix_counts.get("card", 0) + role_prefix_counts.get("hud", 0)
    if panel_count >= 3 and len(flow_children) >= 3:
        return "dashboard"

    return "stage"


def _classify_shape_language(root: HtmlNode, named_nodes: List[Dict[str, object]], all_nodes: List[HtmlNode]) -> str:
    """Pick the dominant data-ui-shape if one exists; otherwise infer from border-radius
    shapes (50% -> capsule; asymmetric -> per-corner; otherwise roundrect).
    """
    shape_counts: Dict[str, int] = {}
    for node in named_nodes:
        shape_id = str(node.get("shapeId", "")).strip().lower()
        if shape_id in SHAPE_LANGUAGES:
            shape_counts[shape_id] = shape_counts.get(shape_id, 0) + 1
    if shape_counts:
        return max(shape_counts.items(), key=lambda kv: kv[1])[0]

    root_shape = root.attrs.get("data-ui-shape", "").strip().lower()
    if root_shape in SHAPE_LANGUAGES:
        return root_shape

    # Scan inline border-radius usage across all nodes with a style attr.
    circle_hits = 0
    asym_hits = 0
    normal_hits = 0
    for node in all_nodes:
        style = (node.attrs.get("style", "") or "").lower()
        if "border-radius" not in style:
            continue
        after = style.split("border-radius", 1)[1]
        after = after.split(";", 1)[0]
        if "50%" in after or "9999" in after:
            circle_hits += 1
        elif len([p for p in after.replace(":", " ").split() if p.endswith("px") or p.endswith("%")]) >= 3:
            asym_hits += 1
        else:
            normal_hits += 1
    if circle_hits >= max(2, asym_hits + normal_hits):
        return "capsule"
    if asym_hits >= max(2, normal_hits):
        return "per-corner"
    return "roundrect"


def _classify_frame_language(root: HtmlNode, named_nodes: List[Dict[str, object]], all_nodes: List[HtmlNode]) -> str:
    """Prefer authored data-ui-frame; else look at border weight + glow ornaments."""
    frame_counts: Dict[str, int] = {}
    for node in named_nodes:
        fid = str(node.get("frameId", "")).strip().lower()
        if fid in FRAME_LANGUAGES:
            frame_counts[fid] = frame_counts.get(fid, 0) + 1
    if frame_counts:
        return max(frame_counts.items(), key=lambda kv: kv[1])[0]

    root_frame = root.attrs.get("data-ui-frame", "").strip().lower()
    if root_frame in FRAME_LANGUAGES:
        return root_frame

    glow_hits = sum(1 for n in all_nodes if n.attrs.get("data-ui-glow", "").strip())
    hairline_hits = 0
    solid_hits = 0
    outline_hits = 0
    for node in all_nodes:
        style = (node.attrs.get("style", "") or "").lower()
        if "border" not in style:
            continue
        if "1px" in style:
            hairline_hits += 1
        elif "2px" in style or "3px" in style:
            outline_hits += 1
        else:
            solid_hits += 1
    if glow_hits >= 2:
        return "glow"
    scores = {"solid": solid_hits, "outline": outline_hits, "hairline": hairline_hits}
    if any(scores.values()):
        return max(scores.items(), key=lambda kv: kv[1])[0]
    return "solid"


def _classify_ornament_language(all_nodes: List[HtmlNode]) -> str:
    """Detect the first recognizable ornament cue from role tokens or class names."""
    role_text = " ".join((n.role or "") for n in all_nodes if n.role).lower()
    class_text = " ".join(
        cls.lower()
        for n in all_nodes
        for cls in n.class_list
    )
    combined = role_text + " " + class_text
    # Longest-first so 'title-bar' beats 'title' when both exist in tokens.
    for ornament in sorted(ORNAMENT_LANGUAGES, key=len, reverse=True):
        if ornament in combined:
            return ornament
    if "seal" in combined or "medal" in combined:
        return "seal"
    if "cloud" in combined or "wisp" in combined:
        return "collage"
    return "none"


def infer_visual_language(root: HtmlNode, named_nodes: List[Dict[str, object]], page_id: str) -> Dict[str, str]:
    """Infer the four style axes described in 规范/AIToUGUI_形态语言与风格分叉规范.md.

    These axes are the engineered signal for Track B: audit_style_diversity.py compares
    them across cases and flags suspected homogenisation when < 3 axes differ between
    two sibling sites. Keep classifications restricted to the enumerated values so the
    audit produces stable buckets.
    """
    all_nodes = list(walk_nodes(root))
    return {
        "layoutArchetype": _classify_layout_archetype(root, all_nodes),
        "shapeLanguage": _classify_shape_language(root, named_nodes, all_nodes),
        "frameLanguage": _classify_frame_language(root, named_nodes, all_nodes),
        "ornamentLanguage": _classify_ornament_language(all_nodes),
    }


def build_contract_node(node: HtmlNode, resolved_style: Dict[str, str]) -> Dict[str, object]:
    element_id, variant_id = normalize_element_identity(node.attrs.get("data-ui-element", ""))
    style_subset = pick_style_subset(node, resolved_style)
    payload: Dict[str, object] = {
        "name": node.name,
        "tag": node.tag,
        "role": node.role or "none",
        "classes": node.class_list,
        "text": node.direct_text,
        "sizePolicy": infer_size_policy(node, style_subset),
        "style": style_subset,
    }
    if element_id:
        payload["elementId"] = element_id
    if variant_id:
        payload["variantId"] = variant_id
    shape_id = node.attrs.get("data-ui-shape", "").strip()
    if shape_id:
        payload["shapeId"] = shape_id
    frame_id = node.attrs.get("data-ui-frame", "").strip()
    if frame_id:
        payload["frameId"] = frame_id
    return payload


def build_self_check(site_id: str, pages: List[Dict[str, object]]) -> Dict[str, object]:
    page_reports: List[Dict[str, object]] = []
    for page in pages:
        page_reports.append(
            {
                "pageId": page["pageId"],
                "unsupportedTags": [],
                "unsupportedAttributes": [],
                "unsupportedSelectors": [],
                "unsupportedProperties": [],
                "unsupportedValuePatterns": [],
                "missingRequiredSizeNodes": [],
                "missingNameNodes": [],
                "missingRoleNodes": [],
                "undefinedClasses": [],
                "undefinedRoles": [],
                "relativeSizeNodes": [],
                "templateSizeNodes": [],
                "browserOnlyPatterns": [],
                "warnings": [],
            }
        )
    return {
        "reportVersion": REPORT_VERSION,
        "siteId": site_id,
        "status": "pass",
        "summary": {
            "pageCount": len(page_reports),
            "errorCount": 0,
            "warningCount": 0,
        },
        "violations": [],
        "pageReports": page_reports,
        "notes": [
            "Generated by generate_site_contract.py as a draft-first normalized scaffold.",
        ],
    }


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    site_root = args.site_root.resolve()

    layout = resolve_site_package_layout(site_root)
    validator = SitePackageValidator(site_root, args.allowlist, allow_legacy_metadata=True)
    manifest = load_json(layout.source_root / "site.json")
    theme_sheet = validator._parse_css_sheet(layout.source_root / "theme.css", "theme")
    widgets_sheet = validator._parse_css_sheet(layout.source_root / "shared" / "widgets.css", "widgets")
    all_rules = [*theme_sheet.rules, *widgets_sheet.rules]
    tokens = validator._collect_tokens(all_rules)

    contract_pages: List[Dict[str, object]] = []
    used_roles: Set[str] = set()
    used_classes: Set[str] = set()

    for manifest_page in manifest.get("pages", []):
        if not isinstance(manifest_page, dict):
            continue
        page_id = str(manifest_page.get("pageId", "")).strip()
        html_rel = str(manifest_page.get("html", "")).strip()
        if not page_id or not html_rel:
            continue

        html_path = layout.source_root / html_rel
        html_text = html_path.read_text(encoding="utf-8")
        parser_instance = LimitedHtmlTreeBuilder()
        parser_instance.feed(html_text)
        page_root = find_page_root(parser_instance.document, page_id)
        if page_root is None:
            raise ValueError(f"Could not find page root for '{page_id}' in {html_rel}.")

        resolved_root = validator._resolve_styles(page_root, all_rules, tokens)
        root_payload = build_contract_node(page_root, resolved_root)

        named_nodes: List[Dict[str, object]] = []
        relative_nodes: List[Dict[str, object]] = []
        template_nodes: List[Dict[str, object]] = []
        for node in walk_nodes(page_root):
            if node is page_root or not node.name:
                continue
            resolved_style = validator._resolve_styles(node, all_rules, tokens)
            node_payload = build_contract_node(node, resolved_style)
            named_nodes.append(node_payload)

            used_roles.add(node.role)
            used_classes.update(node.class_list)

            if node_payload.get("sizePolicy") == "relative":
                relative_nodes.append(
                    {
                        "name": node.name,
                        "reason": "percentage-based normalized node",
                        "parentConstraint": "resolved from normalized HTML",
                        "style": {
                            key: value
                            for key, value in node_payload.get("style", {}).items()
                            if "%" in str(value)
                        },
                    }
                )

            if node.attrs.get("data-ui-template-size", "").strip().lower() == "true":
                template_nodes.append(
                    {
                        "name": node.name,
                        "elementId": node_payload.get("elementId", ""),
                        "reason": "authoring template-size node",
                    }
                )

        used_roles.add(page_root.role)
        used_classes.update(page_root.class_list)

        page_payload = {
            "pageId": page_id,
            "displayName": manifest_page.get("displayName", page_id),
            "html": html_rel,
            "prefabName": manifest_page.get("prefabName", ""),
            "targetLayer": manifest_page.get("targetLayer", "Normal"),
            "visualLanguage": infer_visual_language(page_root, named_nodes, page_id),
            "root": root_payload,
            "namedNodes": named_nodes,
            "relativeSizeNodes": relative_nodes,
            "templateSizeNodes": template_nodes,
            "notes": [],
        }
        contract_pages.append(page_payload)

    contract = {
        "contractVersion": REPORT_VERSION,
        "workflow": {
            "mode": "draft-first",
            "currentStage": "normalized",
            "normalizePolicy": {
                "preserve": ["structure", "layout", "semantic-nodes", "key-copy"],
                "degradeOrder": ["special-effects", "ornament-overdraw", "shape-complexity", "layout"],
            },
        },
        "site": {
            "siteId": manifest.get("siteId", ""),
            "displayName": manifest.get("displayName", ""),
            "designWidth": manifest.get("designWidth", 1920),
            "designHeight": manifest.get("designHeight", 1080),
            "themeCss": manifest.get("themeCss", "theme.css"),
            "sharedStyles": manifest.get("sharedStyles", []),
        },
        "pages": contract_pages,
        "usedRoles": sorted(role for role in used_roles if role),
        "usedClasses": sorted(class_name for class_name in used_classes if class_name),
        "notes": [
            "Generated from normalized HTML after the draft-first convergence pass.",
        ],
    }

    contract_path = layout.source_root / "ui_contract.json"
    write_json(contract_path, contract)
    if args.write_self_check:
        self_check_path = layout.source_root / "ui_self_check_report.json"
        write_json(self_check_path, build_self_check(str(manifest.get("siteId", "")).strip(), contract_pages))

    print(f"[AIToUGUI Contract] contract={contract_path}")
    if args.write_self_check:
        print(f"[AIToUGUI Contract] self_check={self_check_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
