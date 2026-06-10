#!/usr/bin/env python3
"""Audit style-axis diversity across multiple AIToUGUI site packages.

Track B of the 2026-04 direction adjustment: the four axes defined in
规范/AIToUGUI_形态语言与风格分叉规范.md (LayoutArchetype / ShapeLanguage /
FrameLanguage / OrnamentLanguage) are inferred by generate_site_contract.py
and stored at source/ui_contract.json -> pages[*].visualLanguage. This script
compares those axes across many sites and flags suspected homogenisation.

Rule: at least 3 of 4 axes must differ between distinct sites (same theme,
different AI or different attempt) for them to count as meaningfully-different
styles. Pairs below the threshold get flagged.
"""
from __future__ import annotations

import argparse
import itertools
import json
import sys
from collections import Counter
from pathlib import Path

AXES = ("layoutArchetype", "shapeLanguage", "frameLanguage", "ornamentLanguage")


def _load_site(site_dir):
    contract_path = site_dir / "source" / "ui_contract.json"
    if not contract_path.exists():
        return None
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None
    pages = contract.get("pages", []) if isinstance(contract, dict) else []
    axes_counts = {axis: Counter() for axis in AXES}
    for page in pages:
        if not isinstance(page, dict):
            continue
        vl = page.get("visualLanguage") or {}
        for axis in AXES:
            val = str(vl.get(axis, "")).strip() or "?"
            axes_counts[axis][val] += 1
    dominant = {
        axis: (counts.most_common(1)[0][0] if counts else "?")
        for axis, counts in axes_counts.items()
    }
    site_field = contract.get("site") if isinstance(contract, dict) else None
    site_id = site_dir.name
    if isinstance(site_field, dict):
        site_id = site_field.get("siteId", site_dir.name)
    return {
        "path": str(site_dir),
        "siteId": site_id,
        "pageCount": len(pages),
        "axes": dominant,
        "axesPerPage": {axis: dict(counts) for axis, counts in axes_counts.items()},
    }


def _discover_sites(paths):
    found = []
    for p in paths:
        if not p.exists():
            continue
        if (p / "source" / "ui_contract.json").exists():
            found.append(p)
            continue
        for child in p.iterdir():
            if not child.is_dir():
                continue
            if (child / "source" / "ui_contract.json").exists():
                found.append(child)
                continue
            for grandchild in child.iterdir():
                if grandchild.is_dir() and (grandchild / "source" / "ui_contract.json").exists():
                    found.append(grandchild)
    seen = set()
    result = []
    for p in found:
        rp = p.resolve()
        if rp in seen:
            continue
        seen.add(rp)
        result.append(p)
    return result


THEME_HINTS = (
    ("xianxia", "xianxia"),
    ("immortal", "xianxia"),
    ("wasteland", "wasteland"),
    ("tactical", "tactical"),
    ("terminal", "tactical"),
    ("squad", "tactical"),
    ("steam", "steam-shipping"),
    ("voyage", "steam-shipping"),
    ("shipping", "steam-shipping"),
    ("coastal", "tower-defense"),
    ("coastline", "tower-defense"),
    ("td_", "tower-defense"),
    ("moba", "moba"),
    ("arcade", "arcade-fighter"),
    ("fighter", "arcade-fighter"),
    ("opera", "grand-opera"),
    ("mmorpg", "grand-opera"),
    ("rpg", "xianxia"),
)


def _infer_theme(site):
    path_str = str(site["path"]).replace(chr(92), "/").lower()
    name = path_str + "/" + str(site["siteId"]).lower()
    for needle, theme in THEME_HINTS:
        if needle in name:
            return theme
    return "unknown"


def _axis_distance(a, b):
    return sum(1 for axis in AXES if a.get(axis, "?") != b.get(axis, "?"))


def _format_axes(axes):
    return " | ".join(axis[:5] + "=" + axes.get(axis, "?") for axis in AXES)


