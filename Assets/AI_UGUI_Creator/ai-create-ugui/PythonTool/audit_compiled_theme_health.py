#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Dict, List, Tuple

from site_package_layout import resolve_site_package_layout


DEFAULT_THEME_SENTINELS = {
    "#101010",
    "#1e1e1e",
    "rgba(255,255,255,0.12)",
    "rgba(0,0,0,0.25)",
}

CORE_THEME_FIELDS = ("pageBackground", "cardFill")
SECONDARY_THEME_FIELDS = ("panelFill", "buttonFill", "accentColor")
CANONICAL_TEXT_ROLE_HINTS = {
    "text/title",
    "text/body",
    "text/label",
    "text/value",
    "text/gold",
}

CANONICAL_ROLE_HINTS = {
    "window/main",
    "panel/hero",
    "panel/section",
    "panel/resource-bar",
    "card/info",
    "card/entry",
    "card/slot",
    "card/accent",
    "button/primary",
    "button/secondary",
    "button/gold",
    "button/ghost",
    "text/title",
    "text/body",
    "text/label",
    "text/value",
    "text/gold",
}


def load_json(path: Path) -> Dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def is_empty_visual_preset(preset: Dict[str, object]) -> bool:
    return (
        not bool(preset.get("enableFill"))
        and not bool(preset.get("useGradient"))
        and float(preset.get("outlineWidth", 0) or 0) == 0.0
        and float(preset.get("shadowBlur", 0) or 0) == 0.0
        and not bool(preset.get("enableGlow"))
    )


def field_state(theme: Dict[str, object], field: str) -> str:
    value = str(theme.get(field, "")).strip()
    if not value:
        return "empty"
    if value in DEFAULT_THEME_SENTINELS:
        return "default"
    return "custom"


def audit_theme(theme: Dict[str, object]) -> Tuple[List[str], List[str], Dict[str, int]]:
    warnings: List[str] = []
    infos: List[str] = []
    metrics: Dict[str, int] = {
        "coreCustomCount": 0,
        "secondaryDefaultCount": 0,
        "emptyPresetCount": 0,
        "activePresetCount": 0,
        "tokenCount": 0,
    }

    for field in CORE_THEME_FIELDS + SECONDARY_THEME_FIELDS:
        value = str(theme.get(field, "")).strip()
        state = field_state(theme, field)
        if state == "empty":
            warnings.append(f"theme.{field} is empty.")
        elif state == "default":
            if field in CORE_THEME_FIELDS:
                warnings.append(f"theme.{field} uses a compile default sentinel value: {value}")
            else:
                metrics["secondaryDefaultCount"] += 1
        else:
            if field in CORE_THEME_FIELDS:
                metrics["coreCustomCount"] += 1
            infos.append(f"theme.{field} = {value}")

    presets = theme.get("visualPresets", [])
    if isinstance(presets, list) and presets:
        empty_presets = 0
        for preset in presets:
            if isinstance(preset, dict) and is_empty_visual_preset(preset):
                empty_presets += 1
        active_presets = len(presets) - empty_presets
        metrics["emptyPresetCount"] = empty_presets
        metrics["activePresetCount"] = active_presets
        if empty_presets == len(presets):
            warnings.append("All theme.visualPresets are effectively empty; theme semantics may not have compiled correctly.")
    else:
        warnings.append("theme.visualPresets is missing or empty.")

    tokens = theme.get("tokens", [])
    token_count = len(tokens) if isinstance(tokens, list) else 0
    metrics["tokenCount"] = token_count
    if token_count == 0:
        warnings.append("theme.tokens is empty.")
    else:
        infos.append(f"theme.tokens count = {token_count}")

    return warnings, infos, metrics


def audit_contract_semantics(site_root: Path) -> Tuple[List[str], List[str], Dict[str, int]]:
    warnings: List[str] = []
    infos: List[str] = []
    metrics: Dict[str, int] = {
        "canonicalRoleCount": 0,
        "textRoleCount": 0,
        "hasWindowMain": 0,
    }

    contract_path = site_root / "source" / "ui_contract.json"
    if not contract_path.exists():
        warnings.append("source/ui_contract.json is missing; cannot audit semantic coverage.")
        return warnings, infos, metrics

    contract = load_json(contract_path)
    used_roles = contract.get("usedRoles", [])
    if not isinstance(used_roles, list):
        warnings.append("ui_contract.usedRoles is missing or malformed.")
        return warnings, infos, metrics

    used_role_set = {str(role).strip() for role in used_roles if str(role).strip()}
    matched = sorted(CANONICAL_ROLE_HINTS.intersection(used_role_set))
    text_role_count = len(CANONICAL_TEXT_ROLE_HINTS.intersection(used_role_set))
    has_window_main = "window/main" in used_role_set
    metrics["canonicalRoleCount"] = len(matched)
    metrics["textRoleCount"] = text_role_count
    metrics["hasWindowMain"] = 1 if has_window_main else 0
    infos.append(f"canonical role matches = {len(matched)}")

    if len(matched) < 6:
        warnings.append(
            "Very low canonical role coverage in ui_contract.usedRoles. "
            "This usually means the model invented a parallel role system and compile may fall back to defaults."
        )
    if text_role_count == 0:
        warnings.append("No canonical text roles detected in ui_contract.usedRoles.")
    if not has_window_main:
        warnings.append("Canonical root role 'window/main' is not used.")

    return warnings, infos, metrics


