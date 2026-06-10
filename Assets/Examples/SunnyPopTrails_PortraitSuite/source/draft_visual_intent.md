# Draft Visual Intent

## Style Axes

```yaml
styleAxes:
  layoutArchetype: scrapbook
  shapeLanguage: roundrect
  frameLanguage: solid
  ornamentLanguage: sticker-corner
surfaceMaterial: acrylic
depthModel: layered-shadow
typographyTone: playful
```

## Intent

- bright portrait casual match-3 presentation aimed at a release-facing mobile sample
- candy-gloss panels, soft sky gradients, and readable game-state hierarchy
- clear separation between home, loading, and gameplay rather than three recolors of one page

## SVG Usage Strategy

- use SVG only for compact iconography and badge ornament that benefits from clean vector edges
- keep panels, ribbons, buttons, progress bars, and most gameplay shapes procedural
- do not create SVG just to satisfy the feature; if a page module does not need iconography, keep it procedural

## Normalize Strategy

- preserve portrait composition and gameplay readability first
- keep large surfaces procedural and let SVG handle only the icon and badge layer
- avoid browser-only filters, masks, blend modes, and screenshot-style large images

## Hard Limits

- no full-page raster backplates
- no SVG text used for runtime-modified labels
- no unnecessary SVG for ordinary rounded cards or buttons
- no browser-only filter or mask chains
