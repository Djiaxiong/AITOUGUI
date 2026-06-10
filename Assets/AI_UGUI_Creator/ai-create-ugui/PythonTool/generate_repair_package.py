#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Dict, List, Optional, Tuple


def load_json(path: Path) -> Dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def dedupe(items: List[str]) -> List[str]:
    seen = set()
    result: List[str] = []
    for item in items:
        if not item or item in seen:
            continue
        seen.add(item)
        result.append(item)
    return result


def _compact_text(value: str, *, limit: int = 180) -> str:
    compact = " ".join(value.split())
    if len(compact) <= limit:
        return compact
    return compact[: limit - 3].rstrip() + "..."


def _collect_failing_pages(report: Dict[str, object]) -> List[Dict[str, object]]:
    return [
        page
        for page in report.get("pageReports", [])
        if isinstance(page, dict) and str(page.get("status", "")).strip() == "fail"
    ]


def classify_issue(issue_type: str, message: str, page: Dict[str, object]) -> Tuple[str, List[str]]:
    html_path = str(page.get("html", "")).strip()
    files: List[str] = []
    advice = message

    if issue_type == "contractMismatches":
        files.extend(["ui_contract.json", "site.json"])
        if any(token in message for token in ("prefabName", "targetLayer")):
            advice = f"{message} Align `ui_contract.json` with `site.json` first."
        elif any(token in message for token in ("style mismatch", "tag mismatch", "role mismatch", "text mismatch")):
            files.append(html_path)
            advice = f"{message} Check the page HTML and `ui_contract.json` together."
        else:
            files.append(html_path)
    elif issue_type == "metadataMismatches":
        files.append(html_path)
        if "height should be 1080px" in message or "width should be 1920px" in message:
            advice = f"{message} Fix the page root inline size so it matches the site manifest."
    elif issue_type == "undefinedRoles":
        files.extend([html_path, "theme.css"])
        advice = f"{message} Reuse an existing role if possible; otherwise define the missing role in `theme.css`."
    elif issue_type == "undefinedClasses":
        files.extend([html_path, "shared/widgets.css"])
        advice = f"{message} Reuse an existing class if possible; otherwise define the missing class in `shared/widgets.css`."
    elif issue_type == "missingRequiredAttributes":
        files.append(html_path)
        advice = f"{message} Add the missing required attributes without changing unrelated structure."
    elif issue_type == "missingRequiredSizeNodes":
        files.append(html_path)
        advice = f"{message} Add explicit size information or repair the related contract sizing strategy."
    elif issue_type in {"unsupportedProperties", "unsupportedValuePatterns", "browserOnlyPatterns"}:
        files.append(html_path)
        if "background" in message:
            files.append("theme.css")
        advice = f"{message} Replace it with an allowlist-safe implementation."
    elif issue_type == "unsupportedSelectors":
        files.extend(["theme.css", "shared/widgets.css"])
        advice = f"{message} Replace it with `:root`, `body`, `.ClassName`, or `[data-ui-role=\"...\"]`."
    elif issue_type in {"unsupportedTags", "unsupportedAttributes"}:
        files.append(html_path)
        advice = f"{message} Replace it with allowlist-supported markup."
    elif issue_type == "errors":
        files.append(html_path or "site package")
    else:
        files.append(html_path or "site package")

    return advice, dedupe([item for item in files if item])


def build_fix_tasks(report: Dict[str, object]) -> List[Dict[str, object]]:
    tasks: List[Dict[str, object]] = []
    issue_keys = [
        "errors",
        "unsupportedTags",
        "unsupportedAttributes",
        "unsupportedSelectors",
        "unsupportedProperties",
        "unsupportedValuePatterns",
        "browserOnlyPatterns",
        "missingRequiredAttributes",
        "missingRequiredSizeNodes",
        "undefinedClasses",
        "undefinedRoles",
        "contractMismatches",
        "metadataMismatches",
    ]

    for page in report.get("pageReports", []):
        if not isinstance(page, dict):
            continue
        page_id = str(page.get("pageId", "")).strip()
        html_path = str(page.get("html", "")).strip()
        for issue_key in issue_keys:
            values = page.get(issue_key, [])
            if not isinstance(values, list):
                continue
            for message in values:
                advice, files = classify_issue(issue_key, str(message), page)
                tasks.append(
                    {
                        "pageId": page_id,
                        "html": html_path,
                        "issueType": issue_key,
                        "message": str(message),
                        "advice": advice,
                        "files": files,
                    }
                )

    return condense_tasks(tasks)


