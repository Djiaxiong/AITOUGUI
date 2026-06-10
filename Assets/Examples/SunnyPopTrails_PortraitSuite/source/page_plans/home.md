# Page Plan

## Page

- pageId: `home`
- displayName: `Home`
- purpose: `show a polished portrait match-3 front door with progression, events, and a strong play CTA`
- confidence: `high`
- fallback: `preserve the top status bar, hero block, event strip, and bottom booster shelf`

## Layout Budget

- `HomeTopStatusBar`: `x=56 y=48 w=968 h=116`
- `HomeHeroPanel`: `x=72 y=196 w=936 h=580`
- `HomeEventSection`: `x=72 y=812 w=936 h=356`
- `HomeJourneyCard`: `x=72 y=1202 w=936 h=430`
- `HomeBoosterShelf`: `x=96 y=1670 w=888 h=170`

## Main CTA

- id: `HomePlayButton`
- purpose: `enter the currently selected stage`
- priority: `P0`

## Approved Semantics

- roles: `window/main`, `panel/resource-bar`, `panel/hero`, `panel/section`, `card/accent`, `card/entry`, `card/info`, `button/primary`, `button/secondary`, `button/ghost`, `text/*`
- shapes: `plate`
- frames: `solid`

## Safety Notes

- SVG only for small currency icons, booster icons, and the hero badge ornament
- no screenshot-style backplates
- no overflow-based closure
