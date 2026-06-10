# AIToUGUI Team Generation Workflow

Use team mode only when the site package has 3 or more pages and the expected gain from parallelism is larger than the coordination cost.

Team mode is an acceleration strategy, not the default quality strategy.
If the design is highly coupled or the layout language is still unstable, single-agent mode is safer.

## Four Phases

```text
Phase 1: Team Lead Planning
  -> style intent + page scope + site plan + page plans + semantic freeze

Phase 2: Team Lead Package Setup
  -> contract + theme + shared widgets + worker briefs

Phase 3: Page Workers
  -> one worker per page, parallel implementation only

Phase 4: Validation and Repair
  -> serial validate + targeted repair + theme health audit
```

## Phase 1: Team Lead Planning Responsibilities

### Inputs

- user requirements
- page list
- game/theme references
- hard production constraints

### Outputs

Team Lead must prepare:

| File / Artifact | Purpose |
|---|---|
| `draft_visual_intent.md` | style intent freeze |
| `page_scope.md` | page scope recognition and page-set freeze |
| `site_plan.md` | site-level page strategy, shared invariants, and page delta matrix |
| `page_plans/<pageId>.md` | per-page layout budget and CTA hierarchy |
| `site.json` | site manifest |
| `ui_contract.json` | pages, named nodes, approved semantics |
| `theme.css` | root tokens and allowed role styling |
| `shared/widgets.css` | shared component families only |
| `preview.html` | preview entry |
| `WorkerBriefs` | one compact worker brief per page |

Each page plan should include:

- page goal
- confidence
- fallback
- main regions
- module rectangles
- main CTA
- information hierarchy
- approved canonical roles
- inherited invariants
- unique anchors
- explicit "what not to do"

### Team Lead Planning Anti-Patterns

- Do not let weak models jump straight from user request to multi-page HTML.
- Do not skip page-scope recognition when the user asked for a full UI suite.
- Do not freeze only color names; freeze color relationships and usage ratios.
- Do not leave page complexity unconstrained when the model already struggles with contract compliance.
- Do not ask every worker to fill the same fixed prompt template with a few nouns swapped.

## Phase 2: Team Lead Package Setup

Team Lead must prepare:

| File / Artifact | Purpose |
|---|---|
| `site.json` | site manifest |
| `ui_contract.json` | pages, named nodes, approved semantics |
| `theme.css` | root tokens and allowed role styling |
| `shared/widgets.css` | shared component families only |
| `preview.html` | preview entry |
| `WorkerBriefs` | one compact worker brief per page |

### Team Lead Must Also Precompute

For every page:

- approved `layoutArchetype / shapeLanguage / frameLanguage / ornamentLanguage`
- approved `data-ui-role` list
- approved `data-ui-shape` list
- approved `data-ui-frame` list
- a layout budget with explicit major-module rectangles
- which site-level invariants must remain unchanged on that page
- which page-level anchors must stay unique relative to sibling pages
- explicit wrapper bands for any parallel regions such as `MainContentRow`, `TopMetaRow`, or `BottomActionRow`
- which shared component families the page is allowed to use

The layout budget should look like:

```text
TopBanner: x=0 y=0 w=1920 h=120
MainContentRow: x=48 y=168 w=1824 h=720
LeftRail: x=48 y=168 w=280 h=720
MainBoard: x=360 y=168 w=1512 h=720
FooterActionBar: x=360 y=912 w=1512 h=72
```

This budget is the main containment control.
Without it, parallel workers will drift and push convergence cost into repair.

The layout budget must be expressed in the locked site `designWidth/designHeight`, not assumed to be one fixed desktop size.

For `full-suite`, Team Lead must also write a simple page-delta matrix before workers start.
That matrix should make it obvious why page A is not page B with modules moved around.

### Team Lead Setup Anti-Patterns

