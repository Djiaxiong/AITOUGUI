# AIToUGUI Repair Summary

Use this summary first.
Avoid reading raw `validation_report.json` unless this summary is still insufficient.

## Status

- siteRoot: `E:\UnityProject\AIGeneratedUGUI\Assets\AI_UGUI_Creator\Samples\Casual\SunnyPopTrails_PortraitSuite`
- status: `fail`
- errorCount: `4`
- warningCount: `13`
- failingPages: `1`

## Failing Pages

- `home` -> `pages/home.html` (2 errors, 2 warnings)

## Issue Buckets

- `errors`: 2

## Snapshot Artifacts


## Priority Repair Tasks

1. [`home`] HomePlayButton exceeds HomeHeroPanel height (588.0px > 580.0px).
   Files: `pages/home.html`
2. [`home`] HomePlayNote exceeds HomeHeroPanel height (626.0px > 580.0px).
   Files: `pages/home.html`

## Workflow

- Start from the tasks above and the matching HTML/CSS/contract files.
- Use `compiled_pages/` for structure issues and `layout_snapshots/` for footprint or overflow issues.
- Regenerate contract/self-check if metadata drifted.
- Rerun `validate_and_prepare_repair.py` after the fixes.