#!/usr/bin/env python3
from __future__ import annotations

import json
import posixpath
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List


SOURCE_DIR_NAME = "source"
REPORTS_DIR_NAME = "reports"
SNAPSHOTS_DIR_NAME = "snapshots"
PREVIEW_FILE_NAME = "preview.html"
LEGACY_PREVIEW_FILE_NAME = "preview-index.html"
BUNDLE_FILE_NAME = "compiled_site_bundle.json"
TASK_REPORT_FILE_NAME = "任务报告.md"


@dataclass(frozen=True)
class SitePackageLayout:
    site_root: Path
    source_root: Path
    reports_root: Path
    snapshots_root: Path
    preview_path: Path
    bundle_path: Path
    task_report_path: Path

    @property
    def uses_nested_source(self) -> bool:
        return self.source_root != self.site_root

    def resolve_source_path(self, relative_path: str) -> Path:
        return (self.source_root / relative_path).resolve()

    def to_site_relative_posix(self, path: Path) -> str:
        return path.resolve().relative_to(self.site_root.resolve()).as_posix()

    def authored_path_for_preview(self, relative_path: str) -> str:
        normalized = posixpath.normpath(relative_path.strip()) if relative_path else ""
        if not normalized:
            return normalized
        if self.uses_nested_source:
            return posixpath.normpath(f"{SOURCE_DIR_NAME}/{normalized}")
        return normalized


def resolve_site_package_layout(site_root: Path) -> SitePackageLayout:
    root = site_root.resolve()
    nested_source_root = root / SOURCE_DIR_NAME
    source_root = nested_source_root if (nested_source_root / "site.json").exists() else root

    preview_path = root / PREVIEW_FILE_NAME
    if not preview_path.exists():
        legacy_preview_path = root / LEGACY_PREVIEW_FILE_NAME
        if legacy_preview_path.exists():
            preview_path = legacy_preview_path

    return SitePackageLayout(
        site_root=root,
        source_root=source_root,
        reports_root=root / REPORTS_DIR_NAME,
        snapshots_root=root / SNAPSHOTS_DIR_NAME,
        preview_path=preview_path,
        bundle_path=root / BUNDLE_FILE_NAME,
        task_report_path=root / TASK_REPORT_FILE_NAME,
    )


def load_manifest(layout: SitePackageLayout) -> Dict[str, object]:
    return json.loads((layout.source_root / "site.json").read_text(encoding="utf-8"))


def build_preview_html(layout: SitePackageLayout, manifest: Dict[str, object]) -> str:
    site_title = str(manifest.get("displayName", "")).strip() or str(manifest.get("siteId", "")).strip() or "AIToUGUI Site"
    links: List[str] = []
    for page in manifest.get("pages", []):
        if not isinstance(page, dict):
            continue
        page_name = str(page.get("displayName", "")).strip() or str(page.get("pageId", "")).strip() or "Page"
        html_rel = str(page.get("html", "")).strip()
        if not html_rel:
            continue
        href = layout.authored_path_for_preview(html_rel)
        links.append(f'    <a class="preview-link" href="{href}">{page_name}</a>')

    links_markup = "\n".join(links) if links else '    <span class="preview-empty">No pages declared in site.json</span>'
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <title>{site_title} Preview</title>
  <style>
    :root {{
      color-scheme: dark;
    }}
    * {{
      box-sizing: border-box;
    }}
    body {{
      margin: 0;
      min-height: 100vh;
      padding: 40px;
      font-family: "Segoe UI", "Microsoft YaHei UI", sans-serif;
      background:
        radial-gradient(circle at top, rgba(110, 168, 254, 0.16), transparent 38%),
        linear-gradient(180deg, #0d1117 0%, #06080d 100%);
      color: #f3f6fb;
    }}
    .shell {{
      max-width: 980px;
      margin: 0 auto;
      padding: 28px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 20px;
      background: rgba(13, 17, 23, 0.82);
      box-shadow: 0 22px 70px rgba(0, 0, 0, 0.35);
    }}
    h1 {{
      margin: 0 0 10px;
      font-size: 32px;
      line-height: 1.2;
    }}
    .meta {{
      margin: 0 0 24px;
      color: rgba(243, 246, 251, 0.72);
      font-size: 14px;
    }}
    .preview-list {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: 14px;
    }}
    .preview-link {{
      display: block;
      padding: 16px 18px;
      border-radius: 14px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: rgba(255, 255, 255, 0.04);
      color: inherit;
      text-decoration: none;
      transition: transform 120ms ease, border-color 120ms ease, background 120ms ease;
    }}
    .preview-link:hover {{
      transform: translateY(-1px);
      border-color: rgba(110, 168, 254, 0.65);
      background: rgba(110, 168, 254, 0.12);
    }}
    .preview-empty {{
      color: rgba(243, 246, 251, 0.52);
      font-size: 14px;
    }}
  </style>
</head>
<body>
  <div class="shell">
    <h1>{site_title}</h1>
    <p class="meta">Open a page below to inspect the authored HTML preview.</p>
    <div class="preview-list">
{links_markup}
    </div>
  </div>
</body>
</html>
"""


def write_preview_entry(layout: SitePackageLayout, manifest: Dict[str, object]) -> Path:
    layout.preview_path.write_text(build_preview_html(layout, manifest), encoding="utf-8")
    return layout.preview_path


def write_task_report(layout: SitePackageLayout) -> Path:
    manifest = load_manifest(layout)
    site_id = str(manifest.get("siteId", "")).strip() or "unknown_site"
    display_name = str(manifest.get("displayName", "")).strip() or site_id
    page_count = len([page for page in manifest.get("pages", []) if isinstance(page, dict)])

    compile_status = "missing"
    compile_report_path = layout.reports_root / "compile_report.json"
    if compile_report_path.exists():
        compile_status = str(json.loads(compile_report_path.read_text(encoding="utf-8")).get("status", "unknown"))

    validation_status = "missing"
    validation_report_path = layout.reports_root / "validation_report.json"
    if validation_report_path.exists():
        validation_status = str(json.loads(validation_report_path.read_text(encoding="utf-8")).get("status", "unknown"))

    lines = [
        f"# {display_name} 任务报告",
        "",
        "## 站点概览",
        "",
        f"- siteId: `{site_id}`",
        f"- displayName: `{display_name}`",
        f"- designResolution: `{manifest.get('designWidth', 1920)} x {manifest.get('designHeight', 1080)}`",
        f"- pageCount: `{page_count}`",
        "",
        "## 正式交付入口",
        "",
        f"- HTML 预览入口: `{layout.to_site_relative_posix(layout.preview_path)}`",
        f"- Unity 导入 JSON: `{layout.to_site_relative_posix(layout.bundle_path)}`",
        "",
        "## 目录说明",
        "",
        "- `source/`: HTML/CSS 源包、site.json、contract、自检文件",
        "- `reports/`: validate / compile / repair 输出",
        "- `snapshots/`: compiled page/layout 快照",
        "",
        "## 当前状态",
        "",
        f"- validation: `{validation_status}`",
        f"- compile: `{compile_status}`",
        "",
        "## Unity 使用",
        "",
        "1. 在 Unity 中选择根目录下的 `compiled_site_bundle.json`。",
        "2. 使用 AIToUGUI 导入工具生成预览与配置资产。",
        "3. 确认页面还原后导出 Prefab，并按需挂接代码绑定。",
        "",
    ]

    layout.task_report_path.write_text("\n".join(lines), encoding="utf-8")
    return layout.task_report_path
