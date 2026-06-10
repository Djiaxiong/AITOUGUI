# Page Plan

## Page

- pageId: `combat-hud`
- displayName: `Combat HUD`
- purpose: `show a clean FPS overlay layout with explicit positioning and stable procedural modules`
- confidence: `high`
- fallback: `preserve anchor rectangles, kill feed stack, bottom health and ammo rails, and center crosshair`

## Layout Budget

- `TopMatchInfo`: `x=720 y=20 w=480 h=64`
- `TopRightKillFeed`: `x=1460 y=28 w=380 h=220`
- `CenterCrosshair`: `x=930 y=500 w=60 h=60`
- `BottomLeftHealthArmor`: `x=40 y=906 w=360 h=124`
- `BottomRightAmmoAbility`: `x=1420 y=876 w=420 h=154`
- `MidPrompt`: `x=760 y=760 w=400 h=64`
- `MiniMapPlaceholder`: `x=40 y=40 w=240 h=240`

## Main CTA

- id: `none`
- purpose: `this page is an information HUD rather than a menu action screen`

## Approved Semantics

- roles: `window/main`, `panel/resource-bar`, `panel/ink`, `panel/section`, `card/accent`, `card/entry`, `card/info`, `text/*`
- shapes: `plate`
- frames: `outline`

## Safety Notes

- no browser-only fixed layout
- no images
- no dashed borders
