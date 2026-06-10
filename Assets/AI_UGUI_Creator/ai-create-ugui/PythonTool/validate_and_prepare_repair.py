#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path

from export_site_snapshots import export_site_snapshots
from generate_repair_package import build_repair_console_digest, build_repair_package, load_json
from site_package_layout import resolve_site_package_layout, write_task_report
from validate_site_package import SitePackageValidator


MAX_REPAIR_DIGEST_LINES = 24


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run AIToUGUI validation and generate a repair package when validation fails."
    )
    parser.add_argument("site_root", type=Path, help="Path to the site package root.")
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
    output_dir = args.output_dir.resolve() if args.output_dir else layout.reports_root
    output_dir.mkdir(parents=True, exist_ok=True)

    validator = SitePackageValidator(
        site_root=site_root,
        allowlist_path=args.allowlist.resolve() if args.allowlist else None,
        allow_legacy_metadata=args.allow_legacy_metadata,
    )
    report = validator.validate()

    report_path = output_dir / "validation_report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print(
        f"[AIToUGUI Cycle] validate status={report['status']} "
        f"errors={report['summary']['errorCount']} "
        f"warnings={report['summary']['warningCount']} "
        f"report={report_path}"
    )

    snapshot_outputs = export_site_snapshots(
        site_root=site_root,
        output_root=layout.snapshots_root,
        allowlist_path=args.allowlist.resolve() if args.allowlist else None,
        allow_legacy_metadata=args.allow_legacy_metadata,
    )
    print(f"[AIToUGUI Cycle] compiled_site={snapshot_outputs['compiled_site']}")
    print(f"[AIToUGUI Cycle] compiled_pages={snapshot_outputs['compiled_pages_dir']}")
    print(f"[AIToUGUI Cycle] layout_snapshots={snapshot_outputs['layout_snapshots_dir']}")

    if report["status"] != "pass":
        outputs = build_repair_package(report_path, output_dir / "repair_package")
        print(f"[AIToUGUI Cycle] repair json={outputs['repair_json']}")
        print(f"[AIToUGUI Cycle] repair md={outputs['repair_md']}")
        print(f"[AIToUGUI Cycle] repair summary={outputs['repair_summary']}")

        digest_text = build_repair_console_digest(site_root, report, load_json(outputs["repair_json"]).get("tasks", []))  # type: ignore[arg-type]
        print("[AIToUGUI Cycle] repair digest begin")
        for line in digest_text.splitlines()[:MAX_REPAIR_DIGEST_LINES]:
            print(f"[AIToUGUI Cycle] {line}")
        print("[AIToUGUI Cycle] repair digest end")
        write_task_report(layout)
        return 1

    print("[AIToUGUI Cycle] validation passed, no repair package generated.")
    write_task_report(layout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