def add_semantic_health_warnings(
    warnings: List[str],
    infos: List[str],
    theme: Dict[str, object],
    theme_metrics: Dict[str, int],
    contract_metrics: Dict[str, int],
) -> None:
    presets = theme.get("visualPresets", [])
    active_preset_ids = set()
    if isinstance(presets, list):
        for preset in presets:
            if isinstance(preset, dict) and not is_empty_visual_preset(preset):
                preset_id = str(preset.get("presetId", "")).strip()
                if preset_id:
                    active_preset_ids.add(preset_id)

    page_background_custom = field_state(theme, "pageBackground") == "custom"
    card_semantics_available = field_state(theme, "cardFill") == "custom" or "card/default" in active_preset_ids
    button_semantics_available = field_state(theme, "buttonFill") == "custom" or "button/default" in active_preset_ids
    semantic_healthy = (
        page_background_custom
        and card_semantics_available
        and button_semantics_available
        and theme_metrics.get("tokenCount", 0) > 0
        and theme_metrics.get("activePresetCount", 0) >= 2
        and contract_metrics.get("canonicalRoleCount", 0) >= 8
        and contract_metrics.get("textRoleCount", 0) >= 3
        and contract_metrics.get("hasWindowMain", 0) == 1
    )

    card_fill_warning = f"theme.cardFill uses a compile default sentinel value: {str(theme.get('cardFill', '')).strip()}"
    if semantic_healthy and field_state(theme, "cardFill") == "default" and "card/default" in active_preset_ids:
        if card_fill_warning in warnings:
            warnings.remove(card_fill_warning)
        infos.append(
            "theme.cardFill stayed at a compile default sentinel, but card/default preset compiled with active fill semantics."
        )

    secondary_default_count = theme_metrics.get("secondaryDefaultCount", 0)
    secondary_default_fields = [field for field in SECONDARY_THEME_FIELDS if field_state(theme, field) == "default"]
    if secondary_default_count > 0:
        field_list = ", ".join(secondary_default_fields)
        if semantic_healthy:
            infos.append(
                "Secondary theme fields fell back to compile defaults but core theme semantics remain healthy: "
                f"{field_list}"
            )
        else:
            warnings.append(
                "Secondary theme fields fell back to compile defaults: "
                f"{field_list}. This usually means the site theme was under-specified."
            )

    empty_preset_count = theme_metrics.get("emptyPresetCount", 0)
    active_preset_count = theme_metrics.get("activePresetCount", 0)
    if empty_preset_count > 0 and active_preset_count > 0:
        if semantic_healthy:
            infos.append(
                f"{empty_preset_count}/{empty_preset_count + active_preset_count} theme.visualPresets are empty, "
                "but the active presets are sufficient to preserve the current theme semantics."
            )
        else:
            warnings.append(
                f"{empty_preset_count}/{empty_preset_count + active_preset_count} theme.visualPresets are effectively empty."
            )



def main() -> int:
    parser = argparse.ArgumentParser(description="Audit compiled theme health for an AIToUGUI site package.")
    parser.add_argument("site_root", type=Path, help="Path to the site package root.")
    args = parser.parse_args()

    site_root = args.site_root.resolve()
    layout = resolve_site_package_layout(site_root)
    report_path = layout.reports_root / "theme_health_report.json"

    warnings: List[str] = []
    infos: List[str] = []
    contract_audited = False

    bundle_path = layout.bundle_path
    if not bundle_path.exists():
        warnings.append("compiled_site_bundle.json is missing.")
    else:
        bundle = load_json(bundle_path)
        theme = bundle.get("theme", {})
        if not isinstance(theme, dict):
            warnings.append("compiled_site_bundle.json.theme is missing or malformed.")
        else:
            theme_warnings, theme_infos, theme_metrics = audit_theme(theme)
            warnings.extend(theme_warnings)
            infos.extend(theme_infos)
            contract_warnings, contract_infos, contract_metrics = audit_contract_semantics(site_root)
            contract_audited = True
            warnings.extend(contract_warnings)
            infos.extend(contract_infos)
            add_semantic_health_warnings(warnings, infos, theme, theme_metrics, contract_metrics)

    if not contract_audited:
        contract_warnings, contract_infos, _contract_metrics = audit_contract_semantics(site_root)
        warnings.extend(contract_warnings)
        infos.extend(contract_infos)

    status = "pass" if not warnings else "warn"
    payload = {
        "status": status,
        "warningCount": len(warnings),
        "infoCount": len(infos),
        "warnings": warnings,
        "infos": infos,
        "bundlePath": bundle_path.as_posix(),
    }
    layout.reports_root.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"[AIToUGUI ThemeHealth] status={status} warnings={len(warnings)} report={report_path}")
    for warning in warnings:
        print(f"[AIToUGUI ThemeHealth] warn: {warning}")

    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
