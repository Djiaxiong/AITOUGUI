# Unity 能力与 HTML 写法映射

本文件是 `ai-create-ugui -> validator -> compiler/bundle -> baker -> runtime adapter` 的统一口径。

目标不是浏览器炫技，而是让 AI 第一稿就写出 Unity 真能消费、真能还原的结构与视觉。

## Core Native

这些能力可以直接写，属于 full `AIToUGUI` 的正式能力：

- 布局：
  - `position:absolute`
  - `display:flex`
  - `flex-direction: row | column`
  - `gap`
  - `padding`
  - `margin`
  - `width / height / min-* / max-*`
- 文本：
  - `color`
  - `font-size`
  - `font-family`
  - `font-weight`
  - `line-height`
  - `letter-spacing`
  - `text-align`
  - `text-transform`
- 视觉：
  - `background-color`
  - 单层 `linear-gradient(...)`
  - `border`
  - `border-radius`
  - `box-shadow`
  - `data-ui-shape`
  - `data-ui-motion`
  - `data-ui-glow*`

## Core Plus

这些能力现在也是正式能力，但 authoring 要按下面的约束写：

- 旋转：
  - 首选 `data-ui-rotation="-12deg"`
  - 兼容 `transform: rotate(-12deg)`
  - 只允许单一 `rotate(z)`
- 透明度：
  - `opacity: 0.0 ~ 1.0`
- 虚线边框/圆环：
  - `border-style: dashed`
  - 再配合 `border-width` / `border-color` / `border-radius`
- 持续循环动效：
  - 首选 `data-ui-loop-motion`
  - 允许值：
    - `loop/rotate-slow`
    - `loop/rotate-slow-reverse`
    - `loop/float-soft`
    - `loop/pulse-soft`

## 推荐写法

### 旋转装饰

```html
<div
  data-ui-name="sealRing"
  data-ui-role="ornament/ring"
  data-ui-rotation="-15deg"
  style="
    position:absolute;
    left:860px;
    top:180px;
    width:200px;
    height:200px;
    border:2px solid rgba(255,215,128,0.8);
    border-radius:50%;
  ">
</div>
```

### 虚线圆环

```html
<div
  data-ui-name="magicCircle"
  data-ui-role="ornament/ring"
  style="
    position:absolute;
    left:720px;
    top:220px;
    width:480px;
    height:480px;
    border-width:2px;
    border-style:dashed;
    border-color:rgba(120,220,255,0.85);
    border-radius:50%;
    background-color:transparent;
  ">
</div>
```

### 持续慢旋

```html
<div
  data-ui-name="magicCircle"
  data-ui-role="ornament/ring"
  data-ui-loop-motion="loop/rotate-slow"
  style="
    position:absolute;
    left:720px;
    top:220px;
    width:480px;
    height:480px;
    border-width:2px;
    border-style:dashed;
    border-color:rgba(120,220,255,0.85);
    border-radius:50%;
    background-color:transparent;
  ">
</div>
```

### 兼容写法

只有在必须兼容旧页面时才保留 raw CSS：

```css
transform: rotate(-15deg);
animation: rotate 12s linear infinite;
animation-delay: 0.4s;
```

编译阶段只会归一化以下 loop 动效：

- `rotate <duration>s linear infinite`
- `rotate <duration>s linear infinite reverse`
- `float <duration>s ease-in-out infinite`
- `pulse <duration>s ease-in-out infinite`

## 明确禁止

以下仍然属于 browser-only，不要写：

- 任意 `translate / scale / skew / matrix`
- 任意 transform 组合
- 任意 `@keyframes`
- 任意复杂 `animation`
- `filter`
- `backdrop-filter`
- `clip-path`
- `mask`
- `mix-blend-mode`
- `radial-gradient`
- `conic-gradient`
- `repeating-* gradient`
- `grid`
- `flex-wrap`

## 风格差异应该放在哪里

为了既保留风格差异，又不把 Unity 侧逼回 repair，优先把差异放在这些安全维度：

- 布局骨架
- 标题与徽章的轻微旋转
- 虚线边框和圆环装饰
- 渐变、描边、阴影、辉光
- 半透明装饰层
- 轻量循环动效

不要把风格差异主要压在浏览器专属滤镜、伪元素特效或复杂 keyframes 上。
