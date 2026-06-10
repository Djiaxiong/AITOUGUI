#!/usr/bin/env python3
from __future__ import annotations

import argparse
import shutil
from pathlib import Path
from typing import Iterable, List

from site_package_layout import (
    LEGACY_PREVIEW_FILE_NAME,
    PREVIEW_FILE_NAME,
    REPORTS_DIR_NAME,
    SOURCE_DIR_NAME,
    SNAPSHOTS_DIR_NAME,
    load_manifest,
    resolve_site_package_layout,
    write_preview_entry,
    write_task_report,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Reorganize an AIToUGUI site package into the release-friendly layout.")
    parser.add_argument("site_root", type=Path, help="Path to the site package root.")
    return parser


def move_path_with_meta(source: Path, target: Path) -> None:
    if not source.exists():
        return

    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        return

    shutil.move(str(source), str(target))

    source_meta = Path(f"{source}.meta")
    target_meta = Path(f"{target}.meta")
    if source_meta.exists() and not target_meta.exists():
        target_meta.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(source_meta), str(target_meta))


def remove_path_with_meta(path: Path) -> None:
    if path.is_dir():
        shutil.rmtree(path, ignore_errors=True)
    elif path.exists():
        path.unlink()

    meta_path = Path(f"{path}.meta")
    if meta_path.exists():
        meta_path.unlink()


def ensure_directory_with_meta(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def move_children(source_dir: Path, target_dir: Path, names: Iterable[str]) -> None:
    for name in names:
        move_path_with_meta(source_dir / name, target_dir / name)


def organize_site(site_root: Path) -> List[str]:
    root = site_root.resolve()
    actions: List[str] = []

    legacy_source_items = [
        "site.json",
        "theme.css",
        "ui_contract.json",
        "ui_self_check_report.json",
        "pages",
        "shared",
    ]

    source_root = root / SOURCE_DIR_NAME
    if any((root / item).exists() for item in legacy_source_items):
        ensure_directory_with_meta(source_root)
        for item in legacy_source_items:
            source = root / item
            target = source_root / item
            if source.exists() and not target.exists():
                move_path_with_meta(source, target)
                actions.append(f"move {source.name} -> {SOURCE_DIR_NAME}/{source.name}")

    layout = resolve_site_package_layout(root)
    ensure_directory_with_meta(layout.reports_root)
    ensure_directory_with_meta(layout.snapshots_root)

    legacy_compile_dir = root / "bundle_compile"
    legacy_repair_dir = root / "repair_cycle"

    move_path_with_meta(legacy_compile_dir / "compile_report.json", layout.reports_root / "compile_report.json")
    move_path_with_meta(legacy_compile_dir / "compile_repair_prompt.md", layout.reports_root / "compile_repair_prompt.md")
    move_path_with_meta(legacy_repair_dir / "validation_report.json", layout.reports_root / "validation_report.json")
    move_path_with_meta(legacy_repair_dir / "repair_package", layout.reports_root / "repair_package")
    move_path_with_meta(legacy_repair_dir / "page_validation", layout.reports_root / "page_validation")
    move_path_with_meta(root / "validation_report.json", layout.reports_root / "validation_report.json")
    move_path_with_meta(root / "compile_report.json", layout.reports_root / "compile_report.json")
    move_path_with_meta(root / "compile_repair_prompt.md", layout.reports_root / "compile_repair_prompt.md")

    snapshot_sources = [
        legacy_compile_dir / "_snapshot_export",
        legacy_repair_dir,
    ]
    for snapshot_source in snapshot_sources:
        if not snapshot_source.exists():
            continue
        move_children(snapshot_source, layout.snapshots_root, ("compiled_site.json", "compiled_pages", "layout_snapshots"))

    legacy_bundle_path = legacy_compile_dir / "compiled_site_bundle.json"
    if not layout.bundle_path.exists() and legacy_bundle_path.exists():
        move_path_with_meta(legacy_bundle_path, layout.bundle_path)
        actions.append(f"move {legacy_bundle_path.relative_to(root).as_posix()} -> {layout.bundle_path.name}")
    elif legacy_bundle_path.exists():
        remove_path_with_meta(legacy_bundle_path)
        actions.append(f"remove redundant {legacy_bundle_path.relative_to(root).as_posix()}")

    legacy_preview_path = root / LEGACY_PREVIEW_FILE_NAME
    if legacy_preview_path.exists():
        remove_path_with_meta(legacy_preview_path)
        actions.append(f"remove legacy preview entry {LEGACY_PREVIEW_FILE_NAME}")

    preview_layout = resolve_site_package_layout(root)
    preview_target = root / PREVIEW_FILE_NAME
    preview_target_meta = Path(f"{preview_target}.meta")
    if preview_layout.preview_path != preview_target and preview_layout.preview_path.exists() and not preview_target.exists():
        move_path_with_meta(preview_layout.preview_path, preview_target)
    if not preview_target.exists() and not preview_target_meta.exists():
        preview_target.parent.mkdir(parents=True, exist_ok=True)

    layout = resolve_site_package_layout(root)
    manifest = load_manifest(layout)
    write_preview_entry(layout, manifest)
    write_task_report(layout)

    redundant_bundle_copy = layout.reports_root / "compiled_site_bundle.json"
    if redundant_bundle_copy.exists():
        remove_path_with_meta(redundant_bundle_copy)
        actions.append(f"remove redundant reports/{redundant_bundle_copy.name}")
    if (root / "validation_report.json").exists() and (layout.reports_root / "validation_report.json").exists():
        remove_path_with_meta(root / "validation_report.json")
        actions.append("remove redundant root validation_report.json")
    if (root / "compile_report.json").exists() and (layout.reports_root / "compile_report.json").exists():
        remove_path_with_meta(root / "compile_report.json")
        actions.append("remove redundant root compile_report.json")
    if (root / "compile_repair_prompt.md").exists() and (layout.reports_root / "compile_repair_prompt.md").exists():
        remove_path_with_meta(root / "compile_repair_prompt.md")
        actions.append("remove redundant root compile_repair_prompt.md")

    if legacy_compile_dir.exists():
        remove_path_with_meta(legacy_compile_dir)
        actions.append("remove legacy bundle_compile/")
    if legacy_repair_dir.exists():
        remove_path_with_meta(legacy_repair_dir)
        actions.append("remove legacy repair_cycle/")

    return actions


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    site_root = args.site_root.resolve()
    if not site_root.exists() or not site_root.is_dir():
        parser.error(f"site_root does not exist or is not a directory: {site_root}")

    actions = organize_site(site_root)
    print(f"[AIToUGUI Organize] site={site_root}")
    print(f"[AIToUGUI Organize] preview={site_root / PREVIEW_FILE_NAME}")
    print(f"[AIToUGUI Organize] reports={site_root / REPORTS_DIR_NAME}")
    print(f"[AIToUGUI Organize] source={site_root / SOURCE_DIR_NAME}")
    print(f"[AIToUGUI Organize] snapshots={site_root / SNAPSHOTS_DIR_NAME}")
    for action in actions:
        print(f"[AIToUGUI Organize] {action}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
