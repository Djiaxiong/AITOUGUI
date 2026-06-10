# Page Plan

## Page

- pageId: `gameplay`
- displayName: `Gameplay`
- purpose: `show a real portrait match-3 play state with HUD, board, boosters, and stage goals`
- confidence: `high`
- fallback: `preserve top HUD, board rectangle, booster tray, and goal summary card`

## Layout Budget

- `GameplayTopBar`: `x=40 y=44 w=1000 h=166`
- `GameplayGoalPanel`: `x=40 y=236 w=344 h=120`
- `GameplayMovesPanel`: `x=400 y=236 w=192 h=120`
- `GameplayScorePanel`: `x=608 y=236 w=256 h=120`
- `GameplayPauseButton`: `x=880 y=236 w=160 h=120`
- `GameplayBoardPanel`: `x=144 y=388 w=792 h=916`
- `GameplayComboCard`: `x=56 y=1338 w=968 h=112`
- `GameplayBoosterTray`: `x=56 y=1474 w=968 h=184`
- `GameplayGoalSummary`: `x=56 y=1680 w=968 h=180`

## Main CTA

- id: `GameplayBoosterRocketButton`
- purpose: `show an action-capable gameplay utility entry in the sample`
- priority: `P0`

## Approved Semantics

- roles: `window/main`, `panel/resource-bar`, `panel/hero`, `panel/section`, `card/accent`, `card/entry`, `card/info`, `button/secondary`, `button/ghost`, `text/*`
- shapes: `plate`
- frames: `solid`

## Safety Notes

- SVG only for target and booster icons, not for the whole board
- board uses explicit tile rectangles rather than browser grid
- no hidden overflow used to repair layout
