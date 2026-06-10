# Page Plan

## Page

- pageId: `weapon-loadout`
- displayName: `Weapon Loadout`
- purpose: `show a stable FPS loadout composition with explicit modules and direct Unity-safe structure`
- confidence: `high`
- fallback: `preserve the five main module rectangles and action hierarchy`

## Layout Budget

- `TopNavBar`: `x=40 y=24 w=1840 h=76`
- `LeftWeaponList`: `x=48 y=132 w=360 h=820`
- `CenterWeaponStage`: `x=436 y=132 w=760 h=610`
- `RightAttachmentBoard`: `x=1228 y=132 w=644 h=610`
- `BottomStatCompare`: `x=436 y=770 w=1436 h=182`

## Main CTA

- id: `RightApplyButton`
- purpose: `apply current attachment selection`
- priority: `P0`

## Approved Semantics

- roles: `window/main`, `panel/resource-bar`, `panel/hero`, `panel/ink`, `panel/section`, `card/accent`, `card/entry`, `card/info`, `button/primary`, `button/secondary`, `button/ghost`, `text/*`
- shapes: `plate`
- frames: `outline`

## Safety Notes

- no image nodes
- no dashed slot treatment
- no hidden overflow used to close layout
