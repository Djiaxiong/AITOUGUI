# AIToUGUI Full 能力对齐补充清单

这个清单补在原有站点包检查清单之外，专门用于 full `AIToUGUI` 的首批扩能能力。

## Rotation

- [ ] 旋转只使用 `data-ui-rotation` 或 `transform: rotate(<deg>)`
- [ ] 没有 `translate / scale / skew / matrix`
- [ ] 没有多段 transform 组合

## Opacity

- [ ] `opacity` 只使用 `0..1`
- [ ] 半透明装饰层没有依赖浏览器混合模式

## Dashed Border

- [ ] 虚线边框明确写了 `border-style:dashed`
- [ ] 同时明确写了 `border-width`
- [ ] 同时明确写了 `border-color`
- [ ] 圆环或圆角框明确写了 `border-radius`
- [ ] 纯装饰 ring 没有错误地写成实心填充块

## Loop Motion

- [ ] 持续循环动效优先使用 `data-ui-loop-motion`
- [ ] `data-ui-motion` 只用于 enter / hover / press
- [ ] `data-ui-loop-motion` 只使用以下 preset：
- [ ] `loop/rotate-slow`
- [ ] `loop/rotate-slow-reverse`
- [ ] `loop/float-soft`
- [ ] `loop/pulse-soft`

## Raw CSS Compatibility

- [ ] 如保留 raw `animation`，只能归一化为：
- [ ] `rotate <duration>s linear infinite`
- [ ] `rotate <duration>s linear infinite reverse`
- [ ] `float <duration>s ease-in-out infinite`
- [ ] `pulse <duration>s ease-in-out infinite`
- [ ] 没有任意 `@keyframes`

## Browser-only Still Forbidden

- [ ] 没有 `filter`
- [ ] 没有 `backdrop-filter`
- [ ] 没有 `clip-path`
- [ ] 没有 `mask`
- [ ] 没有 `mix-blend-mode`
- [ ] 没有 `radial-gradient / conic-gradient / repeating-* gradient`
- [ ] 没有 `grid`
- [ ] 没有 `flex-wrap`
