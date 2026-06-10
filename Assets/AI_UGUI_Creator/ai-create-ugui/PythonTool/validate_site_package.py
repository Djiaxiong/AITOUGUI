#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import posixpath
import re
import sys
from dataclasses import dataclass, field
from html.parser import HTMLParser
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Set, Tuple

from site_package_layout import resolve_site_package_layout


REPORT_VERSION = "1.0"
VOID_TAGS = {"meta", "link", "img", "input", "br"}
STANDARD_HTML_ATTRIBUTES = {"href", "rel", "charset", "lang", "content", "name", "src", "alt", "type"}
DEFAULT_ALLOWED_TARGET_LAYERS = {"Normal", "Top", "Popup", "Overlay"}
LAYOUT_ROLE_PREFIXES = ("layout/",)
FOOTPRINT_TOLERANCE_PX = 8.0
GEOMETRY_TOLERANCE_PX = 0.5

SELECTOR_CLASS_REGEX = re.compile(r"^\.[A-Za-z_][\w\-]*$")
SELECTOR_ROLE_REGEX = re.compile(r'^\[data-ui-role="([^"]+)"\]$')
CSS_RULE_REGEX = re.compile(r"(?P<selector>[^{}]+)\{(?P<body>[^{}]*)\}", re.S)
CSS_DECLARATION_REGEX = re.compile(r"(?P<name>[\w\-]+)\s*:\s*(?P<value>[^;]+);?")
CSS_COMMENT_REGEX = re.compile(r"/\*.*?\*/", re.S)
STYLE_TAG_REGEX = re.compile(r"<style[^>]*>(.*?)</style>", re.I | re.S)
WHITESPACE_REGEX = re.compile(r"\s+")
PERCENTAGE_REGEX = re.compile(r"(?<![\w\-])\-?(?:\d+|\d*\.\d+)%")
HEX_COLOR_REGEX = re.compile(r"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")
RGBA_COLOR_REGEX = re.compile(r"^rgba?\([^)]+\)$", re.I)
CSS_VAR_REGEX = re.compile(r"^var\(--[\w\-]+\)$", re.I)
CSS_VAR_REFERENCE_REGEX = re.compile(r"var\((--[\w\-]+)\)")
PIXEL_VALUE_REGEX = re.compile(r"^-?(?:\d+|\d*\.\d+)px$", re.I)
BORDER_WIDTH_REGEX = re.compile(r"(?<![\w\-])-?(?:\d+|\d*\.\d+)px", re.I)
ROTATE_TRANSFORM_REGEX = re.compile(r"^rotate\(\s*-?(?:\d+|\d*\.\d+)deg\s*\)$", re.I)
ANIMATION_REGEX = re.compile(
    r"^(rotate|float|pulse)\s+-?(?:\d+|\d*\.\d+)s\s+(?:linear|ease-in-out)\s+infinite(?:\s+reverse)?$",
    re.I,
)
ANIMATION_DELAY_REGEX = re.compile(r"^-?(?:\d+|\d*\.\d+)s$", re.I)
OPACITY_REGEX = re.compile(r"^(?:0|1|0?\.\d+)$")

INLINE_STYLE_COMPATIBILITY_ALLOWLIST = {
    "background",
    "border-style",
    "box-sizing",
    "min-width",
    "min-height",
    "max-height",
    "overflow",
    "overflow-x",
    "overflow-y",
    "margin",
    "margin-right",
    "margin-bottom",
    "font-family",
    "text-transform",
    "transform",
    "opacity",
    "animation",
    "animation-delay",
    "-ai-value",
}

THEME_STYLE_COMPATIBILITY_ALLOWLIST = {
    "background",
    "border-style",
    "min-width",
    "min-height",
    "max-width",
    "max-height",
    "transform",
    "opacity",
    "animation",
    "animation-delay",
}

WIDGET_STYLE_COMPATIBILITY_ALLOWLIST = {
    "min-width",
    "max-height",
    "overflow",
    "overflow-x",
    "overflow-y",
    "color",
    "background-color",
    "background",
    "position",
    "left",
    "right",
    "top",
    "bottom",
    "border",
    "border-style",
    "border-radius",
    "box-shadow",
    "transform",
    "opacity",
    "animation",
    "animation-delay",
}

BROWSER_ONLY_PATTERN_RULES = (
    ("flex-wrap", re.compile(r"(^|[;{\s])flex-wrap\s*:", re.I)),
    ("grid", re.compile(r"(^|[;{\s])(?:display\s*:\s*grid|grid(?:-[\w]+)?\s*:)", re.I)),
    ("calc()", re.compile(r"calc\s*\(", re.I)),
    ("aspect-ratio", re.compile(r"(^|[;{\s])aspect-ratio\s*:", re.I)),
    ("repeating-linear-gradient", re.compile(r"repeating-linear-gradient\s*\(", re.I)),
    ("repeating-radial-gradient", re.compile(r"repeating-radial-gradient\s*\(", re.I)),
    ("radial-gradient", re.compile(r"radial-gradient\s*\(", re.I)),
    ("filter", re.compile(r"(^|[;{\s])filter\s*:", re.I)),
    ("backdrop-filter", re.compile(r"(^|[;{\s])backdrop-filter\s*:", re.I)),
    ("position:fixed", re.compile(r"(^|[;{\s])position\s*:\s*fixed", re.I)),
)

BROWSER_ONLY_WARNING_RULES = (
    ("@keyframes", re.compile(r"@keyframes", re.I)),
)

DISCOURAGED_UNIT_PATTERNS = (
    ("rem unit", re.compile(r"(?<![\w\-])\-?(?:\d+|\d*\.\d+)rem\b", re.I)),
    ("em unit", re.compile(r"(?<![\w\-])\-?(?:\d+|\d*\.\d+)em\b", re.I)),
    ("vw unit", re.compile(r"(?<![\w\-])\-?(?:\d+|\d*\.\d+)vw\b", re.I)),
    ("vh unit", re.compile(r"(?<![\w\-])\-?(?:\d+|\d*\.\d+)vh\b", re.I)),
)

DEFAULT_PRIMITIVE_VARIANT = "default"
DEFAULT_SHAPE_ALLOWLIST = {"roundrect", "per-corner", "capsule", "cut-corner", "plate", "banner"}
DEFAULT_FRAME_ALLOWLIST = {"solid", "outline", "hairline", "glow"}
TEXT_LIKE_TAGS = {"span", "p", "label", "h1", "h2", "h3", "h4", "h5", "h6"}
TEXT_CHILD_ALLOWED_PRIMITIVES = {"button", "toggle", "dropdown", "progress"}


def normalize_whitespace(value: Optional[str]) -> str:
    return WHITESPACE_REGEX.sub(" ", value or "").strip()


def dedupe_strings(values: Iterable[str]) -> List[str]:
    seen: Set[str] = set()
    result: List[str] = []
    for value in values:
        if not value or value in seen:
            continue
        seen.add(value)
        result.append(value)
    return result


def is_css_var(value: str) -> bool:
    return bool(CSS_VAR_REGEX.match(value.strip()))


def is_pixel_or_zero_or_var(value: str) -> bool:
    stripped = value.strip().lower()
    return bool(re.match(r"^-?(?:\d+|\d*\.\d+)px$", stripped) or stripped in {"0", "0px", "auto"} or is_css_var(stripped))


def parse_px_value(value: Optional[str]) -> Optional[float]:
    if value is None:
        return None
    stripped = value.strip().lower()
    if stripped in {"0", "0px"}:
        return 0.0
    if PIXEL_VALUE_REGEX.match(stripped):
        return float(stripped[:-2])
    return None


def is_supported_transform_value(value: str) -> bool:
    return bool(ROTATE_TRANSFORM_REGEX.match(value.strip()))


def is_supported_animation_value(value: str) -> bool:
    return bool(ANIMATION_REGEX.match(value.strip()))


def is_supported_animation_delay_value(value: str) -> bool:
    return bool(ANIMATION_DELAY_REGEX.match(value.strip()))


def is_supported_opacity_value(value: str) -> bool:
    stripped = value.strip()
    if not OPACITY_REGEX.match(stripped):
        return False

    try:
        numeric = float(stripped)
    except ValueError:
        return False

    return 0.0 <= numeric <= 1.0


def resolve_css_value(value: str, tokens: Dict[str, str]) -> str:
    if not value:
        return value

    resolved = value
    for _ in range(8):
        updated = CSS_VAR_REFERENCE_REGEX.sub(lambda match: tokens.get(match.group(1), match.group(0)), resolved)
        if updated == resolved:
            break
        resolved = updated
    return resolved.strip()


def is_color_literal(value: str) -> bool:
    value = value.strip()
    return bool(HEX_COLOR_REGEX.match(value) or RGBA_COLOR_REGEX.match(value) or value.lower() == "transparent" or is_css_var(value))


def has_explicit_asset_background(node: "HtmlNode") -> bool:
    asset_id = normalize_whitespace(node.attrs.get("data-ui-asset-id", ""))
    if not asset_id:
        return False
    asset_type = normalize_whitespace(node.attrs.get("data-ui-asset-type", "")).lower()
    return asset_type in {"background", "frame", "ornament", "snapshot", "icon"} or not asset_type


def parse_style_declarations(style_text: Optional[str]) -> Dict[str, str]:
    declarations: Dict[str, str] = {}
    if not style_text:
        return declarations

    for match in CSS_DECLARATION_REGEX.finditer(style_text):
        name = match.group("name").strip().lower()
        value = match.group("value").strip()
        declarations[name] = value

    return declarations


def parse_style_map(style_value) -> Dict[str, str]:
    if style_value is None:
        return {}
    if isinstance(style_value, dict):
        result: Dict[str, str] = {}
        for key, value in style_value.items():
            if value is None:
                continue
            result[str(key).strip().lower()] = str(value).strip()
        return result
    if isinstance(style_value, str):
        return parse_style_declarations(style_value)
    return {}


def normalize_variant_id(value: Optional[str]) -> str:
    normalized = normalize_whitespace(value)
    return normalized or DEFAULT_PRIMITIVE_VARIANT


def normalize_shape_id(value: Optional[str]) -> str:
    return normalize_whitespace(value).lower()


def normalize_frame_id(value: Optional[str]) -> str:
    return normalize_whitespace(value).lower()


@dataclass
class HtmlNode:
    tag: str
    attrs: Dict[str, str]
    line: int
    column: int
    children: List["HtmlNode"] = field(default_factory=list)
    text_chunks: List[str] = field(default_factory=list)
    parent: Optional["HtmlNode"] = None

    @property
    def class_list(self) -> List[str]:
        return [token for token in self.attrs.get("class", "").split() if token]

    @property
    def role(self) -> str:
        return self.attrs.get("data-ui-role", "")

    @property
    def name(self) -> str:
        return self.attrs.get("data-ui-name", "")

    @property
    def inline_styles(self) -> Dict[str, str]:
        return parse_style_declarations(self.attrs.get("style"))

    @property
    def direct_text(self) -> str:
        return normalize_whitespace(" ".join(self.text_chunks))

    def display_label(self) -> str:
        if self.name:
            return self.name
        return f"{self.tag}@{self.line}:{self.column}"