def condense_tasks(tasks: List[Dict[str, object]]) -> List[Dict[str, object]]:
    grouped: Dict[Tuple[str, str, str], Dict[str, object]] = {}
    role_pattern = re.compile(r"undefined role '([^']+)'")
    class_pattern = re.compile(r"undefined class '([^']+)'")

    for task in tasks:
        page_id = str(task["pageId"])
        issue_type = str(task["issueType"])
        message = str(task["message"])
        group_key = message

        role_match = role_pattern.search(message)
        class_match = class_pattern.search(message)
        if issue_type == "undefinedRoles" and role_match:
            group_key = f"undefined role::{role_match.group(1)}"
        elif issue_type == "undefinedClasses" and class_match:
            group_key = f"undefined class::{class_match.group(1)}"

        key = (page_id, issue_type, group_key)
        bucket = grouped.get(key)
        if bucket is None:
            grouped[key] = {
                "pageId": page_id,
                "html": task["html"],
                "issueType": issue_type,
                "message": message,
                "advice": task["advice"],
                "files": list(task["files"]),
                "count": 1,
                "items": [message],
            }
            continue

        bucket["count"] = int(bucket["count"]) + 1
        bucket["items"].append(message)
        bucket["files"] = dedupe(list(bucket["files"]) + list(task["files"]))

    result: List[Dict[str, object]] = []
    for item in grouped.values():
        items = dedupe([str(entry) for entry in item["items"]])
        count = int(item["count"])
        if count > 1 and items:
            item["message"] = f"{items[0]} (x{count})"
        item["items"] = items
        result.append(item)

    result.sort(key=lambda value: (str(value["pageId"]), str(value["issueType"]), str(value["message"])))
    return result


def _issue_type_counts(tasks: List[Dict[str, object]]) -> List[Tuple[str, int]]:
    counts: Dict[str, int] = {}
    for task in tasks:
        issue_type = str(task.get("issueType", "")).strip() or "unknown"
        counts[issue_type] = counts.get(issue_type, 0) + int(task.get("count", 1))
    return sorted(counts.items(), key=lambda item: (-item[1], item[0]))


def _priority_tasks(tasks: List[Dict[str, object]]) -> List[Dict[str, object]]:
    return sorted(
        tasks,
        key=lambda item: (
            -int(item.get("count", 1)),
            str(item.get("pageId", "")),
            str(item.get("message", "")),
        ),
    )