def audit(sites, threshold):
    theme_groups = {}
    for s in sites:
        theme_groups.setdefault(_infer_theme(s), []).append(s)
    flagged = []
    for theme, members in theme_groups.items():
        if len(members) < 2:
            continue
        for a, b in itertools.combinations(members, 2):
            dist = _axis_distance(a["axes"], b["axes"])
            if dist < threshold:
                flagged.append({
                    "theme": theme,
                    "siteA": a["path"],
                    "siteB": b["path"],
                    "axesA": a["axes"],
                    "axesB": b["axes"],
                    "axisDistance": dist,
                    "differingAxes": [axis for axis in AXES if a["axes"].get(axis) != b["axes"].get(axis)],
                })
    flagged.sort(key=lambda e: (e["axisDistance"], e["theme"]))

    global_counters = {axis: Counter() for axis in AXES}
    for s in sites:
        for axis in AXES:
            global_counters[axis][s["axes"].get(axis, "?")] += 1
    dominance = {}
    for axis, counter in global_counters.items():
        total = sum(counter.values()) or 1
        top_value, top_count = counter.most_common(1)[0]
        dominance[axis] = {
            "dominant": top_value,
            "dominantShare": round(top_count / total, 3),
            "distinctValues": len(counter),
            "distribution": dict(counter),
        }

    return {
        "siteCount": len(sites),
        "themes": {t: [m["path"] for m in members] for t, members in theme_groups.items()},
        "axisThreshold": threshold,
        "flaggedPairCount": len(flagged),
        "flaggedPairs": flagged,
        "globalAxisDominance": dominance,
        "sites": [
            {"path": s["path"], "siteId": s["siteId"], "pageCount": s["pageCount"], "axes": s["axes"]}
            for s in sites
        ],
    }


def _print_human(result):
    print()
    print("Audited " + str(result["siteCount"]) + " site(s); axis threshold = " + str(result["axisThreshold"]))
    print()
    print("Global axis dominance:")
    for axis, info in result["globalAxisDominance"].items():
        dist_items = sorted(info["distribution"].items(), key=lambda kv: -kv[1])
        dist = ", ".join(k + ":" + str(v) for k, v in dist_items)
        share = "%5.1f" % (info["dominantShare"] * 100)
        print(
            "  " + axis.ljust(18)
            + " dominant=" + info["dominant"].ljust(14)
            + " share=" + share + "%"
            + " distinct=" + str(info["distinctValues"])
            + "  [" + dist + "]"
        )
    print()
    if result["flaggedPairs"]:
        print("Homogeneity flags: " + str(len(result["flaggedPairs"])) + " pair(s) with axis_distance < " + str(result["axisThreshold"]))
        for entry in result["flaggedPairs"]:
            a = Path(entry["siteA"]).name
            b = Path(entry["siteB"]).name
            print("  [" + entry["theme"].ljust(16) + "] " + a + " <> " + b + "  distance=" + str(entry["axisDistance"]))
            print("       A: " + _format_axes(entry["axesA"]))
            print("       B: " + _format_axes(entry["axesB"]))
    else:
        print("No homogeneity flags.")


def build_parser():
    parser = argparse.ArgumentParser(description="Audit style-axis diversity across AIToUGUI site packages.")
    parser.add_argument("paths", nargs="+")
    parser.add_argument("--threshold", type=int, default=3)
    parser.add_argument("--output", type=Path, default=None)
    return parser


def main(argv=None):
    args = build_parser().parse_args(argv)
    site_dirs = _discover_sites(Path(p).resolve() for p in args.paths)
    sites = [r for r in (_load_site(sd) for sd in site_dirs) if r]
    if not sites:
        print("No site packages with ui_contract.json found under the given paths.", file=sys.stderr)
        return 1
    result = audit(sites, threshold=args.threshold)
    _print_human(result)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print()
        print("audit report written to " + str(args.output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