class LimitedHtmlTreeBuilder(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.document = HtmlNode(tag="#document", attrs={}, line=0, column=0)
        self._stack: List[HtmlNode] = [self.document]

    def handle_starttag(self, tag: str, attrs: List[Tuple[str, Optional[str]]]) -> None:
        self._push_node(tag, attrs, self_closing=tag in VOID_TAGS)

    def handle_startendtag(self, tag: str, attrs: List[Tuple[str, Optional[str]]]) -> None:
        self._push_node(tag, attrs, self_closing=True)

    def _push_node(self, tag: str, attrs: List[Tuple[str, Optional[str]]], self_closing: bool) -> None:
        line, column = self.getpos()
        node = HtmlNode(
            tag=tag.lower(),
            attrs={key.lower(): (value or "") for key, value in attrs},
            line=line,
            column=column,
        )
        node.parent = self._stack[-1]
        self._stack[-1].children.append(node)
        if not self_closing:
            self._stack.append(node)

    def handle_endtag(self, tag: str) -> None:
        tag = tag.lower()
        for index in range(len(self._stack) - 1, 0, -1):
            if self._stack[index].tag == tag:
                del self._stack[index:]
                return

    def handle_data(self, data: str) -> None:
        if normalize_whitespace(data):
            self._stack[-1].text_chunks.append(data)


@dataclass
class CssRule:
    selector: str
    declarations: Dict[str, str]
    source: str
    line: int


@dataclass
class CssSheet:
    source: str
    kind: str
    rules: List[CssRule] = field(default_factory=list)
    classes: Set[str] = field(default_factory=set)
    roles: Set[str] = field(default_factory=set)
    errors: List[str] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)


@dataclass
class PageValidationReport:
    page_id: str
    html_path: str
    error_messages: List[str] = field(default_factory=list)
    warning_messages: List[str] = field(default_factory=list)
    unsupported_tags: List[str] = field(default_factory=list)
    unsupported_attributes: List[str] = field(default_factory=list)
    unsupported_selectors: List[str] = field(default_factory=list)
    unsupported_properties: List[str] = field(default_factory=list)
    unsupported_value_patterns: List[str] = field(default_factory=list)
    browser_only_patterns: List[str] = field(default_factory=list)
    missing_required_attributes: List[str] = field(default_factory=list)
    missing_required_size_nodes: List[str] = field(default_factory=list)
    undefined_classes: List[str] = field(default_factory=list)
    undefined_roles: List[str] = field(default_factory=list)
    contract_mismatches: List[str] = field(default_factory=list)
    metadata_mismatches: List[str] = field(default_factory=list)

    def add_error(self, message: str) -> None:
        self.error_messages.append(message)

    def add_warning(self, message: str) -> None:
        self.warning_messages.append(message)

    def finalize(self) -> Dict[str, object]:
        # style-level issues (CSS vocabulary, preview-only attrs/tags, browser-only patterns)
        # are downgraded to warnings: compile auto-drops or auto-degrades them, so they are not
        # Unity-consumption blockers. Contract-level issues (missing required attrs, wrong page
        # root dimensions, duplicate data-ui-name, contract mismatches) stay as errors.
        error_count = (
            len(self.error_messages)
            + len(self.missing_required_attributes)
            + len(self.missing_required_size_nodes)
            + len(self.contract_mismatches)
            + len(self.metadata_mismatches)
        )
        warning_count = (
            len(self.warning_messages)
            + len(self.unsupported_tags)
            + len(self.unsupported_attributes)
            + len(self.unsupported_selectors)
            + len(self.unsupported_properties)
            + len(self.unsupported_value_patterns)
            + len(self.browser_only_patterns)
            + len(self.undefined_classes)
            + len(self.undefined_roles)
        )

        return {
            "pageId": self.page_id,
            "html": self.html_path,
            "status": "pass" if error_count == 0 else "fail",
            "errorCount": error_count,
            "warningCount": warning_count,
            "errors": dedupe_strings(self.error_messages),
            "warnings": dedupe_strings(self.warning_messages),
            "unsupportedTags": dedupe_strings(self.unsupported_tags),
            "unsupportedAttributes": dedupe_strings(self.unsupported_attributes),
            "unsupportedSelectors": dedupe_strings(self.unsupported_selectors),
            "unsupportedProperties": dedupe_strings(self.unsupported_properties),
            "unsupportedValuePatterns": dedupe_strings(self.unsupported_value_patterns),
            "browserOnlyPatterns": dedupe_strings(self.browser_only_patterns),
            "missingRequiredAttributes": dedupe_strings(self.missing_required_attributes),
            "missingRequiredSizeNodes": dedupe_strings(self.missing_required_size_nodes),
            "undefinedClasses": dedupe_strings(self.undefined_classes),
            "undefinedRoles": dedupe_strings(self.undefined_roles),
            "contractMismatches": dedupe_strings(self.contract_mismatches),
            "metadataMismatches": dedupe_strings(self.metadata_mismatches),
        }


@dataclass
class GeometryFrame:
    left: float
    top: float
    width: float
    height: float