- Do not ask each worker to re-run a full draft exploration phase.
- Do not make each worker rediscover the shared style system from scratch.
- Do not leave approved roles/shapes/frames implicit.
- Do not leave geometry open-ended if the page must fit a strict locked design-resolution frame.
- Do not ask workers to reuse old examples as starting shells.
- Do not ask all workers to implement the same page skeleton with swapped labels and coordinates.

## Phase 3: Page Worker Responsibilities

### Worker Scope

Each worker owns exactly one page HTML file.

The worker:

- implements the page inside the Team Lead layout budget
- uses the approved semantics only
- uses shared widgets first
- keeps all named nodes stable
- normalizes immediately toward Unity-safe output
- runs page-level validation before handoff

The worker does not:

- modify `theme.css`
- modify `shared/widgets.css`
- modify `ui_contract.json`
- modify `site.json`
- invent new shared classes
- invent new roles/shapes/frames
- redesign the page outside the assigned layout budget

### Worker Context Policy

Each worker should receive a compact plain-language brief.

Preferred read order:

1. worker brief
2. launcher rules
3. full skill only if blocked

If every worker re-reads the full skill pack and re-derives the design language, startup latency grows sharply and page drift increases.

### Worker Geometry Checklist

Before a worker finishes:

- root matches site `designWidth/designHeight`
- no major block crosses the root bounds
- no negative `left` or `top`
- no fixed-size flex overflow after padding/border
- no mixed "fixed-height panel + unresolved flex stack" that relies on browser reflow to hide overflow
- spacer / filler nodes inside fixed panels use explicit closed sizes instead of implicit remaining-space assumptions
- intended side-by-side regions must sit inside an explicit row wrapper instead of default block flow
- no browser-only feature dependencies
- `python PythonTool/validate_page_package.py <SiteRoot> <pageId>` returns pass
- page still preserves its assigned unique anchors and has not drifted into a sibling page skeleton

Team Lead should not accept a worker handoff until that page-level validator passes.

## Phase 4: Validation and Repair

### Order

1. run `validate_and_prepare_repair.py`
2. if validation passes, run `compile_site_bundle.py`
3. run `audit_compiled_theme_health.py`
4. if validation fails, dispatch targeted repair workers
5. re-run validation
6. compile only after validation passes

Use this split strictly:

- Page Workers own `validate_page_package.py` for their page.
- Team Lead owns `validate_and_prepare_repair.py` for the whole site.
- Team Lead owns the final compile gate.

### Repair Source Priority

When validation fails, read in this order:

1. printed repair digest
2. `repair_summary.md`
3. `repair_prompt.md`
4. raw `validation_report.json` only if the summary is still insufficient

### Repair Ownership

- repair one page at a time where possible
- keep repair workers on page-local HTML changes first
- touch shared styles or contract only when the validator clearly requires it

## Why Team Mode Can Become Slower

Team mode often feels slower when:

- each worker re-reads large docs
- each worker performs its own draft exploration
- Team Lead did not freeze the semantic vocabulary
- Team Lead did not define page geometry budgets
- the site uses too much visual divergence across pages
- all page drift is deferred into one serial repair phase

In that state, parallelism speeds up page drafting but slows down total delivery.

## Recommended Strategy

Use this split:

- Team Lead owns creative divergence and semantic freezing.
- Page Workers own bounded implementation.
- Repair Workers own narrow validator-driven fixes.

If the site still produces many repair errors, reduce worker freedom before changing models.
If the site validates but theme health fails, reduce semantic freedom before changing models.
If the site validates but multiple pages converge into the same skeleton, tighten the page-delta matrix before changing models.

## When To Prefer Single-Agent Mode

Prefer single-agent mode when:

- the page count is small
- the visual system is still unstable
- the shared widget families are not frozen
- the layout is tightly coupled across pages
- the model is repeatedly inventing semantics that later need repair

Single-agent mode is slower per step but often faster end-to-end for unstable prompts.
