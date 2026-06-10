# Page Plan

## Page

- pageId: `loading`
- displayName: `Loading`
- purpose: `show a clean transition page with stage identity, target preview, and progress feedback`
- confidence: `high`
- fallback: `preserve the center crest, progress module, target preview band, and tip card`

## Layout Budget

- `LoadingHeroBadge`: `x=348 y=160 w=384 h=384`
- `LoadingLevelCard`: `x=140 y=688 w=800 h=140`
- `LoadingMascotPanel`: `x=236 y=860 w=608 h=272`
- `LoadingProgressCard`: `x=140 y=1198 w=800 h=184`
- `LoadingTargetPreview`: `x=140 y=1422 w=800 h=220`
- `LoadingTipCard`: `x=120 y=1680 w=840 h=120`

## Main CTA

- id: `none`
- purpose: `non-interactive transition state`
- priority: `P0`

## Approved Semantics

- roles: `window/main`, `panel/hero`, `panel/section`, `card/accent`, `card/info`, `text/*`
- shapes: `plate`
- frames: `solid`

## Safety Notes

- SVG only for the crest badge
- no fake browser spinners or custom keyframes
- keep the loading page centered and simple