def build_repair_summary(
    site_root: Path,
    report: Dict[str, object],
    tasks: List[Dict[str, object]],
    snapshot_artifacts: Optional[Dict[str, str]] = None,
    *,
    max_tasks: int = 24,
) -> str:
    failing_pages = _collect_failing_pages(report)
    issue_type_counts = _issue_type_counts(tasks)
    prioritized_tasks = _priority_tasks(tasks)
    visible_tasks = prioritized_tasks[:max_tasks]
    omitted_task_count = max(0, len(prioritized_tasks) - len(visible_tasks))

    lines: List[str] = [
        "# AIToUGUI Repair Summary",
        "",
        "Use this summary first.",
        "Avoid reading raw `validation_report.json` unless this summary is still insufficient.",
        "",
        "## Status",
        "",
        f"- siteRoot: `{site_root}`",
        f"- status: `{report.get('status', 'unknown')}`",
        f"- errorCount: `{report.get('summary', {}).get('errorCount', 0)}`",
        f"- warningCount: `{report.get('summary', {}).get('warningCount', 0)}`",
        f"- failingPages: `{len(failing_pages)}`",
        "",
    ]

    if failing_pages:
        lines.extend(["## Failing Pages", ""])
        for page in failing_pages[:10]:
            page_id = str(page.get("pageId", "")).strip() or "<unknown-page>"
            html = str(page.get("html", "")).strip() or "<unknown-file>"
            error_count = int(page.get("errorCount", 0))
            warning_count = int(page.get("warningCount", 0))
            lines.append(f"- `{page_id}` -> `{html}` ({error_count} errors, {warning_count} warnings)")
        if len(failing_pages) > 10:
            lines.append(f"- ... {len(failing_pages) - 10} more failing pages omitted")
        lines.append("")

    if issue_type_counts:
        lines.extend(["## Issue Buckets", ""])
        for issue_type, count in issue_type_counts[:8]:
            lines.append(f"- `{issue_type}`: {count}")
        if len(issue_type_counts) > 8:
            lines.append(f"- ... {len(issue_type_counts) - 8} more issue buckets omitted")
        lines.append("")

    if snapshot_artifacts:
        lines.extend(["## Snapshot Artifacts", ""])
        compiled_site = snapshot_artifacts.get("compiledSite", "")
        compiled_pages_dir = snapshot_artifacts.get("compiledPagesDir", "")
        layout_snapshots_dir = snapshot_artifacts.get("layoutSnapshotsDir", "")
        if compiled_site:
            lines.append(f"- `compiled_site.json`: `{compiled_site}`")
        if compiled_pages_dir:
            lines.append(f"- `compiled_pages/`: `{compiled_pages_dir}`")
        if layout_snapshots_dir:
            lines.append(f"- `layout_snapshots/`: `{layout_snapshots_dir}`")
        lines.append("")

    lines.extend(["## Priority Repair Tasks", ""])
    if visible_tasks:
        for index, task in enumerate(visible_tasks, start=1):
            page_id = str(task.get("pageId", "")).strip() or "<unknown-page>"
            advice = _compact_text(str(task.get("advice", "")).strip() or str(task.get("message", "")).strip())
            count = int(task.get("count", 1))
            files = ", ".join(f"`{item}`" for item in task.get("files", []) if isinstance(item, str))
            count_suffix = f" (x{count})" if count > 1 else ""
            lines.append(f"{index}. [`{page_id}`] {advice}{count_suffix}")
            if files:
                lines.append(f"   Files: {files}")
        if omitted_task_count:
            lines.append(f"- ... {omitted_task_count} more condensed tasks omitted")
    else:
        lines.append("- No repair tasks were generated.")

    lines.extend(
        [
            "",
            "## Workflow",
            "",
            "- Start from the tasks above and the matching HTML/CSS/contract files.",
            "- Use `compiled_pages/` for structure issues and `layout_snapshots/` for footprint or overflow issues.",
            "- Regenerate contract/self-check if metadata drifted.",
            "- Rerun `validate_and_prepare_repair.py` after the fixes.",
        ]
    )
    return "\n".join(lines)


