#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Dict, Iterable, List

from site_package_layout import resolve_site_package_layout
from validate_site_package import SitePackageValidator


MAX_CONSOLE_ITEMS = 20


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Validate one AIToUGUI page plus shared package dependencies."
    )
    parser.add_argument("site_root", type=Path, help="Path to the site package root.")
    parser.add_argument("page_id", help="The pageId to validate.")
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Optional output path. Defaults to <site_root>/reports/page_validation/<page_id>.validation_report.json.",
    )
    parser.add_argument(
        "--allowlist",
        type=Path,
        default=None,
        help="Optional path to the AIToUGUI allowlist JSON.",
    )
    parser.add_argument(
        "--require-preview",
        action="store_true",
        help="Also require the root preview entry to exist for this validation pass.",
    )
    parser.add_argument(
        "--strict-metadata",
        dest="allow_legacy_metadata",
        action="store_false",
        help="Treat missing ui_contract.json / ui_self_check_report.json as blocking errors.",
    )
    parser.set_defaults(allow_legacy_metadata=True)
    return parser


def iter_report_items(report: Dict[str, object]) -> Iterable[str]:
    for value in report.get("violations", []):
        yield f"shared error: {value}"

    page_report = report.get("pageReport", {})
    if not isinstance(page_report, dict):
        return

    section_order = (
        ("errors", "error"),
        ("unsupportedTags", "unsupported tag"),
        ("unsupportedAttributes", "unsupported attribute"),
        ("unsupportedSelectors", "unsupported selector"),
        ("unsupportedProperties", "unsupported property"),
        ("unsupportedValuePatterns", "unsupported value"),
        ("browserOnlyPatterns", "browser-only"),
        ("missingRequiredAttributes", "missing attribute"),
        ("missingRequiredSizeNodes", "missing size"),
        ("contractMismatches", "contract mismatch"),
        ("metadataMismatches", "metadata mismatch"),
        ("warnings", "warning"),
        ("undefinedClasses", "undefined class"),
        ("undefinedRoles", "undefined role"),
    )
    for key, label in section_order:
        for value in page_report.get(key, []):
            yield f"{label}: {value}"

    for value in report.get("warnings", []):
        yield f"shared warning: {value}"


def build_actions(report: Dict[str, object]) -> List[str]:
    if report.get("status") == "pass":
        return []

    corpus = "\n".join(iter_report_items(report)).lower()
    actions: List[str] = []

    if any(
        token in corpus
        for token in (
            "negative left offset",
            "negative top offset",
            "outside the ",
            "exceeds page root",
            "exceeds ",
        )
    ):
        actions.append(
            "Move or resize the offending module so its final box stays inside the locked design-resolution page and inside its parent frame. Do not hide overflow."
        )
    if "flex footprint" in corpus:
        actions.append(
            "Reduce child count, width, padding, or gap in the failing flex container, or enlarge the container budget before handing the page back."
        )
    if any(
        token in corpus
        for token in (
            "side-by-side siblings",
            "fixed row wrapper",
            "stack vertically",
        )
    ):
        actions.append(
            "Wrap intended parallel regions in an explicit fixed-size layout container such as display:flex row/column, or place them with absolute coordinates. Do not rely on default block flow for side-by-side modules."
        )
    if "box-sizing:border-box" in corpus:
        actions.append(
            "Add box-sizing:border-box to any fixed-size node that also uses padding or border so the rendered footprint matches the layout budget."
        )
    if any(
        token in corpus
        for token in (
            "browser-only",
            "unsupported background",
            "unsupported css property",
            "unsupported unit pattern",
        )
    ):
        actions.append(
            "Replace browser-only CSS with the allowed Unity-safe subset: pixel sizes, approved selectors, simple backgrounds, and supported primitives only."
        )
    if any(
        token in corpus
        for token in (
            "undefined class",
            "undefined role",
            "contract mismatch",
            "metadata mismatch",
        )
    ):
        actions.append(
            "Align the page with Team Lead semantics, shared classes, and ui_contract.json before submitting the page."
        )

    if not actions:
        actions.append("Fix the first blocking item in this page HTML and rerun the page validator until it passes.")

    return actions


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    site_root = args.site_root.resolve()
    if not site_root.exists() or not site_root.is_dir():
        parser.error(f"site_root does not exist or is not a directory: {site_root}")

    layout = resolve_site_package_layout(site_root)
    output_path = args.output.resolve() if args.output else layout.reports_root / "page_validation" / f"{args.page_id}.validation_report.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)

    validator = SitePackageValidator(
        site_root=site_root,
        allowlist_path=args.allowlist.resolve() if args.allowlist else None,
        allow_legacy_metadata=args.allow_legacy_metadata,
    )
    report = validator.validate_page(args.page_id, require_preview=args.require_preview)
    actions = build_actions(report)

    output_payload = dict(report)
    output_payload["actions"] = actions
    output_path.write_text(json.dumps(output_payload, ensure_ascii=False, indent=2), encoding="utf-8")

    page_report = report.get("pageReport", {})
    page_html = page_report.get("html", "") if isinstance(page_report, dict) else ""
    print(
        f"[AIToUGUI Page Validator] {str(report.get('status', 'fail')).upper()} "
        f"page={args.page_id} "
        f"errors={report.get('summary', {}).get('errorCount', 0)} "
        f"warnings={report.get('summary', {}).get('warningCount', 0)} "
        f"report={output_path}"
    )
    if page_html:
        print(f"[AIToUGUI Page Validator] html={page_html}")

    for index, line in enumerate(iter_report_items(report)):
        if index >= MAX_CONSOLE_ITEMS:
            print("[AIToUGUI Page Validator] ... more items omitted, see JSON report for full details.")
            break
        print(f"[AIToUGUI Page Validator] {line}")

    for action in actions:
        print(f"[AIToUGUI Page Validator] action: {action}")

    return 0 if report.get("status") == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
