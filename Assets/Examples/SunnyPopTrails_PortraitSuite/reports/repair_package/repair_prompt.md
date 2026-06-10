# AIToUGUI Repair Prompt

This prompt already contains a condensed repair plan.
Use it first and avoid reading raw `validation_report.json` unless you still need missing detail.

## Goal

- Fix only the issues captured below.
- Do not rewrite the whole site package.
- Do not rename stable `data-ui-name` values without a direct validator reason.
- The package must pass `validate_site_package.py` again when you finish.

## Site Root

`E:\UnityProject\AIGeneratedUGUI\Assets\AI_UGUI_Creator\Samples\Casual\SunnyPopTrails_PortraitSuite`

## Current Status

- status: `fail`
- errorCount: `4`
- warningCount: `13`
- failingPages: `1`

## Snapshot Artifacts

- Check `compiled_pages/` first for structure mismatches.
- Check `layout_snapshots/` first for overflow, footprint, size, or gap issues.

## Failing Pages

- `home` -> `pages/home.html`

## Repair Tasks

1. [`home`] HomePlayButton exceeds HomeHeroPanel height (588.0px > 580.0px).
   Files: `pages/home.html`
2. [`home`] HomePlayNote exceeds HomeHeroPanel height (626.0px > 580.0px).
   Files: `pages/home.html`

## Constraints

- Reuse existing roles, classes, and element families when possible.
- For contract mismatches, treat `site.json` as the page manifest source of truth, then repair `ui_contract.json`.
- For undefined roles or classes, prefer mapping back to the existing system before adding new definitions.
- For root size problems, fix the page root directly instead of compensating with parent wrappers.
- Rerun validation after the edits instead of relying on visual inspection alone.

## Output

- List the files you changed.
- Explain how each repair maps back to the blocking issues.
- Summarize the rerun validation result.