def build_repair_console_digest(
    site_root: Path,
    report: Dict[str, object],
    tasks: List[Dict[str, object]],
    *,
    max_tasks: int = 8,
) -> str:
    failing_pages = _collect_failing_pages(report)
    issue_type_counts = _issue_type_counts(tasks)
    prioritized_tasks = _priority_tasks(tasks)[:max_tasks]

    lines: List[str] = [
        "AIToUGUI repair digest",
        f"- siteRoot: {site_root}",
        (
            f"- status: {report.get('status', 'unknown')}, "
            f"errors: {report.get('summary', {}).get('errorCount', 0)}, "
            f"warnings: {report.get('summary', {}).get('warningCount', 0)}, "
            f"failingPages: {len(failing_pages)}"
        ),
        "- Use `repair_summary.md` first. Avoid reading raw `validation_report.json` first.",
        "- Failing pages:",
    ]

    if failing_pages:
        for page in failing_pages[:6]:
            page_id = str(page.get("pageId", "")).strip() or "<unknown-page>"
            html = str(page.get("html", "")).strip() or "<unknown-file>"
            error_count = int(page.get("errorCount", 0))
            warning_count = int(page.get("warningCount", 0))
            lines.append(f"  - {page_id}: {error_count} errors, {warning_count} warnings -> {html}")
    else:
        lines.append("  - none")

    lines.append("- Top issue buckets:")
    if issue_type_counts:
        for issue_type, count in issue_type_counts[:5]:
            lines.append(f"  - {issue_type}: {count}")
    else:
        lines.append("  - none")

    lines.append("- Priority repair tasks:")
    if prioritized_tasks:
        for index, task in enumerate(prioritized_tasks, start=1):
            page_id = str(task.get("pageId", "")).strip() or "<unknown-page>"
            advice = _compact_text(str(task.get("advice", "")).strip() or str(task.get("message", "")).strip(), limit=150)
            count = int(task.get("count", 1))
            count_suffix = f" (x{count})" if count > 1 else ""
            lines.append(f"  {index}. [{page_id}] {advice}{count_suffix}")
    else:
        lines.append("  - none")

    lines.extend(
        [
            "- Workflow:",
            "  - Use `compiled_pages/` for structure issues.",
            "  - Use `layout_snapshots/` for footprint or overflow issues.",
            "  - Rerun `validate_and_prepare_repair.py` after edits.",
        ]
    )
    return "\n".join(lines)


def build_repair_prompt(
    site_root: Path,
    report: Dict[str, object],
    tasks: List[Dict[str, object]],
    snapshot_artifacts: Optional[Dict[str, str]] = None,
) -> str:
    failing_pages = _collect_failing_pages(report)
    lines: List[str] = [
        "# AIToUGUI Repair Prompt",
        "",
        "This prompt already contains a condensed repair plan.",
        "Use it first and avoid reading raw `validation_report.json` unless you still need missing detail.",
        "",
        "## Goal",
        "",
        "- Fix only the issues captured below.",
        "- Do not rewrite the whole site package.",
        "- Do not rename stable `data-ui-name` values without a direct validator reason.",
        "- The package must pass `validate_site_package.py` again when you finish.",
        "",
        "## Site Root",
        "",
        f"`{site_root}`",
        "",
        "## Current Status",
        "",
        f"- status: `{report.get('status', 'unknown')}`",
        f"- errorCount: `{report.get('summary', {}).get('errorCount', 0)}`",
        f"- warningCount: `{report.get('summary', {}).get('warningCount', 0)}`",
        f"- failingPages: `{len(failing_pages)}`",
        "",
    ]

    if snapshot_artifacts:
        lines.extend(["## Snapshot Artifacts", ""])
        compiled_site = snapshot_artifacts.get("compiledSite", "")
        compiled_pages_dir = snapshot_artifacts.get("compiledPagesDir", "")
        layout_snapshots_dir = snapshot_artifacts.get("layoutSnapshotsDir", "")
        if compiled_site:
            lines.append(f"- `compiled_site.json`: `{compiled_site}`")
        if compiled_pages_dir:
            lines.append(f"- `compiled_pages/`: `{compiled_pages_dir}`")
        if layout_snapshots_dir:
            lines.append(f"- `layout_snapshots/`: `{layout_snapshots_dir}`")
        lines.append("- Check `compiled_pages/` first for structure mismatches.")
        lines.append("- Check `layout_snapshots/` first for overflow, footprint, size, or gap issues.")
        lines.append("")

    lines.extend(["## Failing Pages", ""])
    if failing_pages:
        for page in failing_pages:
            lines.append(f"- `{page.get('pageId', '')}` -> `{page.get('html', '')}`")
    else:
        lines.append("- No failing pages were reported.")
    lines.append("")

    lines.extend(["## Repair Tasks", ""])
    if tasks:
        for index, task in enumerate(tasks, start=1):
            page_id = str(task.get("pageId", "")).strip() or "<unknown-page>"
            advice = str(task.get("advice", "")).strip() or str(task.get("message", "")).strip()
            files = ", ".join(f"`{item}`" for item in task.get("files", []) if isinstance(item, str)) or "`<unknown>`"
            count = int(task.get("count", 1))
            count_suffix = f" (x{count})" if count > 1 else ""
            lines.append(f"{index}. [`{page_id}`] {advice}{count_suffix}")
            lines.append(f"   Files: {files}")
    else:
        lines.append("- No repair tasks were generated.")
    lines.extend(
        [
            "",
            "## Constraints",
            "",
            "- Reuse existing roles, classes, and element families when possible.",
            "- For contract mismatches, treat `site.json` as the page manifest source of truth, then repair `ui_contract.json`.",
            "- For undefined roles or classes, prefer mapping back to the existing system before adding new definitions.",
            "- For root size problems, fix the page root directly instead of compensating with parent wrappers.",
            "- Rerun validation after the edits instead of relying on visual inspection alone.",
            "",
            "## Output",
            "",
            "- List the files you changed.",
            "- Explain how each repair maps back to the blocking issues.",
            "- Summarize the rerun validation result.",
        ]
    )
    return "\n".join(lines)


