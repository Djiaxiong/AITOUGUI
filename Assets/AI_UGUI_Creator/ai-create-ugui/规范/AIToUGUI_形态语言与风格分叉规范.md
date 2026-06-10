# AIToUGUI 形态语言与风格分叉规范

这份文档专门解决一个问题：

为什么同类游戏 UI 容易退化成“同一套组件换配色”，以及怎样把风格差异真正拉开。

## 根因

如果作者态只锁这些信息：

- 游戏题材
- 配色
- 少量阴影和渐变
- 常规 `panel / card / button / chip`

最终结果通常只会是：

同一套圆角矩形组件家族的换色版。

## 新原则：先发散，再收敛

风格差异不应该在第一步就被 Unity 子集限制压扁。

正确顺序：

1. Draft 阶段先拉开构图和组件家族
2. Normalize 阶段再映射到当前能力池
3. 只在最后一步收掉实现不了的细节

## 先锁四个风格轴

在 Draft 阶段先明确：

1. `LayoutArchetype`
2. `ShapeLanguage`
3. `FrameLanguage`
4. `OrnamentLanguage`

其中至少 3 个轴要发生变化，才能算“同类游戏换了新风格”。

## 这四个轴分别是什么

### LayoutArchetype

页面的大骨架。例子：

- `stage`
- `dashboard`
- `scrapbook`
- `binder`
- `album-grid`
- `hero-focus`

### ShapeLanguage

组件轮廓家族。例子：

- `roundrect`
- `per-corner`
- `capsule`
- `cut-corner`
- `plate`
- `banner`

### FrameLanguage

边框和外轮廓表达。例子：

- `solid`
- `outline`
- `hairline`
- `glow`

### OrnamentLanguage

不改业务结构、但能明显拉开风格的装饰语言。例子：

- 标题条
- 票券腰线
- 活页签
- 贴纸角标
- 海报板
- 拼贴底板
- 网格纸背景

## Draft 阶段允许更大胆

Draft 阶段允许提出更发散的形态意图，例如：

- 票券感主按钮
- 活页签页签
- 手账便签卡
- 海报看板
- 拼贴式页头

但 Normalize 阶段必须把它们落回当前可稳定还原的家族。

## Normalize 阶段的生产规则

风格分叉不是“每个元素都长得不一样”。

真正可生产的规则是：

- 页面之间可以分叉
- 模块之间可以分叉
- 高重复元素内部必须统一

也就是：

- 背包格子一整组统一
- 英雄候选卡一整组统一
- 技能按钮按组统一
- 设置页条目按组统一

异形和特殊轮廓留给这些位置：

- 页头
- 主 CTA
- 特色大卡
- 侧栏头
- 关键标题条

## 当前形态家族边界

### 预览安全形态

这些形态适合 HTML 预览与 Unity 最终还原保持较强一致：

- `roundrect`
- `per-corner`
- `capsule`

### Unity 增强形态

这些形态当前更适合作为 Unity 增强路径：

- `cut-corner`
- `plate`
- `banner`

HTML 端通常只能做近似预览，不应承诺浏览器级像素等价。

## shape authoring 补充规则

当前统一补一条边界：

- `roundrect`
- `per-corner`

这两类本质仍然是矩形系轮廓，优先用 `border-radius` 表达。

不要把下列情况继续写成常规 authoring：

- 节点视觉上是标准矩形或四角圆角矩形
- 但同时挂 `data-ui-shape="plate" / "banner" / "cut-corner"`

这些 `shape` 值只应留给：

- 真正非矩形的轮廓拓扑
- 需要 Unity 侧异形后端接管的模块

## 旧页面修复规则

修旧页面时，遇到旧 `shapeId` 和显式 CSS 圆角冲突，按下面口径处理：

1. 先看浏览器预览里最终可见的轮廓
2. 如果结果本质是矩形系，优先保矩形系视觉
3. 不要因为历史标签存在，就强制把节点改回异形底板

## 失败示例

以下情况都不算真正分叉成功：

- 只换配色
- 只换渐变明暗
- 只换标题文案
- 只把所有圆角统一调大或调小
- 把重复模块每个实例都换一种形态

## 验收顺序

判断两套 UI 是否真的不同，优先看：

1. 页面骨架是否不同
2. 模块家族是否不同
3. 标题条、页头、主 CTA 是否不同
4. 装饰语言是否不同
5. 最后才看配色