class SitePackageValidator:
    def __init__(self, site_root: Path, allowlist_path: Optional[Path], allow_legacy_metadata: bool = False) -> None:
        self.site_root = site_root.resolve()
        self.layout = resolve_site_package_layout(self.site_root)
        self.source_root = self.layout.source_root
        self.allow_legacy_metadata = allow_legacy_metadata
        self.allowlist_path = allowlist_path or self._resolve_default_allowlist()
        self.allowlist = self._load_json(self.allowlist_path) if self.allowlist_path else {}

        html_rules = self.allowlist.get("html", {})
        css_rules = self.allowlist.get("css", {})
        selectors_rules = self.allowlist.get("selectors", {})

        self.allowed_tags: Set[str] = set(html_rules.get("allowedTags", [])) | {"p", "h1", "h2", "h3", "h4", "h5", "h6", "img", "input"}
        self.allowed_attributes: Set[str] = set(html_rules.get("allowedAttributes", [])) | STANDARD_HTML_ATTRIBUTES
        # Collapse theme/widgets/inline property allowlists into a single set. The original split
        # was artificial (same property is valid in inline but invalid in theme.css), which forced
        # AI to inline everything and lose stylesheet-level cohesion. Any property-level complaint
        # is now a warning (see _validate_css_declarations) so AI gets a cue without blocking.
        preview_only_css = {
            str(item).strip().lower()
            for item in css_rules.get("previewOnlyProperties", [])
            if str(item).strip()
        }
        self.allowed_css_properties: Set[str] = (
            set(css_rules.get("themeAllowedProperties", []))
            | set(css_rules.get("widgetsAllowedProperties", []))
            | set(css_rules.get("inlineAllowedProperties", []))
            | THEME_STYLE_COMPATIBILITY_ALLOWLIST
            | WIDGET_STYLE_COMPATIBILITY_ALLOWLIST
            | INLINE_STYLE_COMPATIBILITY_ALLOWLIST
            | preview_only_css
        )
        # Retained for any legacy caller that inspects these directly.
        self.allowed_theme_properties: Set[str] = self.allowed_css_properties
        self.allowed_widget_properties: Set[str] = self.allowed_css_properties
        self.allowed_inline_properties: Set[str] = self.allowed_css_properties
        self.preview_only_css_properties: Set[str] = preview_only_css
        self.allowed_selectors: List[str] = list(selectors_rules.get("allowed", []))
        self.allowed_value_rules: Dict[str, Set[str]] = {
            key.lower(): {value.lower() for value in values}
            for key, values in css_rules.get("valueRules", {}).items()
        }
        element_rules = self.allowlist.get("elements", {})
        self.primitive_elements: Set[str] = {
            str(item).strip().lower()
            for item in element_rules.get("primitiveRequired", [])
            if str(item).strip()
        } or {"button", "input", "toggle", "slider", "dropdown", "scrollbar", "scrollview", "image", "progress"}
        self.variant_allowlist: Dict[str, Set[str]] = {
            str(key).strip().lower(): {
                str(value).strip().lower()
                for value in values
                if str(value).strip()
            }
            for key, values in element_rules.get("variantAllowlist", {}).items()
            if str(key).strip() and isinstance(values, list)
        }
        self.shape_allowlist: Set[str] = {
            str(item).strip().lower()
            for item in element_rules.get("shapeAllowlist", [])
            if str(item).strip()
        } or set(DEFAULT_SHAPE_ALLOWLIST)
        self.frame_allowlist: Set[str] = {
            str(item).strip().lower()
            for item in element_rules.get("frameAllowlist", [])
            if str(item).strip()
        } or set(DEFAULT_FRAME_ALLOWLIST)
        self.element_shape_allowlist: Dict[str, Set[str]] = {
            str(key).strip().lower(): {
                str(value).strip().lower()
                for value in values
                if str(value).strip()
            }
            for key, values in element_rules.get("elementShapeAllowlist", {}).items()
            if str(key).strip() and isinstance(values, list)
        }
        self.required_root_attributes = ("data-ui-page", "data-ui-name", "data-ui-role")
        self.required_button_attributes = ("data-ui-name", "data-ui-role", "data-ui-element")
        self.global_errors: List[str] = []
        self.global_warnings: List[str] = []

    def validate(self) -> Dict[str, object]:
        site_manifest_path = self.source_root / "site.json"
        contract_path = self.source_root / "ui_contract.json"
        self_check_path = self.source_root / "ui_self_check_report.json"
        preview_path = self.layout.preview_path
        theme_path = self.source_root / "theme.css"
        widgets_path = self.source_root / "shared" / "widgets.css"

        for required in (site_manifest_path, theme_path, widgets_path, preview_path):
            if not required.exists():
                self.global_errors.append(f"Missing required file: {required.relative_to(self.site_root).as_posix()}")

        if not contract_path.exists():
            message = f"Missing required file: {contract_path.relative_to(self.site_root).as_posix()}"
            (self.global_warnings if self.allow_legacy_metadata else self.global_errors).append(message)

        if not self_check_path.exists():
            message = f"Missing required file: {self_check_path.relative_to(self.site_root).as_posix()}"
            (self.global_warnings if self.allow_legacy_metadata else self.global_errors).append(message)

        manifest = self._load_json(site_manifest_path) if site_manifest_path.exists() else {}
        contract = self._load_json(contract_path) if contract_path.exists() else {}
        self_check = self._load_json(self_check_path) if self_check_path.exists() else {}

        site_id = str(manifest.get("siteId", "")).strip()
        design_width = int(manifest.get("designWidth", 1920) or 1920)
        design_height = int(manifest.get("designHeight", 1080) or 1080)

        theme_sheet = self._parse_css_sheet(theme_path, "theme")
        widgets_sheet = self._parse_css_sheet(widgets_path, "widgets")
        self.global_errors.extend(theme_sheet.errors)
        self.global_errors.extend(widgets_sheet.errors)
        self.global_warnings.extend(theme_sheet.warnings)
        self.global_warnings.extend(widgets_sheet.warnings)

        self._validate_body_rule(theme_sheet, design_width, design_height)
        self._validate_manifest(manifest, contract, self_check)

        page_reports: List[Dict[str, object]] = []
        manifest_pages = manifest.get("pages", []) if isinstance(manifest, dict) else []
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

        for manifest_page in manifest_pages:
            if not isinstance(manifest_page, dict):
                continue
            page_id = str(manifest_page.get("pageId", "")).strip()
            html_rel = str(manifest_page.get("html", "")).strip()
            page_report = PageValidationReport(page_id=page_id or "<missing-page-id>", html_path=html_rel)

            if not page_id:
                page_report.add_error("Manifest page is missing pageId.")
                page_reports.append(page_report.finalize())
                continue

            html_path = self.source_root / html_rel if html_rel else None
            if not html_rel or html_path is None or not html_path.exists():
                page_report.add_error(f"Missing HTML file for page '{page_id}': {html_rel or '<missing html path>'}")
                page_reports.append(page_report.finalize())
                continue

            html_text = html_path.read_text(encoding="utf-8")
            local_sheets = self._load_local_style_sheets(manifest_page, html_text, page_report)
            for local_sheet in local_sheets:
                self.global_errors.extend(local_sheet.errors)
                self.global_warnings.extend(local_sheet.warnings)

            self._validate_single_page(
                manifest_page=manifest_page,
                page_contract=contract_pages.get(page_id),
                page_self_check=self_check_pages.get(page_id),
                html_text=html_text,
                page_report=page_report,
                theme_sheet=theme_sheet,
                widgets_sheet=widgets_sheet,
                local_sheets=local_sheets,
                design_width=design_width,
                design_height=design_height,
                site_theme_href=str(manifest.get("themeCss", "")).strip(),
                site_shared_hrefs=[str(item).strip() for item in manifest.get("sharedStyles", []) if str(item).strip()],
            )

            page_reports.append(page_report.finalize())

        total_errors = len(self.global_errors) + sum(int(page["errorCount"]) for page in page_reports)
        total_warnings = len(self.global_warnings) + sum(int(page["warningCount"]) for page in page_reports)

        return {
            "reportVersion": REPORT_VERSION,
            "siteRoot": str(self.site_root),
            "siteId": site_id,
            "status": "pass" if total_errors == 0 else "fail",
            "summary": {
                "pageCount": len(page_reports),
                "errorCount": total_errors,
                "warningCount": total_warnings,
            },
            "violations": dedupe_strings(self.global_errors),
            "warnings": dedupe_strings(self.global_warnings),
            "pageReports": page_reports,
        }

    def validate_page(self, page_id: str, require_preview: bool = False) -> Dict[str, object]:
        site_manifest_path = self.source_root / "site.json"
        contract_path = self.source_root / "ui_contract.json"
        self_check_path = self.source_root / "ui_self_check_report.json"
        preview_path = self.layout.preview_path
        theme_path = self.source_root / "theme.css"
        widgets_path = self.source_root / "shared" / "widgets.css"

        required_paths = [site_manifest_path, theme_path, widgets_path]
        if require_preview:
            required_paths.append(preview_path)

        for required in required_paths:
            if not required.exists():
                self.global_errors.append(f"Missing required file: {required.relative_to(self.site_root).as_posix()}")

        if not contract_path.exists():
            message = f"Missing required file: {contract_path.relative_to(self.site_root).as_posix()}"
            (self.global_warnings if self.allow_legacy_metadata else self.global_errors).append(message)

        if self_check_path.exists():
            self_check = self._load_json(self_check_path)
        else:
            self_check = {}
            if not self.allow_legacy_metadata:
                self.global_warnings.append(f"Missing optional file for page validation: {self_check_path.relative_to(self.site_root).as_posix()}")

        manifest = self._load_json(site_manifest_path) if site_manifest_path.exists() else {}
        contract = self._load_json(contract_path) if contract_path.exists() else {}

        if not manifest:
            self.global_errors.append("site.json is missing or invalid.")

        for field_name in ("siteId", "displayName", "designWidth", "designHeight", "themeCss", "sharedStyles", "pages"):
            if manifest and field_name not in manifest:
                self.global_errors.append(f"site.json is missing required field '{field_name}'.")

        site_id = str(manifest.get("siteId", "")).strip()
        design_width = int(manifest.get("designWidth", 1920) or 1920)
        design_height = int(manifest.get("designHeight", 1080) or 1080)

        theme_sheet = self._parse_css_sheet(theme_path, "theme")
        widgets_sheet = self._parse_css_sheet(widgets_path, "widgets")
        self.global_errors.extend(theme_sheet.errors)
        self.global_errors.extend(widgets_sheet.errors)
        self.global_warnings.extend(theme_sheet.warnings)
        self.global_warnings.extend(widgets_sheet.warnings)

        if theme_path.exists():
            self._validate_body_rule(theme_sheet, design_width, design_height)

        if contract:
            contract_site = contract.get("site", {})
            if isinstance(contract_site, dict):
                for key in ("siteId", "designWidth", "designHeight", "themeCss"):
                    if manifest.get(key) != contract_site.get(key):
                        self.global_errors.append(f"ui_contract.json site.{key} does not match site.json.")

        manifest_page: Optional[Dict[str, object]] = None
        for page in manifest.get("pages", []) if isinstance(manifest, dict) else []:
            if not isinstance(page, dict):
                continue
            if str(page.get("pageId", "")).strip() == page_id:
                manifest_page = page
                break

        if manifest_page is None:
            self.global_errors.append(f"site.json does not contain page '{page_id}'.")
            page_report_data = PageValidationReport(page_id=page_id, html_path="").finalize()
        else:
            html_rel = str(manifest_page.get("html", "")).strip()
            page_report = PageValidationReport(page_id=page_id, html_path=html_rel)

            if not html_rel:
                page_report.add_error(f"Page '{page_id}' is missing html path in site.json.")
            if not str(manifest_page.get("prefabName", "")).strip():
                page_report.add_error(f"Page '{page_id}' is missing prefabName in site.json.")

            target_layer = str(manifest_page.get("targetLayer", "")).strip()
            if target_layer and target_layer not in DEFAULT_ALLOWED_TARGET_LAYERS:
                self.global_warnings.append(f"Page '{page_id}' uses unrecognized targetLayer '{target_layer}'.")

            html_path = self.source_root / html_rel if html_rel else None
            if not html_rel or html_path is None or not html_path.exists():
                page_report.add_error(f"Missing HTML file for page '{page_id}': {html_rel or '<missing html path>'}")
                page_report_data = page_report.finalize()
            else:
                html_text = html_path.read_text(encoding="utf-8")
                local_sheets = self._load_local_style_sheets(manifest_page, html_text, page_report)
                for local_sheet in local_sheets:
                    self.global_errors.extend(local_sheet.errors)
                    self.global_warnings.extend(local_sheet.warnings)

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

                self._validate_single_page(
                    manifest_page=manifest_page,
                    page_contract=contract_pages.get(page_id),
                    page_self_check=self_check_pages.get(page_id),
                    html_text=html_text,
                    page_report=page_report,
                    theme_sheet=theme_sheet,
                    widgets_sheet=widgets_sheet,
                    local_sheets=local_sheets,
                    design_width=design_width,
                    design_height=design_height,
                    site_theme_href=str(manifest.get("themeCss", "")).strip(),
                    site_shared_hrefs=[str(item).strip() for item in manifest.get("sharedStyles", []) if str(item).strip()],
                )
                page_report_data = page_report.finalize()

        total_errors = len(self.global_errors) + int(page_report_data["errorCount"])
        total_warnings = len(self.global_warnings) + int(page_report_data["warningCount"])

        return {
            "reportVersion": REPORT_VERSION,
            "siteRoot": str(self.site_root),
            "siteId": site_id,
            "pageId": page_id,
            "status": "pass" if total_errors == 0 else "fail",
            "summary": {
                "errorCount": total_errors,
                "warningCount": total_warnings,
            },
            "violations": dedupe_strings(self.global_errors),
            "warnings": dedupe_strings(self.global_warnings),
            "pageReport": page_report_data,
        }

    def _resolve_default_allowlist(self) -> Optional[Path]:
        candidates = [
            self.site_root.parent / "ai-create-ugui" / "规范" / "AIToUGUI_Allowlist参考.json",
            self.site_root.parent / "规范" / "AIToUGUI_Allowlist参考.json",
            Path(__file__).resolve().parents[1] / "规范" / "AIToUGUI_Allowlist参考.json",
        ]
        for candidate in candidates:
            if candidate.exists():
                return candidate
        return None

    def _load_json(self, path: Path) -> Dict[str, object]:
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except FileNotFoundError:
            return {}
        except json.JSONDecodeError as exc:
            self.global_errors.append(f"Invalid JSON at {path.name}: {exc}")
            return {}

    def _parse_css_sheet(self, path: Path, kind: str) -> CssSheet:
        relative_source = path.relative_to(self.site_root).as_posix() if path.exists() and self.site_root in path.parents else str(path)
        if not path.exists():
            return CssSheet(source=relative_source, kind=kind, errors=[f"Missing CSS file: {relative_source}"])
        return self._parse_css_text(path.read_text(encoding="utf-8"), relative_source, kind)

    def _parse_css_text(self, text: str, source: str, kind: str) -> CssSheet:
        sheet = CssSheet(source=source, kind=kind)
        stripped = CSS_COMMENT_REGEX.sub("", text)
        for match in CSS_RULE_REGEX.finditer(stripped):
            selector_block = match.group("selector")
            body = match.group("body")
            line = stripped[: match.start()].count("\n") + 1
            declarations = parse_style_declarations(body)
            selectors = [selector.strip() for selector in selector_block.split(",") if selector.strip()]
            for selector in selectors:
                self._validate_css_selector(selector, source, sheet)
                self._validate_css_declarations(selector, declarations, source, kind, sheet)
                role_match = SELECTOR_ROLE_REGEX.match(selector)
                if role_match:
                    sheet.roles.add(role_match.group(1))
                elif SELECTOR_CLASS_REGEX.match(selector):
                    sheet.classes.add(selector[1:])
                sheet.rules.append(CssRule(selector=selector, declarations=dict(declarations), source=source, line=line))
        return sheet

    def _validate_css_selector(self, selector: str, source: str, sheet: CssSheet) -> None:
        if selector in {":root", "body", "html"}:
            return
        if SELECTOR_CLASS_REGEX.match(selector):
            return
        if SELECTOR_ROLE_REGEX.match(selector):
            return
        # Preview-only selectors (tag selectors, universal selectors, pseudo-elements) never
        # reach Unity. Treat as style-level warning rather than blocking error so AI is not
        # punished for emitting conventional browser-preview CSS (h1, p, *, ::before, etc).
        sheet.warnings.append(f"[{source}] Unsupported selector (preview-only, ignored by Unity): {selector}")

    def _validate_css_declarations(self, selector: str, declarations: Dict[str, str], source: str, kind: str, sheet: CssSheet) -> None:
        # Unified property allowlist: the theme/widgets/inline split was artificial and trained AI
        # to inline everything. Unity consumes the same property set regardless of which stylesheet
        # declared it. All property-level complaints are warnings -- compile will either handle or
        # auto-downgrade, see compile_report.downgrades[].
        allowed = self.allowed_css_properties

        for name, value in declarations.items():
            lowered_name = name.lower()
            normalized_value = value.strip()

            if lowered_name.startswith("--") and selector == ":root":
                continue

            if lowered_name not in allowed:
                sheet.warnings.append(f"[{source}] Unsupported CSS property '{lowered_name}' in selector '{selector}'.")

            if lowered_name in self.allowed_value_rules:
                normalized_key = normalize_whitespace(normalized_value).lower()
                if lowered_name in {"margin-left", "margin-top"} and is_pixel_or_zero_or_var(normalized_value):
                    pass
                elif normalized_key not in self.allowed_value_rules[lowered_name] and not is_css_var(normalized_value):
                    sheet.warnings.append(f"[{source}] Unsupported value '{normalized_value}' for '{lowered_name}' in selector '{selector}'.")

            if lowered_name == "background":
                lowered_value = normalized_value.lower()
                if "repeating-linear-gradient" in lowered_value or "repeating-radial-gradient" in lowered_value:
                    sheet.warnings.append(f"[{source}] Unsupported background pattern '{normalized_value}' in selector '{selector}'.")
                elif "radial-gradient" in lowered_value:
                    sheet.warnings.append(f"[{source}] Radial gradient may not match Unity exactly in selector '{selector}'.")
                elif "linear-gradient" not in lowered_value and not is_color_literal(normalized_value):
                    sheet.warnings.append(f"[{source}] Unsupported background value '{normalized_value}' in selector '{selector}'.")

            if lowered_name == "transform" and normalized_value and not is_supported_transform_value(normalized_value):
                sheet.warnings.append(
                    f"[{source}] Unsupported transform value '{normalized_value}' in selector '{selector}'. Only rotate(<deg>) is applied; other transforms are dropped."
                )

            if lowered_name == "opacity" and normalized_value and not is_supported_opacity_value(normalized_value):
                sheet.warnings.append(
                    f"[{source}] Unsupported opacity value '{normalized_value}' in selector '{selector}'. Only values between 0 and 1 are applied."
                )

            if lowered_name == "border-style":
                normalized_border_style = normalize_whitespace(normalized_value).lower()
                if normalized_border_style not in {"solid", "dashed"} and not is_css_var(normalized_value):
                    sheet.warnings.append(
                        f"[{source}] Unsupported border-style '{normalized_value}' in selector '{selector}'."
                    )

            if lowered_name == "animation" and normalized_value and not is_supported_animation_value(normalized_value):
                sheet.warnings.append(
                    f"[{source}] Unsupported animation '{normalized_value}' in selector '{selector}'. Only rotate/float/pulse loop presets are applied."
                )

            if lowered_name == "animation-delay" and normalized_value and not is_supported_animation_delay_value(normalized_value):
                sheet.warnings.append(
                    f"[{source}] Unsupported animation-delay '{normalized_value}' in selector '{selector}'."
                )

            for label, pattern in BROWSER_ONLY_PATTERN_RULES:
                if pattern.search(f"{lowered_name}:{normalized_value}"):
                    sheet.warnings.append(f"[{source}] Browser-only pattern detected ({label}) in selector '{selector}'.")

            for label, pattern in BROWSER_ONLY_WARNING_RULES:
                if pattern.search(f"{lowered_name}:{normalized_value}"):
                    sheet.warnings.append(f"[{source}] Browser-only pattern detected ({label}) in selector '{selector}'.")

            for label, pattern in DISCOURAGED_UNIT_PATTERNS:
                if pattern.search(normalized_value):
                    sheet.warnings.append(f"[{source}] Discouraged unit pattern ({label}) in selector '{selector}'.")

    def _validate_body_rule(self, theme_sheet: CssSheet, design_width: int, design_height: int) -> None:
        body_rule = self._resolve_rule_block(theme_sheet.rules, "body")
        if not body_rule:
            self.global_errors.append("theme.css is missing a body rule.")
            return

        if body_rule.get("width", "") != f"{design_width}px":
            self.global_errors.append(f"theme.css body width must be {design_width}px.")
        if body_rule.get("height", "") != f"{design_height}px":
            self.global_errors.append(f"theme.css body height must be {design_height}px.")
        if body_rule.get("margin", "") not in {"0", "0px"}:
            self.global_errors.append("theme.css body margin must be 0.")
        if body_rule.get("overflow", "").lower() != "hidden":
            self.global_errors.append("theme.css body overflow must be hidden.")

    def _validate_manifest(self, manifest: Dict[str, object], contract: Dict[str, object], self_check: Dict[str, object]) -> None:
        if not manifest:
            self.global_errors.append("site.json is missing or invalid.")
            return

        for field_name in ("siteId", "displayName", "designWidth", "designHeight", "themeCss", "sharedStyles", "pages"):
            if field_name not in manifest:
                self.global_errors.append(f"site.json is missing required field '{field_name}'.")

        page_ids: Set[str] = set()
        for page in manifest.get("pages", []):
            if not isinstance(page, dict):
                self.global_errors.append("site.json contains a non-object page entry.")
                continue
            page_id = str(page.get("pageId", "")).strip()
            if not page_id:
                self.global_errors.append("A page in site.json is missing pageId.")
                continue
            if page_id in page_ids:
                self.global_errors.append(f"Duplicate pageId in site.json: {page_id}")
            page_ids.add(page_id)

            if not str(page.get("html", "")).strip():
                self.global_errors.append(f"Page '{page_id}' is missing html path in site.json.")
            if not str(page.get("prefabName", "")).strip():
                self.global_errors.append(f"Page '{page_id}' is missing prefabName in site.json.")
            target_layer = str(page.get("targetLayer", "")).strip()
            if target_layer and target_layer not in DEFAULT_ALLOWED_TARGET_LAYERS:
                self.global_warnings.append(f"Page '{page_id}' uses unrecognized targetLayer '{target_layer}'.")

        if contract:
            contract_site = contract.get("site", {})
            if isinstance(contract_site, dict):
                for key in ("siteId", "designWidth", "designHeight", "themeCss"):
                    if manifest.get(key) != contract_site.get(key):
                        self.global_errors.append(f"ui_contract.json site.{key} does not match site.json.")

        if self_check:
            status = str(self_check.get("status", "")).strip().lower()
            violations = self_check.get("violations", [])
            if status != "pass":
                self.global_errors.append("ui_self_check_report.json status must be 'pass'.")
            if isinstance(violations, list) and violations:
                self.global_errors.append("ui_self_check_report.json has non-empty violations.")

    def _load_local_style_sheets(self, manifest_page: Dict[str, object], html_text: str, page_report: PageValidationReport) -> List[CssSheet]:
        local_sheets: List[CssSheet] = []
        local_styles = manifest_page.get("localStyles", [])
        if isinstance(local_styles, list):
            for local_style in local_styles:
                rel = str(local_style).strip()
                if not rel:
                    continue
                path = self.site_root / rel
                if not path.exists():
                    page_report.add_error(f"Missing local style file: {rel}")
                    continue
                local_sheets.append(self._parse_css_sheet(path, "local"))

        for index, match in enumerate(STYLE_TAG_REGEX.finditer(html_text), start=1):
            source = f"{manifest_page.get('html', '<html>')}#inline-style-{index}"
            local_sheets.append(self._parse_css_text(match.group(1), source, "local"))

        return local_sheets

    def _validate_single_page(
        self,
        manifest_page: Dict[str, object],
        page_contract: Optional[Dict[str, object]],
        page_self_check: Optional[Dict[str, object]],
        html_text: str,
        page_report: PageValidationReport,
        theme_sheet: CssSheet,
        widgets_sheet: CssSheet,
        local_sheets: List[CssSheet],
        design_width: int,
        design_height: int,
        site_theme_href: str,
        site_shared_hrefs: List[str],
    ) -> None:
        parser = LimitedHtmlTreeBuilder()
        parser.feed(html_text)
        all_nodes = self._flatten_nodes(parser.document)

        for node in all_nodes:
            self._validate_html_node(node, page_report)
            self._validate_inline_styles(node, page_report)

        page_root = self._find_page_root(all_nodes, str(manifest_page.get("pageId", "")).strip())
        if page_root is None:
            page_report.add_error(f"Page '{manifest_page.get('pageId', '')}' is missing a root node with data-ui-page.")
            return

        name_map: Dict[str, List[HtmlNode]] = {}
        semantic_maps: Dict[str, Dict[str, List[HtmlNode]]] = {
            "data-ui-slot": {},
            "data-ui-container": {},
            "data-ui-template": {},
        }
        for node in all_nodes:
            if node.name:
                name_map.setdefault(node.name, []).append(node)
            for attr_name, value_map in semantic_maps.items():
                raw_value = node.attrs.get(attr_name)
                if raw_value is None:
                    continue
                normalized_value = normalize_whitespace(raw_value)
                if not normalized_value:
                    page_report.missing_required_attributes.append(
                        f"{node.display_label()} declares '{attr_name}' but leaves it empty."
                    )
                    continue
                if not node.name:
                    page_report.missing_required_attributes.append(
                        f"{node.display_label()} uses '{attr_name}=\"{normalized_value}\"' but is missing required data-ui-name."
                    )
                value_map.setdefault(normalized_value, []).append(node)
        for name, nodes in name_map.items():
            if len(nodes) > 1:
                page_report.add_error(f"Duplicate data-ui-name '{name}' in page '{page_report.page_id}'.")
        for attr_name, value_map in semantic_maps.items():
            for semantic_id, nodes in value_map.items():
                if len(nodes) > 1:
                    page_report.contract_mismatches.append(
                        f"Duplicate {attr_name} '{semantic_id}' in page '{page_report.page_id}'."
                    )

        defined_classes = set(theme_sheet.classes) | set(widgets_sheet.classes)
        defined_roles = set(theme_sheet.roles) | set(widgets_sheet.roles)
        all_rules = list(theme_sheet.rules) + list(widgets_sheet.rules)
        for sheet in local_sheets:
            defined_classes |= set(sheet.classes)
            defined_roles |= set(sheet.roles)
            all_rules.extend(sheet.rules)
        style_tokens = self._collect_tokens(all_rules)
        resolved_styles = {
            id(node): self._resolve_styles(node, all_rules, style_tokens)
            for node in all_nodes
            if node.tag != "#document"
        }

        self._validate_root_node(
            page_root=page_root,
            page_report=page_report,
            manifest_page=manifest_page,
            design_width=design_width,
            design_height=design_height,
            resolved_style=resolved_styles.get(id(page_root), {}),
        )
        self._validate_page_geometry(
            all_nodes=all_nodes,
            page_root=page_root,
            resolved_styles=resolved_styles,
            page_report=page_report,
            design_width=design_width,
            design_height=design_height,
        )

        page_html_rel = str(manifest_page.get("html", "")).strip()
        linked_styles = self._extract_link_hrefs(all_nodes, page_html_rel)
        if site_theme_href and site_theme_href not in linked_styles:
            page_report.add_warning(f"HTML page does not link site theme stylesheet '{site_theme_href}'.")
        for shared_href in site_shared_hrefs:
            if shared_href and shared_href not in linked_styles:
                page_report.add_warning(f"HTML page does not link shared stylesheet '{shared_href}'.")

        for node in all_nodes:
            if node.tag == "#document":
                continue

            for class_name in node.class_list:
                if class_name not in defined_classes:
                    page_report.undefined_classes.append(f"{node.display_label()} uses undefined class '{class_name}'.")

            if node.role and node.role not in defined_roles and not node.role.startswith(LAYOUT_ROLE_PREFIXES):
                page_report.undefined_roles.append(f"{node.display_label()} uses undefined role '{node.role}'.")

            resolved_style = resolved_styles.get(id(node), {})
            self._validate_required_size(node, page_contract, resolved_style, page_report)
            self._validate_percent_usage(node, page_contract, resolved_style, page_report)
            self._validate_button_requirements(node, resolved_style, page_report)
            self._validate_primitive_requirements(node, resolved_style, page_report)
            self._validate_shape_contract(node, resolved_style, page_report)
            self._validate_layout_safety(node, resolved_style, page_report, page_contract, all_rules, style_tokens)

        self._validate_contract(page_contract, manifest_page, name_map, page_root, all_rules, style_tokens, page_report)
        self._validate_self_check(page_self_check, page_report)

    def _validate_html_node(self, node: HtmlNode, page_report: PageValidationReport) -> None:
        if node.tag == "#document":
            return
        if node.tag not in self.allowed_tags:
            page_report.unsupported_tags.append(f"{node.display_label()} uses unsupported tag '{node.tag}'.")
        for attribute_name in node.attrs.keys():
            if attribute_name not in self.allowed_attributes:
                page_report.unsupported_attributes.append(f"{node.display_label()} uses unsupported attribute '{attribute_name}'.")

    def _validate_inline_styles(self, node: HtmlNode, page_report: PageValidationReport) -> None:
        for name, value in node.inline_styles.items():
            lowered_name = name.lower()
            if lowered_name not in self.allowed_inline_properties:
                page_report.unsupported_properties.append(f"{node.display_label()} uses unsupported inline property '{lowered_name}'.")

            if lowered_name in self.allowed_value_rules:
                normalized_value = normalize_whitespace(value).lower()
                if lowered_name in {"margin-left", "margin-top"} and is_pixel_or_zero_or_var(normalized_value):
                    pass
                elif normalized_value not in self.allowed_value_rules[lowered_name] and not is_css_var(value):
                    page_report.unsupported_value_patterns.append(f"{node.display_label()} has unsupported value '{value}' for '{lowered_name}'.")

            if lowered_name == "background":
                lowered_value = value.lower()
                if "repeating-linear-gradient" in lowered_value or "repeating-radial-gradient" in lowered_value:
                    page_report.unsupported_value_patterns.append(f"{node.display_label()} uses unsupported background pattern '{value}'.")
                elif "radial-gradient" in lowered_value:
                    page_report.browser_only_patterns.append(f"{node.display_label()} uses radial-gradient; Unity may only approximate it.")
                elif "url(" in lowered_value and has_explicit_asset_background(node):
                    pass
                elif "linear-gradient" not in lowered_value and not is_color_literal(value):
                    page_report.unsupported_value_patterns.append(f"{node.display_label()} uses unsupported background value '{value}'.")

            for label, pattern in BROWSER_ONLY_PATTERN_RULES:
                if pattern.search(f"{lowered_name}:{value}"):
                    page_report.browser_only_patterns.append(f"{node.display_label()} triggers browser-only pattern '{label}'.")

            for label, pattern in BROWSER_ONLY_WARNING_RULES:
                if pattern.search(f"{lowered_name}:{value}"):
                    page_report.add_warning(f"{node.display_label()} triggers browser-only pattern '{label}' (Unity supports presets only).")

            for label, pattern in DISCOURAGED_UNIT_PATTERNS:
                if pattern.search(value):
                    page_report.unsupported_value_patterns.append(f"{node.display_label()} uses unsupported unit pattern '{label}' in value '{value}'.")

    def _find_page_root(self, all_nodes: List[HtmlNode], page_id: str) -> Optional[HtmlNode]:
        for node in all_nodes:
            if node.attrs.get("data-ui-page") == page_id:
                return node
        return None

    def _normalize_element_identity(self, node: HtmlNode) -> Tuple[str, str]:
        raw_element = normalize_whitespace(node.attrs.get("data-ui-element", "")).lower()
        raw_variant = normalize_whitespace(node.attrs.get("data-ui-variant", "")).lower()
        if not raw_element:
            return "", ""

        if raw_element in self.primitive_elements:
            return raw_element, normalize_variant_id(raw_variant).lower()

        if "/" in raw_element:
            base, variant = raw_element.split("/", 1)
            base = base.strip().lower()
            variant = variant.strip().lower()
            if base in self.primitive_elements:
                return base, normalize_variant_id(raw_variant or variant).lower()

        return raw_element, raw_variant

    def _infer_standard_primitive(self, node: HtmlNode) -> str:
        explicit_type = normalize_whitespace(node.attrs.get("data-ui-type", "")).lower()
        if explicit_type in {"button", "input", "toggle", "slider", "dropdown", "scrollbar", "scroll", "scrollview", "image", "progress"}:
            return "scrollview" if explicit_type == "scroll" else explicit_type

        tag = node.tag.lower()
        if tag == "button":
            return "button"
        if tag in {"input", "textarea"}:
            return "input"
        if tag == "img":
            return "image"

        normalized_element, _ = self._normalize_element_identity(node)
        if normalized_element in self.primitive_elements:
            return normalized_element

        return ""

    def _is_allowed_text_child(self, node: HtmlNode) -> bool:
        return node.tag in TEXT_LIKE_TAGS and not node.children

    def _validate_root_node(
        self,
        page_root: HtmlNode,
        page_report: PageValidationReport,
        manifest_page: Dict[str, object],
        design_width: int,
        design_height: int,
        resolved_style: Dict[str, str],
    ) -> None:
        for attribute_name in self.required_root_attributes:
            if not page_root.attrs.get(attribute_name):
                page_report.missing_required_attributes.append(f"{page_root.display_label()} is missing required root attribute '{attribute_name}'.")

        if page_root.attrs.get("data-ui-page") != str(manifest_page.get("pageId", "")).strip():
            page_report.metadata_mismatches.append("Root data-ui-page does not match manifest pageId.")

        width = resolved_style.get("width", "")
        height = resolved_style.get("height", "")
        if width and width != f"{design_width}px":
            page_report.metadata_mismatches.append(f"{page_root.display_label()} width should be {design_width}px, found '{width}'.")
        if height and height != f"{design_height}px":
            page_report.metadata_mismatches.append(f"{page_root.display_label()} height should be {design_height}px, found '{height}'.")
        if resolved_style.get("box-sizing", "").strip().lower() != "border-box":
            page_report.metadata_mismatches.append(f"{page_root.display_label()} root must resolve to box-sizing:border-box.")
        left = resolved_style.get("left", "").strip()
        top = resolved_style.get("top", "").strip()
        if left and parse_px_value(left) not in {0.0}:
            page_report.metadata_mismatches.append(f"{page_root.display_label()} left should be 0px, found '{left}'.")
        if top and parse_px_value(top) not in {0.0}:
            page_report.metadata_mismatches.append(f"{page_root.display_label()} top should be 0px, found '{top}'.")

    # CSS shorthands reset their longhand sub-properties to initial when applied.
    # We replicate that cascade behavior so a later `background: transparent`
    # clears an earlier `background-color`, matching how a browser renders the
    # source. Without this, plain dict.update() lets the stale longhand survive
    # and the Unity baker paints a fill the browser preview never shows.
    _SHORTHAND_LONGHANDS: Dict[str, Tuple[str, ...]] = {
        "background": (
            "background-color",
            "background-image",
            "background-position",
            "background-size",
            "background-repeat",
            "background-attachment",
            "background-origin",
            "background-clip",
        ),
    }

    def _merge_declarations(self, resolved: Dict[str, str], declarations: Dict[str, str]) -> None:
        for prop, value in declarations.items():
            longhands = self._SHORTHAND_LONGHANDS.get(prop)
            if longhands:
                for longhand in longhands:
                    resolved.pop(longhand, None)
            resolved[prop] = value

    def _resolve_styles(self, node: HtmlNode, rules: List[CssRule], tokens: Optional[Dict[str, str]] = None) -> Dict[str, str]:
        resolved: Dict[str, str] = {}
        for rule in rules:
            if self._selector_matches_node(rule.selector, node):
                self._merge_declarations(resolved, rule.declarations)
        self._merge_declarations(resolved, node.inline_styles)
        if tokens:
            resolved = {
                key: resolve_css_value(value, tokens)
                for key, value in resolved.items()
            }
        return resolved

    def _collect_tokens(self, rules: List[CssRule]) -> Dict[str, str]:
        raw_tokens = self._resolve_rule_block(rules, ":root")
        tokens: Dict[str, str] = {}
        for key, value in raw_tokens.items():
            if not key.startswith("--"):
                continue
            tokens[key] = resolve_css_value(str(value), raw_tokens)
        return tokens

    def _selector_matches_node(self, selector: str, node: HtmlNode) -> bool:
        selector = selector.strip()
        if selector == "body" and node.tag == "body":
            return True
        if selector == "html" and node.tag == "html":
            return True
        if SELECTOR_CLASS_REGEX.match(selector):
            return selector[1:] in node.class_list
        role_match = SELECTOR_ROLE_REGEX.match(selector)
        if role_match:
            return role_match.group(1) == node.role
        return False

    def _validate_required_size(self, node: HtmlNode, page_contract: Optional[Dict[str, object]], resolved_style: Dict[str, str], page_report: PageValidationReport) -> None:
        requires_size = node.tag == "button" or node.role.startswith("button/")
        primitive_id = self._infer_standard_primitive(node)
        if primitive_id in self.primitive_elements:
            requires_size = True
        if page_contract and node.name in self._contract_fixed_node_names(page_contract):
            requires_size = True
        if not requires_size:
            return
        if not resolved_style.get("width", ""):
            page_report.missing_required_size_nodes.append(f"{node.display_label()} is missing required width.")
        if not resolved_style.get("height", ""):
            page_report.missing_required_size_nodes.append(f"{node.display_label()} is missing required height.")

    def _validate_percent_usage(self, node: HtmlNode, page_contract: Optional[Dict[str, object]], resolved_style: Dict[str, str], page_report: PageValidationReport) -> None:
        relative_nodes = self._contract_relative_node_names(page_contract) if page_contract else set()
        if node.name and node.name in relative_nodes:
            return
        for property_name in ("width", "height", "left", "right", "top", "bottom", "padding", "max-width", "min-width", "min-height"):
            value = resolved_style.get(property_name, "")
            if value and PERCENTAGE_REGEX.search(value):
                page_report.unsupported_value_patterns.append(f"{node.display_label()} uses percentage value '{value}' for '{property_name}' without Contract approval.")

    def _validate_button_requirements(self, node: HtmlNode, resolved_style: Dict[str, str], page_report: PageValidationReport) -> None:
        if node.tag != "button":
            return
        for attribute_name in self.required_button_attributes:
            if not node.attrs.get(attribute_name):
                page_report.missing_required_attributes.append(f"{node.display_label()} is missing required button attribute '{attribute_name}'.")
        if not resolved_style.get("width", "") or not resolved_style.get("height", ""):
            page_report.missing_required_size_nodes.append(f"{node.display_label()} button must resolve to explicit width and height.")

    def _validate_primitive_requirements(self, node: HtmlNode, resolved_style: Dict[str, str], page_report: PageValidationReport) -> None:
        primitive_id = self._infer_standard_primitive(node)
        if not primitive_id:
            return

        normalized_element, normalized_variant = self._normalize_element_identity(node)
        composite_candidate = self._is_composite_candidate(node, resolved_style, primitive_id)
        if primitive_id in self.primitive_elements and not normalized_element:
            page_report.missing_required_attributes.append(
                f"{node.display_label()} is missing required data-ui-element for primitive '{primitive_id}'."
            )
            return

        if normalized_element and normalized_element not in self.primitive_elements and primitive_id in self.primitive_elements:
            if composite_candidate:
                page_report.add_warning(
                    f"{node.display_label()} uses primitive control '{primitive_id}' with composite element '{normalized_element}'; Unity composite resolution will decide the final prefab path."
                )
            else:
                page_report.contract_mismatches.append(
                    f"{node.display_label()} uses primitive control '{primitive_id}' but data-ui-element '{normalized_element}' is not allowed."
                )
            return

        if normalized_element in self.primitive_elements:
            allowed_variants = self.variant_allowlist.get(normalized_element, {DEFAULT_PRIMITIVE_VARIANT})
            if normalize_variant_id(normalized_variant).lower() not in allowed_variants:
                if composite_candidate:
                    page_report.add_warning(
                        f"{node.display_label()} uses non-standard data-ui-variant '{normalized_variant or '<empty>'}' on primitive '{normalized_element}'; Unity composite/prefab resolution will attempt to absorb it."
                    )
                else:
                    page_report.contract_mismatches.append(
                        f"{node.display_label()} uses unsupported data-ui-variant '{normalized_variant or '<empty>'}' for primitive '{normalized_element}'."
                    )

            if normalized_element != "scrollview":
                if normalized_element in TEXT_CHILD_ALLOWED_PRIMITIVES:
                    if len(node.children) > 1 or any(not self._is_allowed_text_child(child) for child in node.children):
                        if composite_candidate:
                            page_report.add_warning(
                                f"{node.display_label()} uses authored child structure on primitive '{normalized_element}'; validator is deferring this to Unity composite resolution."
                            )
                        else:
                            page_report.contract_mismatches.append(
                                f"{node.display_label()} uses authored child structure that is not allowed for primitive '{normalized_element}'."
                            )
                elif node.children:
                    if composite_candidate:
                        page_report.add_warning(
                            f"{node.display_label()} uses authored child structure on primitive '{normalized_element}'; validator is deferring this to Unity composite resolution."
                        )
                    else:
                        page_report.contract_mismatches.append(
                            f"{node.display_label()} uses authored child structure that is not allowed for primitive '{normalized_element}'."
                        )

    def _is_composite_candidate(self, node: HtmlNode, resolved_style: Dict[str, str], primitive_id: str) -> bool:
        explicit_family = str(node.attrs.get("data-ui-component-family", "")).strip().lower()
        explicit_template = str(node.attrs.get("data-ui-template", "")).strip()
        explicit_slot = str(node.attrs.get("data-ui-slot", "")).strip()
        explicit_container = str(node.attrs.get("data-ui-container", "")).strip()
        if explicit_family or explicit_template or explicit_slot or explicit_container:
            return True

        semantic = " ".join(
            [
                node.name,
                node.role,
                node.attrs.get("data-ui-element", ""),
                node.attrs.get("data-ui-template", ""),
                " ".join(node.class_list),
            ]
        ).lower()
        if any(token in semantic for token in ("tab", "nav", "navigation", "window", "modal", "dialog", "card", "item", "header", "section", "row", "list", "sidebar")):
            return True

        if primitive_id == "button":
            if len(node.children) > 1:
                return True
            if node.attrs.get("data-ui-icon") or node.attrs.get("data-ui-value") or node.attrs.get("src"):
                return True

        if primitive_id in {"dropdown", "toggle", "progress"} and node.children:
            return True

        background = str(resolved_style.get("background", "")).strip().lower()
        if "url(" in background:
            return True

        return False

    # Shape topologies whose geometry is taken over by the Unity baker. They
    # are NOT rectangle-system outlines, so an explicit CSS border-radius on the
    # same node fights the baked geometry (e.g. cut-corner cutting the top while
    # border-radius rounds the bottom). The browser ignores data-ui-shape and
    # only shows the radius, so preview looks fine while Unity renders a clash.
    _NONRECT_SHAPES = {"cut-corner", "plate", "banner"}

    def _has_nonzero_border_radius(self, resolved_style: Dict[str, str]) -> bool:
        for prop in (
            "border-radius",
            "border-top-left-radius",
            "border-top-right-radius",
            "border-bottom-left-radius",
            "border-bottom-right-radius",
        ):
            raw = str(resolved_style.get(prop, "")).strip()
            if not raw:
                continue
            for token in raw.replace("/", " ").split():
                value = parse_px_value(token)
                if value is not None and value > 0.0:
                    return True
        return False

    def _validate_shape_contract(self, node: HtmlNode, resolved_style: Dict[str, str], page_report: PageValidationReport) -> None:
        shape_id = normalize_shape_id(node.attrs.get("data-ui-shape", ""))
        frame_id = normalize_frame_id(node.attrs.get("data-ui-frame", ""))
        if shape_id and shape_id not in self.shape_allowlist:
            page_report.add_warning(
                f"{node.display_label()} uses unsupported data-ui-shape '{shape_id}'."
            )

        if shape_id in self._NONRECT_SHAPES and self._has_nonzero_border_radius(resolved_style):
            page_report.add_error(
                f"{node.display_label()} combines non-rectangular data-ui-shape '{shape_id}' with a "
                f"non-zero border-radius. These are mutually exclusive: the Unity baker takes over the "
                f"shape geometry while the browser only shows the radius, so Unity renders a corner clash "
                f"the preview never shows. Remove border-radius, or drop data-ui-shape and use a "
                f"rectangle-system shape (roundrect/per-corner)."
            )

        if frame_id and frame_id not in self.frame_allowlist:
            page_report.add_warning(
                f"{node.display_label()} uses unsupported data-ui-frame '{frame_id}'."
            )

        primitive_id = self._infer_standard_primitive(node)
        if not primitive_id or not shape_id:
            return

        allowed_shapes = self.element_shape_allowlist.get(primitive_id)
        if allowed_shapes and shape_id not in allowed_shapes:
            page_report.add_warning(
                f"{node.display_label()} uses data-ui-shape '{shape_id}' which is not allowed for primitive '{primitive_id}'."
            )

    def _validate_layout_safety(
        self,
        node: HtmlNode,
        resolved_style: Dict[str, str],
        page_report: PageValidationReport,
        page_contract: Optional[Dict[str, object]],
        all_rules: List[CssRule],
        tokens: Dict[str, str],
    ) -> None:
        self._validate_border_box_requirement(node, resolved_style, page_report, page_contract)
        self._validate_flex_footprint(node, resolved_style, page_report, all_rules, tokens)
        self._validate_inflow_band_structure(node, resolved_style, page_report, all_rules, tokens)

    def _validate_border_box_requirement(
        self,
        node: HtmlNode,
        resolved_style: Dict[str, str],
        page_report: PageValidationReport,
        page_contract: Optional[Dict[str, object]],
    ) -> None:
        width_px = parse_px_value(resolved_style.get("width", ""))
        height_px = parse_px_value(resolved_style.get("height", ""))
        if width_px is None and height_px is None:
            return

        padding = self._resolve_box_sides(resolved_style, "padding")
        border_width = self._parse_border_width(resolved_style)
        has_shell = (padding is not None and any(side > 0 for side in padding)) or (border_width or 0.0) > 0.0
        if not has_shell:
            return

        if self._is_relative_contract_node(node, page_contract):
            return

        if resolved_style.get("box-sizing", "").strip().lower() != "border-box":
            dimensions: List[str] = []
            if width_px is not None:
                dimensions.append("width")
            if height_px is not None:
                dimensions.append("height")
            joined_dimensions = "/".join(dimensions) if dimensions else "size"
            page_report.add_error(
                f"{node.display_label()} has explicit {joined_dimensions} with padding/border but does not resolve to box-sizing:border-box."
            )

    def _validate_flex_footprint(
        self,
        node: HtmlNode,
        resolved_style: Dict[str, str],
        page_report: PageValidationReport,
        all_rules: List[CssRule],
        tokens: Dict[str, str],
    ) -> None:
        if resolved_style.get("display", "").strip().lower() != "flex":
            return

        direction = resolved_style.get("flex-direction", "row").strip().lower() or "row"
        if direction not in {"row", "column"}:
            return

        main_axis = "width" if direction == "row" else "height"
        cross_axis = "height" if direction == "row" else "width"
        available_main = self._content_box_size(resolved_style, main_axis)
        available_cross = self._content_box_size(resolved_style, cross_axis)
        if available_main is None:
            return

        gap = parse_px_value(resolved_style.get("gap", ""))
        if gap is None and resolved_style.get("gap"):
            return
        gap_value = gap or 0.0

        flow_children: List[Tuple[HtmlNode, float, float]] = []
        for child in node.children:
            child_style = self._resolve_styles(child, all_rules, tokens)
            if child_style.get("position", "").strip().lower() == "absolute":
                continue

            child_main = self._outer_box_size(child_style, main_axis)
            if child_main is None:
                return

            margin_sides = self._resolve_box_sides(child_style, "margin", allow_auto=True)
            if margin_sides is None:
                return

            if direction == "row":
                child_main += margin_sides[1] + margin_sides[3]
            else:
                child_main += margin_sides[0] + margin_sides[2]

            child_cross = self._outer_box_size(child_style, cross_axis)
            if child_cross is not None and available_cross is not None:
                flow_children.append((child, child_main, child_cross))
            else:
                flow_children.append((child, child_main, -1.0))

        if not flow_children:
            return

        total_main = sum(item[1] for item in flow_children) + gap_value * max(0, len(flow_children) - 1)
        if total_main > available_main + FOOTPRINT_TOLERANCE_PX:
            page_report.add_error(
                f"{node.display_label()} flex footprint exceeds available {main_axis} ({total_main:.1f}px > {available_main:.1f}px)."
            )

        if available_cross is None:
            return

        for child, _, child_cross in flow_children:
            if child_cross < 0.0:
                continue
            if child_cross > available_cross + FOOTPRINT_TOLERANCE_PX:
                page_report.add_error(
                    f"{child.display_label()} cross-axis footprint exceeds parent {cross_axis} in {node.display_label()} ({child_cross:.1f}px > {available_cross:.1f}px)."
                )

    def _validate_inflow_band_structure(
        self,
        node: HtmlNode,
        resolved_style: Dict[str, str],
        page_report: PageValidationReport,
        all_rules: List[CssRule],
        tokens: Dict[str, str],
    ) -> None:
        display = resolved_style.get("display", "").strip().lower()
        if display == "flex":
            return

        available_width = self._content_box_size(resolved_style, "width")
        available_height = self._content_box_size(resolved_style, "height")
        if available_width is None or available_height is None:
            return

        flow_children: List[Tuple[HtmlNode, float, float]] = []
        for child in node.children:
            child_style = self._resolve_styles(child, all_rules, tokens)
            if child_style.get("position", "").strip().lower() == "absolute":
                continue

            child_width = self._outer_box_size(child_style, "width")
            child_height = self._outer_box_size(child_style, "height")
            if child_width is None or child_height is None:
                continue

            margin_sides = self._resolve_box_sides(child_style, "margin", allow_auto=True)
            if margin_sides is None:
                continue

            total_width = child_width + margin_sides[1] + margin_sides[3]
            total_height = child_height + margin_sides[0] + margin_sides[2]
            flow_children.append((child, total_width, total_height))

        if len(flow_children) < 2:
            return

        for index in range(len(flow_children) - 1):
            first, first_width, first_height = flow_children[index]
            second, second_width, second_height = flow_children[index + 1]

            if first_width < 120.0 or second_width < 120.0:
                continue

            combined_width = first_width + second_width
            if combined_width > available_width + FOOTPRINT_TOLERANCE_PX:
                continue
            if combined_width < available_width * 0.75:
                continue

            stacked_height = first_height + second_height
            if stacked_height <= available_height + FOOTPRINT_TOLERANCE_PX:
                continue

            side_by_side_height = max(first_height, second_height)
            if side_by_side_height > available_height + FOOTPRINT_TOLERANCE_PX:
                continue

            page_report.add_error(
                f"{first.display_label()} and {second.display_label()} look like side-by-side siblings inside {node.display_label()}, but {node.display_label()} is not using a fixed row wrapper (display:flex) or absolute layout; they will stack vertically."
            )
            break

    def _validate_page_geometry(
        self,
        all_nodes: List[HtmlNode],
        page_root: HtmlNode,
        resolved_styles: Dict[int, Dict[str, str]],
        page_report: PageValidationReport,
        design_width: int,
        design_height: int,
    ) -> None:
        frame_cache: Dict[int, GeometryFrame] = {
            id(page_root): GeometryFrame(
                left=0.0,
                top=0.0,
                width=self._outer_box_size(resolved_styles.get(id(page_root), {}), "width") or float(design_width),
                height=self._outer_box_size(resolved_styles.get(id(page_root), {}), "height") or float(design_height),
            )
        }

        for node in all_nodes:
            if node.tag == "#document":
                continue

            resolved_style = resolved_styles.get(id(node), {})
            if node is page_root:
                self._validate_root_frame(node, frame_cache[id(page_root)], page_report, design_width, design_height)
                continue

            if resolved_style.get("position", "").strip().lower() != "absolute":
                continue

            containing_node = self._find_positioned_container(node, page_root, resolved_styles)
            containing_frame = self._resolve_geometry_frame(containing_node, page_root, resolved_styles, frame_cache, design_width, design_height)
            if containing_frame is None:
                continue

            offsets = self._resolve_absolute_offsets(resolved_style, containing_frame)
            if offsets is None:
                continue

            local_left, local_top, width, height = offsets
            node_frame = GeometryFrame(
                left=containing_frame.left + local_left,
                top=containing_frame.top + local_top,
                width=width,
                height=height,
            )
            frame_cache[id(node)] = node_frame

            container_label = "page root" if containing_node is page_root else containing_node.display_label()
            if local_left < -GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} has negative left offset in {container_label} ({local_left:.1f}px)."
                )
            if local_top < -GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} has negative top offset in {container_label} ({local_top:.1f}px)."
                )
            if local_left + width > containing_frame.width + GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} exceeds {container_label} width ({local_left + width:.1f}px > {containing_frame.width:.1f}px)."
                )
            if local_top + height > containing_frame.height + GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} exceeds {container_label} height ({local_top + height:.1f}px > {containing_frame.height:.1f}px)."
                )

            if node_frame.left < -GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} resolves outside the {design_width}x{design_height} frame on the left ({node_frame.left:.1f}px)."
                )
            if node_frame.top < -GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} resolves outside the {design_width}x{design_height} frame on the top ({node_frame.top:.1f}px)."
                )
            if node_frame.left + node_frame.width > float(design_width) + GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} resolves outside the {design_width}x{design_height} frame on the right ({node_frame.left + node_frame.width:.1f}px > {float(design_width):.1f}px)."
                )
            if node_frame.top + node_frame.height > float(design_height) + GEOMETRY_TOLERANCE_PX:
                page_report.add_error(
                    f"{node.display_label()} resolves outside the {design_width}x{design_height} frame on the bottom ({node_frame.top + node_frame.height:.1f}px > {float(design_height):.1f}px)."
                )

    def _validate_root_frame(
        self,
        page_root: HtmlNode,
        root_frame: GeometryFrame,
        page_report: PageValidationReport,
        design_width: int,
        design_height: int,
    ) -> None:
        if abs(root_frame.left) > GEOMETRY_TOLERANCE_PX:
            page_report.add_error(f"{page_root.display_label()} root left must resolve to 0px.")
        if abs(root_frame.top) > GEOMETRY_TOLERANCE_PX:
            page_report.add_error(f"{page_root.display_label()} root top must resolve to 0px.")
        if abs(root_frame.width - float(design_width)) > GEOMETRY_TOLERANCE_PX:
            page_report.add_error(f"{page_root.display_label()} root width must resolve to {design_width}px.")
        if abs(root_frame.height - float(design_height)) > GEOMETRY_TOLERANCE_PX:
            page_report.add_error(f"{page_root.display_label()} root height must resolve to {design_height}px.")

    def _find_positioned_container(
        self,
        node: HtmlNode,
        page_root: HtmlNode,
        resolved_styles: Dict[int, Dict[str, str]],
    ) -> HtmlNode:
        current = node.parent
        while current is not None and current.tag != "#document":
            if current is page_root:
                return page_root
            current_style = resolved_styles.get(id(current), {})
            if current_style.get("position", "").strip().lower() in {"absolute", "relative", "fixed"}:
                return current
            current = current.parent
        return page_root

    def _resolve_geometry_frame(
        self,
        node: HtmlNode,
        page_root: HtmlNode,
        resolved_styles: Dict[int, Dict[str, str]],
        frame_cache: Dict[int, GeometryFrame],
        design_width: int,
        design_height: int,
    ) -> Optional[GeometryFrame]:
        cached = frame_cache.get(id(node))
        if cached is not None:
            return cached

        if node is page_root:
            root_frame = GeometryFrame(
                left=0.0,
                top=0.0,
                width=self._outer_box_size(resolved_styles.get(id(page_root), {}), "width") or float(design_width),
                height=self._outer_box_size(resolved_styles.get(id(page_root), {}), "height") or float(design_height),
            )
            frame_cache[id(page_root)] = root_frame
            return root_frame

        resolved_style = resolved_styles.get(id(node), {})
        if resolved_style.get("position", "").strip().lower() != "absolute":
            return None

        containing_node = self._find_positioned_container(node, page_root, resolved_styles)
        containing_frame = self._resolve_geometry_frame(
            containing_node,
            page_root,
            resolved_styles,
            frame_cache,
            design_width,
            design_height,
        )
        if containing_frame is None:
            return None

        offsets = self._resolve_absolute_offsets(resolved_style, containing_frame)
        if offsets is None:
            return None

        local_left, local_top, width, height = offsets
        frame = GeometryFrame(
            left=containing_frame.left + local_left,
            top=containing_frame.top + local_top,
            width=width,
            height=height,
        )
        frame_cache[id(node)] = frame
        return frame

    def _resolve_absolute_offsets(
        self,
        resolved_style: Dict[str, str],
        containing_frame: GeometryFrame,
    ) -> Optional[Tuple[float, float, float, float]]:
        width = self._outer_box_size(resolved_style, "width")
        height = self._outer_box_size(resolved_style, "height")
        if width is None or height is None:
            return None

        left = parse_px_value(resolved_style.get("left", ""))
        right = parse_px_value(resolved_style.get("right", ""))
        top = parse_px_value(resolved_style.get("top", ""))
        bottom = parse_px_value(resolved_style.get("bottom", ""))

        if left is None:
            if right is None:
                return None
            left = containing_frame.width - right - width
        if top is None:
            if bottom is None:
                return None
            top = containing_frame.height - bottom - height

        return left, top, width, height

    def _resolve_box_sides(
        self,
        resolved_style: Dict[str, str],
        property_name: str,
        allow_auto: bool = False,
    ) -> Optional[Tuple[float, float, float, float]]:
        shorthand_value = resolved_style.get(property_name, "")
        shorthand = self._parse_quad_value(shorthand_value, allow_auto=allow_auto) if shorthand_value else (0.0, 0.0, 0.0, 0.0)
        if shorthand is None:
            return None

        top, right, bottom, left = shorthand
        overrides = {
            "top": resolved_style.get(f"{property_name}-top", ""),
            "right": resolved_style.get(f"{property_name}-right", ""),
            "bottom": resolved_style.get(f"{property_name}-bottom", ""),
            "left": resolved_style.get(f"{property_name}-left", ""),
        }
        if overrides["top"]:
            parsed = self._parse_length_value(overrides["top"], allow_auto=allow_auto)
            if parsed is None:
                return None
            top = parsed
        if overrides["right"]:
            parsed = self._parse_length_value(overrides["right"], allow_auto=allow_auto)
            if parsed is None:
                return None
            right = parsed
        if overrides["bottom"]:
            parsed = self._parse_length_value(overrides["bottom"], allow_auto=allow_auto)
            if parsed is None:
                return None
            bottom = parsed
        if overrides["left"]:
            parsed = self._parse_length_value(overrides["left"], allow_auto=allow_auto)
            if parsed is None:
                return None
            left = parsed
        return top, right, bottom, left

    def _parse_quad_value(self, value: str, allow_auto: bool = False) -> Optional[Tuple[float, float, float, float]]:
        parts = [part for part in value.strip().split() if part]
        if not parts:
            return 0.0, 0.0, 0.0, 0.0

        parsed_parts: List[float] = []
        for part in parts:
            parsed = self._parse_length_value(part, allow_auto=allow_auto)
            if parsed is None:
                return None
            parsed_parts.append(parsed)

        if len(parsed_parts) == 1:
            top = right = bottom = left = parsed_parts[0]
        elif len(parsed_parts) == 2:
            top = bottom = parsed_parts[0]
            right = left = parsed_parts[1]
        elif len(parsed_parts) == 3:
            top = parsed_parts[0]
            right = left = parsed_parts[1]
            bottom = parsed_parts[2]
        elif len(parsed_parts) == 4:
            top, right, bottom, left = parsed_parts
        else:
            return None
        return top, right, bottom, left

    def _parse_length_value(self, value: str, allow_auto: bool = False) -> Optional[float]:
        normalized = value.strip().lower()
        if allow_auto and normalized == "auto":
            return 0.0
        return parse_px_value(normalized)

    def _parse_border_width(self, resolved_style: Dict[str, str]) -> Optional[float]:
        border_value = resolved_style.get("border", "").strip().lower()
        if not border_value or border_value in {"0", "0px", "none"}:
            return 0.0
        match = BORDER_WIDTH_REGEX.search(border_value)
        if not match:
            return None
        return float(match.group(0)[:-2])

    def _outer_box_size(self, resolved_style: Dict[str, str], axis: str) -> Optional[float]:
        base = parse_px_value(resolved_style.get(axis, ""))
        if base is None:
            return None

        if resolved_style.get("box-sizing", "").strip().lower() == "border-box":
            return base

        padding = self._resolve_box_sides(resolved_style, "padding")
        border_width = self._parse_border_width(resolved_style)
        if padding is None or border_width is None:
            return None

        if axis == "width":
            return base + padding[1] + padding[3] + border_width * 2.0
        return base + padding[0] + padding[2] + border_width * 2.0

    def _content_box_size(self, resolved_style: Dict[str, str], axis: str) -> Optional[float]:
        base = parse_px_value(resolved_style.get(axis, ""))
        if base is None:
            return None

        if resolved_style.get("box-sizing", "").strip().lower() != "border-box":
            return base

        padding = self._resolve_box_sides(resolved_style, "padding")
        border_width = self._parse_border_width(resolved_style)
        if padding is None or border_width is None:
            return None

        if axis == "width":
            return max(0.0, base - padding[1] - padding[3] - border_width * 2.0)
        return max(0.0, base - padding[0] - padding[2] - border_width * 2.0)

    def _is_relative_contract_node(self, node: HtmlNode, page_contract: Optional[Dict[str, object]]) -> bool:
        return bool(node.name and page_contract and node.name in self._contract_relative_node_names(page_contract))

    def _validate_contract(
        self,
        page_contract: Optional[Dict[str, object]],
        manifest_page: Dict[str, object],
        name_map: Dict[str, List[HtmlNode]],
        page_root: HtmlNode,
        all_rules: List[CssRule],
        tokens: Dict[str, str],
        page_report: PageValidationReport,
    ) -> None:
        if not page_contract:
            page_report.add_warning("ui_contract.json does not contain this page.")
            return

        for key in ("pageId", "html", "prefabName", "targetLayer"):
            if manifest_page.get(key) != page_contract.get(key):
                page_report.contract_mismatches.append(f"Contract field '{key}' does not match manifest.")

        contract_root = page_contract.get("root")
        if isinstance(contract_root, dict):
            self._compare_contract_node(contract_root, page_root, all_rules, tokens, page_report, "root")

        expected_names = {str(contract_root.get("name", "")).strip()} if isinstance(contract_root, dict) and contract_root.get("name") else set()
        for node_spec in page_contract.get("namedNodes", []) if isinstance(page_contract.get("namedNodes", []), list) else []:
            if not isinstance(node_spec, dict):
                continue
            node_name = str(node_spec.get("name", "")).strip()
            if not node_name:
                continue
            expected_names.add(node_name)
            html_nodes = name_map.get(node_name, [])
            if not html_nodes:
                page_report.contract_mismatches.append(f"Contract named node '{node_name}' was not found in HTML.")
                continue
            self._compare_contract_node(node_spec, html_nodes[0], all_rules, tokens, page_report, f"named node '{node_name}'")

        template_nodes = {
            str(item.get("name", "")).strip()
            for item in page_contract.get("templateSizeNodes", [])
            if isinstance(item, dict) and str(item.get("name", "")).strip()
        }
        for template_name in template_nodes:
            html_nodes = name_map.get(template_name, [])
            if not html_nodes:
                page_report.contract_mismatches.append(f"templateSizeNodes entry '{template_name}' does not exist in HTML.")
                continue
            if html_nodes[0].attrs.get("data-ui-template-size", "").lower() != "true":
                page_report.contract_mismatches.append(f"Node '{template_name}' is in templateSizeNodes but missing data-ui-template-size=\"true\".")

    def _compare_contract_node(
        self,
        node_spec: Dict[str, object],
        html_node: HtmlNode,
        all_rules: List[CssRule],
        tokens: Dict[str, str],
        page_report: PageValidationReport,
        label: str,
    ) -> None:
        expected_tag = str(node_spec.get("tag", "")).strip().lower()
        raw_role = node_spec.get("role")
        expected_role = str(raw_role).strip() if raw_role is not None else ""
        raw_text = node_spec.get("text")
        expected_text = normalize_whitespace(raw_text) if raw_text is not None else ""
        expected_classes = {str(item).strip() for item in node_spec.get("classes", []) if str(item).strip()}
        expected_style = parse_style_map(node_spec.get("style"))
        resolved_style = self._resolve_styles(html_node, all_rules, tokens)

        if expected_tag and html_node.tag != expected_tag:
            page_report.contract_mismatches.append(f"{label} tag mismatch ({expected_tag} != {html_node.tag}).")
        if expected_role and expected_role.lower() != "none" and html_node.role != expected_role:
            page_report.contract_mismatches.append(f"{label} role mismatch ({expected_role} != {html_node.role}).")
        if expected_classes and not expected_classes.issubset(set(html_node.class_list)):
            page_report.contract_mismatches.append(f"{label} classes mismatch.")
        if expected_text and expected_text != html_node.direct_text:
            page_report.contract_mismatches.append(f"{label} text mismatch.")
        for key, expected_value in expected_style.items():
            if resolved_style.get(key, "") != expected_value:
                page_report.contract_mismatches.append(f"{label} style mismatch for '{key}'.")

    def _validate_self_check(self, page_self_check: Optional[Dict[str, object]], page_report: PageValidationReport) -> None:
        if not page_self_check:
            page_report.add_warning("ui_self_check_report.json does not contain this page.")
            return
        for key in ("unsupportedTags", "unsupportedAttributes", "unsupportedSelectors", "unsupportedProperties", "unsupportedValuePatterns", "missingRequiredSizeNodes", "missingNameNodes", "missingRoleNodes", "undefinedClasses", "undefinedRoles", "browserOnlyPatterns"):
            values = page_self_check.get(key, [])
            if isinstance(values, list) and values:
                page_report.metadata_mismatches.append(f"ui_self_check_report.json page '{page_report.page_id}' has non-empty '{key}'.")

    def _contract_fixed_node_names(self, page_contract: Dict[str, object]) -> Set[str]:
        names: Set[str] = set()
        root = page_contract.get("root")
        if isinstance(root, dict) and str(root.get("sizePolicy", "")).strip() == "fixed":
            names.add(str(root.get("name", "")).strip())
        for node in page_contract.get("namedNodes", []) if isinstance(page_contract.get("namedNodes", []), list) else []:
            if isinstance(node, dict) and str(node.get("sizePolicy", "")).strip() == "fixed":
                names.add(str(node.get("name", "")).strip())
        return {name for name in names if name}

    def _contract_relative_node_names(self, page_contract: Optional[Dict[str, object]]) -> Set[str]:
        if not page_contract:
            return set()
        names: Set[str] = set()
        for item in page_contract.get("relativeSizeNodes", []) if isinstance(page_contract.get("relativeSizeNodes", []), list) else []:
            if isinstance(item, dict):
                name = str(item.get("name", "")).strip()
                if name:
                    names.add(name)
        return names

    def _resolve_rule_block(self, rules: List[CssRule], selector: str) -> Dict[str, str]:
        resolved: Dict[str, str] = {}
        for rule in rules:
            if rule.selector == selector:
                resolved.update(rule.declarations)
        return resolved

    def _flatten_nodes(self, root: HtmlNode) -> List[HtmlNode]:
        result: List[HtmlNode] = []
        stack = [root]
        while stack:
            node = stack.pop()
            result.append(node)
            stack.extend(reversed(node.children))
        return result

    def _extract_link_hrefs(self, nodes: List[HtmlNode], page_html_rel: str) -> Set[str]:
        hrefs: Set[str] = set()
        page_parent = Path(page_html_rel).parent if page_html_rel else Path(".")
        for node in nodes:
            if node.tag == "link" and node.attrs.get("rel", "").lower() == "stylesheet":
                href = node.attrs.get("href", "").strip()
                if href:
                    normalized = posixpath.normpath((page_parent / href).as_posix())
                    hrefs.add(normalized)
        return hrefs


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Validate an AIToUGUI site package and write validation_report.json.")
    parser.add_argument("site_root", type=Path, help="Path to the site package root.")
    parser.add_argument("--output", type=Path, default=None, help="Optional output path for validation_report.json. Defaults to <site_root>/reports/validation_report.json.")
    parser.add_argument("--allowlist", type=Path, default=None, help="Optional path to AIToUGUI_Allowlist参考.json.")
    parser.add_argument("--allow-legacy-metadata", action="store_true", help="Treat missing ui_contract.json / ui_self_check_report.json as warnings.")
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_argument_parser()
    args = parser.parse_args(argv)

    site_root = args.site_root.resolve()
    if not site_root.exists() or not site_root.is_dir():
        parser.error(f"site_root does not exist or is not a directory: {site_root}")

    validator = SitePackageValidator(
        site_root=site_root,
        allowlist_path=args.allowlist.resolve() if args.allowlist else None,
        allow_legacy_metadata=args.allow_legacy_metadata,
    )
    report = validator.validate()

    output_path = args.output.resolve() if args.output else validator.layout.reports_root / "validation_report.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print(
        f"[AIToUGUI Validator] {report['status'].upper()} "
        f"errors={report['summary']['errorCount']} "
        f"warnings={report['summary']['warningCount']} "
        f"report={output_path}"
    )

    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    sys.exit(main())