def build_repair_package(report_path: Path, output_root: Optional[Path]) -> Dict[str, Path]:
    report = load_json(report_path)
    site_root = Path(str(report.get("siteRoot", "")).strip())
    tasks = build_fix_tasks(report)
    cycle_root = report_path.parent

    output_dir = output_root or report_path.parent / "repair_package"
    output_dir.mkdir(parents=True, exist_ok=True)

    snapshot_artifacts = {
        "compiledSite": str((cycle_root / "compiled_site.json").resolve()) if (cycle_root / "compiled_site.json").exists() else "",
        "compiledPagesDir": str((cycle_root / "compiled_pages").resolve()) if (cycle_root / "compiled_pages").exists() else "",
        "layoutSnapshotsDir": str((cycle_root / "layout_snapshots").resolve()) if (cycle_root / "layout_snapshots").exists() else "",
    }

    repair_json_path = output_dir / "repair_instructions.json"
    repair_md_path = output_dir / "repair_prompt.md"
    repair_summary_path = output_dir / "repair_summary.md"

    repair_json = {
        "reportVersion": report.get("reportVersion", "1.0"),
        "siteId": report.get("siteId", ""),
        "siteRoot": str(site_root),
        "status": report.get("status", "unknown"),
        "summary": report.get("summary", {}),
        "snapshotArtifacts": snapshot_artifacts,
        "repairSummary": str(repair_summary_path.resolve()),
        "failingPages": [
            {
                "pageId": page.get("pageId", ""),
                "html": page.get("html", ""),
                "errorCount": page.get("errorCount", 0),
                "warningCount": page.get("warningCount", 0),
            }
            for page in _collect_failing_pages(report)
        ],
        "tasks": tasks,
    }

    repair_json_path.write_text(json.dumps(repair_json, ensure_ascii=False, indent=2), encoding="utf-8")
    repair_md_path.write_text(build_repair_prompt(site_root, report, tasks, snapshot_artifacts), encoding="utf-8")
    repair_summary_path.write_text(build_repair_summary(site_root, report, tasks, snapshot_artifacts), encoding="utf-8")

    return {
        "repair_json": repair_json_path,
        "repair_md": repair_md_path,
        "repair_summary": repair_summary_path,
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate AI repair instructions from validation_report.json.")
    parser.add_argument("validation_report", type=Path, help="Path to validation_report.json.")
    parser.add_argument("--output-dir", type=Path, default=None, help="Optional output directory for repair package.")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    report_path = args.validation_report.resolve()
    if not report_path.exists():
        parser.error(f"validation_report does not exist: {report_path}")

    outputs = build_repair_package(report_path, args.output_dir.resolve() if args.output_dir else None)
    print(
        f"[AIToUGUI Repair] json={outputs['repair_json']} "
        f"md={outputs['repair_md']} summary={outputs['repair_summary']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
