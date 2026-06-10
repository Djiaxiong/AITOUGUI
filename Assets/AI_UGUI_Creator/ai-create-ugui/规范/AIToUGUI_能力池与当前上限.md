# AIToUGUI 能力池与当前上限

这份文档只回答一个问题：

Draft 阶段可以大胆到什么程度，Normalize 阶段又必须收敛到什么程度。

## 两层能力边界

### 第一层：Draft 可表达

Draft 阶段允许更强的设计表达：

- 更鲜明的标题条和页头
- 更明显的装饰语言
- 更有辨识度的模块家族
- 先做更接近原设计气质的 HTML 第一稿

这一层的目标是设计感，不是直接进入 Unity。

### 第二层：Normalized 可稳定消费

最终进入 Unity 的，只能是 Normalize 后的站点包。

也就是说：

- Draft 可以更自由
- Normalized 必须落回当前能力池

## 当前稳定支持

### 结构与布局

- 多页面站点包
- `site.json.designWidth x site.json.designHeight` 设计空间
- `:root` token
- `.class`
- `[data-ui-role="..."]`
- 页面根、面板、卡片、HUD 块、弹窗、按钮、进度区
- `position:absolute`
- `display:flex`
- `flex-direction: row / column`
- `flex-grow` / `flex-shrink` / `flex:1`
- `gap`
- `padding`
- `justify-content`
- `align-items`
- `margin-left:auto`
- `margin-top:auto`
- `min-width / min-height / max-width / max-height`

### 文本

- 真实 DOM 文本
- 标题、按钮、标签、说明、数值
- `color`
- `font-size`
- `font-family`
- `font-weight`
- `line-height`
- `text-align`
- `letter-spacing`
- `text-transform`
- `font-family: primary / heading / mono`

### 视觉

- 纯色填充
- 单层线性渐变
- 统一圆角
- 非均匀圆角
- 胶囊
- 单层描边
- `border-style: solid / dashed`
- 单层阴影
- 单层 glow
- 语义明确的多层阴影：`glow + projection + 更多层`
- `overflow:hidden`
- `opacity`（0..1，CanvasGroup.alpha）
- `transform: rotate(<deg>)`（通过 -ai-rotation-z）
- `animation: rotate / float / pulse` 预设（通过 -ai-loop-motion）
- `animation-delay`
- `-ai-translate-x` / `-ai-translate-y`
- `data-ui-shape`
- `data-ui-frame`
- `data-ui-glow`
- `data-ui-glow-color`
- `data-ui-glow-blur`
- `data-ui-glow-power`

### 当前视觉能力

- 程序化圆角、边框与线条
- 稳定阴影与发光
- 切角、plate、banner 等异形轮廓

## 当前可近似支持

这些可以做，但不要承诺浏览器像素级等价：

- 文本度量
- 多分辨率区域锚点适配
- 阴影强弱微差
- glow 强弱微差
- 渐变质感微差
- `cut-corner`
- `plate`
- `banner`
- 有明确语义的多层 `box-shadow`
- `radial-gradient`（Baker 会近似为线性渐变方向）

## 当前不支持或不应直接输出

### 浏览器能力

- `grid`
- `flex-wrap`
- `calc()`
- `aspect-ratio`
- `position:fixed`
- 响应式断点重排
- 任意浏览器 `background-image`（仅显式本地 SVG 资产引用除外）
- `clip-path`
- `mask`
- `mix-blend-mode`

### 特效

- `filter`
- `backdrop-filter`
- 毛玻璃
- 真实模糊
- `conic-gradient`
- `repeating-linear-gradient`
- `repeating-radial-gradient`
- 任意路径布尔异形
- 复杂 `@keyframes`（非 rotate/float/pulse 预设的自定义关键帧）

### 运行时

- 完整图像资源自动注入
- 完整动画系统
- 任意运行时 HTML/CSS 拼装

### 正式支持的 SVG 资产路径

- 允许显式引用本地 `source/assets/*.svg`
- 允许通过 `data-ui-asset-id/type/usage/import` 把 SVG 资产编译进 bundle 的 `assets`
- 允许为 frame / border / button skin 提供 `data-ui-asset-slice`
- 最终 `SVG -> PNG / Sprite / Nine Slice` 仍由 Unity Editor 侧完成

## 一致性边界

### 硬一致层

- 结构
- 布局
- 命名
- 颜色
- 圆角/形态方向
- 描边
- 阴影
- 渐变

### 软一致层

- 字体字形
- 文本换行
- 字高与字符度量

## 统一降级顺序

当 Draft 稿超出能力池时，按这个顺序降级：

1. `special-effects`
2. `ornament-overdraw`
3. `shape-complexity`
4. `layout`

也就是：

- 先砍毛玻璃、滤镜、复杂遮罩
- 再收掉过密装饰
- 再把极端轮廓收回当前家族
- 最后才允许改布局

## 当前最重要的生产约束

1. 结构和命名必须从第一稿就稳定
2. 重复模块内部必须统一
3. 风格差异优先体现在页面骨架和模块家族，不只是颜色
4. Normalize 后的最终 HTML 才是 contract 和 compile 的真源
5. `data-ui-shape` 只表达轮廓拓扑，不给普通矩形做“风格贴标”
6. 当旧 `shapeId` 与显式 CSS 视觉冲突时，优先保留最终可见结果
7. 双层阴影只能按 `glow + projection` 口径 authoring

## 近期收口经验

### 视觉优先

最近几次还原偏差说明了一个事实：

- 真正需要被还原的是“最终可见结果”
- 不是历史上某个旧标签曾经想表达什么

所以当前正式规则是：

- 显式 `border-radius`、显式边框、显式阴影语义
  - 优先级高于旧 `shapeId`

### 拓扑与风格分离

当前应把两个问题分开看：

- 轮廓拓扑是不是矩形系
- 表面风格是不是终端感、金属感、铭文感

前者决定：

- 走矩形系程序化圆角/边框
- 还是走异形轮廓渲染

后者决定：

- 颜色
- 描边
- glow
- 渐变
- 装饰节点

### 旧产物兼容层

修旧页面时，允许存在历史遗留 `shapeId`。

但兼容口径已经明确：

- 可以保留旧标签
- 不能让旧标签压过显式视觉结果

## 当前仍可能拉平风格的点

1. 复杂 CSS 选择器不会进入 Unity 消费层，最终仍要回到 `:root`、`.class`、`[data-ui-role="..."]`
2. 任意网页字体栈最后只会粗映射到 `primary`、`heading`、`mono`
3. `toggle`、`slider`、`progress` 这类 primitive 内部结构仍然更容易被主题化拉平
4. 如果把差异主要押在滤镜、混合模式、遮罩、浏览器裁切上，最终仍会被降级成相似结果
5. 如果把普通矩形误写成异形 `shape`，仍然可能引入不必要的 Unity 侧分流与视觉偏差
