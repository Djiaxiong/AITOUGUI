# AI_UGUI_Creator

`AI_UGUI_Creator` is the complete directory prepared for external delivery.

It carries three shippable parts, plus one implementation-docs folder that stays in the project only:

- `ai-create-ugui/`
  - AI-side workflow, spec docs, checklists, and the Python tool chain (the Skill itself)
- `AIToUGUI/`
  - Unity-side import, preview, bake, Prefab export, and runtime adoption
- `Samples/`
  - Representative showcase samples (FPS, Casual)
- `Doc/` (kept in the project only, not shipped)
  - Overview, Skill implementation notes, Unity implementation notes, and other deep-dive docs

## Current Release Stance

This version has a clear, finalized scope:

1. The Unity side is the stable landing layer
   - We no longer invest mainly in adding implicit fallback fixes to Unity
2. The Skill side is the last main reinforcement layer
   - Focus on the planning / constraint / validation / repair / compile loop
3. The official target is a mid-to-high capability model
   - Low-capability models are no longer in the release support scope

## Officially Supported Modes

1. `Single Page Text Mode`
2. `Single Page Image Mode`
3. `Full Suite Mode`

`core-flow` is still kept, but it is the reduced-delivery fallback when a full UI suite is too hard. It is no longer a standalone primary product mode.

## Recently Added

The release now includes the SVG asset pipeline as a standard step:

- The Skill can explicitly author local `source/assets/*.svg`
- Unity Studio can preprocess SVG and import it as `sprite / nineslice`
- Rasterized outputs are isolated under `Assets/AIToUGUI_Generated/<siteId>/Sprites`
- Recommended for icons, complex borders, rotating backgrounds, complex plates and ornaments
- Plain solid-color panel / button / roundrect still go procedural first; do not force SVG

## Final Pre-Release Pass

Before release we stop reinforcing indefinitely and converge in this order:

1. Finish the last Skill reinforcement pass
2. Pass the pre-release review
3. Generate representative showcase samples
4. Finalize docs and README
5. Ship per the delivery boundary (Skill + Unity + Samples + zh/en README)

Sample notes:

- [Samples/FPS/README.md](./Samples/FPS/README.md)
- [Samples/Casual/README.md](./Samples/Casual/README.md)

For deeper implementation details, see `Doc/` inside the project (not shipped):

- `Doc/README.md`
- `Doc/README_Skill实现.md`
- `Doc/README_Unity实现.md`

## Delivery Boundary

The official external delivery contains only:

- `ai-create-ugui/` (Skill)
- `AIToUGUI/` (Unity plugin)
- `Samples/` (showcase samples)
- `README.md` (Chinese)
- `README_EN.md` (English)

`Doc/` stays in the project and is not shipped.
Do not mix legacy experiment directories, old Workbench materials, or scattered temporary cases into the official delivery package.
