# Site Plan

## Site

- siteId: `sunny_pop_trails_portrait_suite`
- displayName: `Sunny Pop Trails Portrait Suite`
- pageCount: `3`

## Shared Invariants

- 1080x1920 fixed portrait canvas
- playful sky-and-candy palette with readable white and navy text contrast
- rounded glossy cards, soft shadows, and high-clarity mobile touch targets
- SVG only for compact icons and badge ornament; large panels and structural surfaces remain procedural
- every major module stays inside the page root without overflow tricks

## Page Set

1. `home`
   - role: release-facing front door
   - focus: status bar, hero logo block, level entry, event cards, booster shelf
2. `loading`
   - role: transition screen
   - focus: level badge, loading progress, preview target pieces, light mascot presentation
3. `gameplay`
   - role: active match-3 play screen
   - focus: top HUD, board, booster tray, and stage objective summary

## Page Delta Matrix

- `home`
  - hero-forward composition with the largest CTA on the page
  - stacked content bands instead of dense gameplay utility
  - event and progression framing
- `loading`
  - centered stage composition with no actionable controls
  - progress bar and target preview replace the home CTA and gameplay board
  - softer, more transitional pacing
- `gameplay`
  - board-centered composition with stronger utility density
  - move counter, score, targets, and boosters drive the page
  - the most compact and interaction-heavy information rhythm in the suite
