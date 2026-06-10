---
name: ai-create-ugui
description: Generate game UI site packages for AIToUGUI through planning, validation, repair, and compile workflows without template-driven convergence.
---

# AICreateUGUI Skill

## 目标定位

这套 skill 不是“网页模板包”，也不是“提示词拼装器”。

它的正式目标只有一个：

- 生成一套能被 `AIToUGUI` 稳定消费的站点包
- 通过本地校验、修复、编译流程
- 最终交付根目录下的 `compiled_site_bundle.json`

不要手写 `compiled_site_bundle.json`。只能通过本地 Python 工具链生成。

## 工作原则

1. 规则驱动，不用模板驱动。
2. 每次生成都要从本次需求重新冻结风格意图和页面规划。
3. 同类题材不能只换配色，至少要在 3 个风格轴上显式分叉。
4. `validate pass` 不是终点，`theme health` 也必须成立。

这套 skill 默认不再依赖 `模板/`、`示例/`、`Cases/` 作为创作起点。
需要的 planning artifact 由 AI 根据当前需求现场生成，而不是套历史页面壳。

## 兼容性与运行环境

这套 skill 要分成两层理解：

1. 工作流是通用的。
   - `Understand Style -> Recognize UI Scope -> Plan Pages -> Draft -> Normalize -> Validate -> Compile -> Theme Health`
   - 只要模型能读文档、写文件、跑本地命令，`Codex / Claude / GPT / GLM` 都能执行这套流程。

2. `SKILL.md` 的装载方式不是通用的。
   - `Codex` 可以把这套目录直接当本地 skill 使用。
   - 其他代理通常需要把 `SKILL.md + 使用指南 + 规范/` 明确注入成项目规则或前置上下文。

### 必需环境

- 可读写当前工程目录
- 可执行本地 `Python 3`
- 可从工程根目录运行 `Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/*.py`
- 允许模型持续读写目标站点包根目录下的：
  - `source/`
  - `reports/`
  - `snapshots/`
- 最终如需导入 Unity，工程里要有对应的 `AIToUGUI` 导入端

### 当前 Python 工具依赖

当前 `PythonTool` 下脚本仅依赖 Python 标准库和项目内脚本互相引用，不依赖额外第三方 pip 包。

### PythonTool 入口与内部模块

正式流程只需要直接运行这 4 个入口脚本：

- `generate_site_contract.py`
- `validate_and_prepare_repair.py`
- `compile_site_bundle.py`
- `audit_compiled_theme_health.py`

其余脚本是被上面入口自动引用的内部模块（如 `site_package_layout.py`、`validate_site_package.py`、`export_site_snapshots.py`、`generate_repair_package.py`）或批量辅助工具（`audit_style_diversity.py`、`organize_site_package.py`、`validate_page_package.py`），不需要单独调用。

## 入口约定

- `SKILL.md`：当前通用入口
- 其他代理如需项目规则入口，应直接复用本文件和 `规范/`，不要再维护模型专用分支说明

所有入口必须共用同一套 `PythonTool / 规范 / 工作流` 约束，不允许分叉出两套标准。

## 正式链路

1. 用户描述 UI 需求
2. AI 使用 `ai-create-ugui` 冻结风格意图、识别页面范围并完成页面规划
3. AI 生成站点包源码并运行本地工具持续校验和修复
4. 工具编译出 `compiled_site_bundle.json`
5. 在 Unity 中通过 `AIToUGUI` 插件导入、预览、导出 Prefab

`Workbench` 不属于当前正式链路。

## 发布范围与目标模型边界

发布前当前应把 `Skill` 视为主要补强层，把 Unity 侧视为稳定落地层。
不要再把主要精力放在“让落地端继续兜底修设计”，而是放在让中上能力模型更稳定地产出可编译、可导入、风格成立的站点包。

当前正式支持边界如下：

1. 目标模型是中上能力模型。
   - 能稳定阅读文档
   - 能持续维护 planning artifact
   - 能执行本地 Python 工具链
   - 单页截图复刻场景下，最好具备多模态能力
2. 低能力模型不再是正式适配范围。
   - 不要为了兼容低能力模型而持续放松语义、布局和交付标准
   - 如果模型连 `page_scope / site_plan / page_plan / canonical role` 都难以稳定维护，优先收缩任务范围或更换模型
3. 整套 UI 的质量提升，优先从 `planning -> authoring constraint -> validator/repair language -> compile gate` 这条链路补强，而不是继续给 Unity 侧增加隐式修复。

## 正式使用模式

当前对外正式支持 3 种使用模式；它们共用同一套工具链，但前置规划强度不同。

### 1. Single Page Text Mode

- 适用：用户用语言描述一个页面
- 内部映射：`scopeMode = single-page`，`inputMode = text-brief`
- 目标：把单页结构、布局和视觉语言稳定落到 Unity-safe HTML

### 2. Single Page Image Mode

- 适用：用户给一张 UI 截图、草图或参考图，希望复刻页面布局
- 内部映射：`scopeMode = single-page`，`inputMode = image-reference`
- 要求：模型具备多模态能力
- 关键策略：先把参考图抽象成模块矩形、信息层级、组件家族、配色关系和装饰语义，再重建为无图片资源依赖的程序化页面，不做像素级描摹

### 3. Full Suite Mode

- 适用：用户要一整套 UI，或明确要求多页、完整流程、一组互相关联页面
- 内部映射：`scopeMode = full-suite`
- 这是当前最依赖 Skill 补强的模式，也是发布前优先打磨的模式

`core-flow` 继续保留，但它是 `full-suite` 下的收缩交付策略，不是单独的主要产品模式。
当整套 UI 的任务难度过高、模型置信度不足、或用户只要核心流程时，再从 `full-suite` 收成 `core-flow`。

## Full Suite 一致性控制

整套 UI 的难点不是“会不会画一个页面”，而是“多页既像同一套 UI，又不会只是在挪位置”。

因此在 `full-suite` 模式下，`site_plan.md` 不能只写页面列表，还必须冻结两层东西：

1. `shared invariants`
   - 全站共用的 style axes
   - 标题条 / 面板 / 按钮 / 卡片家族
   - canonical role 策略
   - 导航、资源条、信息条、弹窗体系的共性规则
   - 配色关系和使用比例
2. `page delta matrix`
   - 每页的主要目标
   - 主导区域骨架
   - 主 CTA 家族
   - 信息密度
   - 装饰焦点
   - 明确禁止与哪些页面收敛成同一骨架

硬约束：

- 每页至少要有 3 个清晰差异点
- 其中至少 1 个必须来自页面骨架、模块节奏或主导交互，不允许全部只是换配色或交换位置
- 不要把所有页面都收成同一套“标题栏 + 左侧栏 + 主面板 + 底部按钮”的平移版
- 不要让每一页都复用同一组大卡片骨架，只换文案和坐标
- 如果模型对整套 UI 把握不足，先收成 `core-flow`，不要硬顶着生成“完整但趋同”的多页站点包

## 布局保守约束

这部分是发布前补强规则，默认对所有模型生效，尤其用于约束弱模型输出：

1. 如果一个 `display:flex` 容器有明确的固定高度或固定宽度，那么它的直接 flow 子项总 footprint 必须可在该轴内闭合，不能指望 Unity 侧再帮它收缩。
2. 不要在同一个 flow 容器里混用“必须依赖内容自增的 suggested 子项”和“大量固定像素高/宽子项”去硬塞满固定面板。
3. 需要占位但不显示内容的中间 spacer，必须显式写死尺寸；不要只写 `width` 或只写 `height` 后依赖浏览器剩余空间语义。
4. 对于设置页、列表页、表单页这类垂直堆叠页面，若总高度接近上限，优先减少模块数量、间距或装饰，不要把超出的责任留给 `overflow`。
5. `overflow:hidden` 只能做裁切，不能作为布局闭合手段；如果 validator 报 footprint 超限，必须回到页面结构修正。
6. 非显式语义布局页，不要假设 Unity 运行时存在浏览器级别的 flex 自动回流；页面应在站点包阶段就闭合。

## 必读文件

开始生成前，优先阅读这些文件：

1. `规范/AIToUGUI_GenerationLauncher.md`
2. `规范/UI风格参考.md`
3. `规范/游戏UI页面识别标准文档.md`
4. `规范/AIToUGUI_形态语言与风格分叉规范.md`
5. `规范/AIToUGUI_SitePackageSpec.md`
6. `规范/AIToUGUI_能力池与当前上限.md`
7. `规范/AIToUGUI_Contract与自检规范.md`
8. `规范/AIToUGUI_CSS与HTML实现细则.md`
9. `使用指南.md`

其中第 2 到第 4 条是前置规划硬约束。
在动笔写 HTML 之前，先冻结风格轴，再冻结页面范围。

```yaml
styleAxes:
  layoutArchetype: stage | dashboard | scrapbook | binder | album-grid | hero-focus
  shapeLanguage: roundrect | per-corner | capsule | cut-corner | plate | banner
  frameLanguage: solid | outline | hairline | glow
  ornamentLanguage: title-bar | ticket-line | tab-folder | sticker-corner | poster-board |
                    collage | grid-paper | ring | ribbon | seal | laurel | none
surfaceMaterial: jade | brass | lacquer | alloy | parchment | leather | stone | acrylic
depthModel: flat-stack | layered-shadow | inset-relief | luminous | cockpit
typographyTone: heroic | technical | ritual | playful | archival
```

前 4 轴是当前推荐硬记录轴，后 3 项用于补足材质、空间和字体气质。

如果手头有一批同阶段站点包，可用下面的命令检查轴差异：

```powershell
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/audit_style_diversity.py <SiteCollectionRoot> --threshold 3
```

`<SiteCollectionRoot>` 指同一批站点包的父目录，不要求是历史 `Cases/` 路径。

## 标准工作流

必须按这 6 步执行：

1. `Understand Style + Freeze Intent`
2. `Recognize UI Scope + Freeze Page Set`
3. `Plan Pages + Freeze Semantics`
4. `Draft HTML`
5. `Normalize to Unity-safe HTML`
6. `Validate + Compile + Theme Health Check`

弱模型或中文模型不要从用户需求直接跳到多页面 HTML。
先冻结风格、页面范围和页面规划，再落 HTML，能明显减少第一轮几百个报错和后续 repair 漂移。

### 0. 计划产物

在开始写页面前，至少准备这些 planning artifact：

- `source/draft_visual_intent.md`
- `source/page_scope.md`
- `source/site_plan.md`
- `source/page_plans/<pageId>.md`

这些文档必须围绕本次需求现场生成，不要复制旧页面、旧提示词或固定模板骨架。

### 1. Understand Style + Freeze Intent

先解决这些问题：

- 题材有没有被真正理解对，而不是落成“通用手游蓝金风 / 通用赛博玻璃风”
- 视觉重点到底是材质、轮廓、布局还是装饰
- 哪些风格语义必须保住，哪些装饰在低置信度时可以主动删掉
- 是否已经与同类历史产物拉开至少 3 个风格轴

这一阶段必须在 `source/draft_visual_intent.md` 里写清楚：

- `styleAxes`
- `surfaceMaterial / depthModel / typographyTone`
- 题材关键词与禁止项
- 配色关系，而不是只写几个色值名词
- normalize 时优先保留的视觉语义
- 低置信度 fallback

低置信度时的正确策略：

- 先减装饰密度
- 再减异形数量
- 再减 motion
- 最后才减页面复杂度

### 2. Recognize UI Scope + Freeze Page Set

在正式写 `site_plan` 之前，先识别本轮到底是单页任务还是整套 UI 任务。

必须按 `规范/游戏UI页面识别标准文档.md` 生成 `source/page_scope.md`。

`source/page_scope.md` 至少要写清楚：

- `scopeMode`
  - `single-page`
  - `core-flow`
  - `full-suite`
- `inputMode`
  - `text-brief`
  - `image-reference`
- 游戏类型
- 运行模式
- 产品定位
- UI 范围判断
- 页面结构树
- 页面清单
- 每页优先级
- 页面来源
- 推荐最小页面集
- 推荐完整页面集
- 本轮实际进入设计的页面范围

识别规则：

- 用户明确指定某一页或某一模块：按 `single-page`
- 用户提供页面截图、线框图或明确要求按参考图复刻：`inputMode = image-reference`
- 其余文字需求：`inputMode = text-brief`
- 用户说“做一套 / 整套 / 完整 UI”：按 `full-suite`
- 用户说“只做核心流程”：按 `core-flow`
- 需求模糊但没有明确说只做单页：默认按 `full-suite`

后续约束：

- `source/site_plan.md` 里的页面列表必须来自 `source/page_scope.md`
- `source/site.json.pages[]` 不能超出本轮确定的页面范围
- `source/page_plans/` 只为本轮实际进入设计的页面生成

### 3. Plan Pages + Freeze Semantics

在写任何一个页面 HTML 前，先把页面规划冻结。

每页 `page_plan` 至少要有：

- 页面目标
- 主模块矩形预算
- 主 CTA
- 信息层级
- 允许使用的 canonical role
- 允许复用的 shared classes
- 必须继承的 `shared invariants`
- 本页独有的 `unique anchors`
- safe-area / bound 约束
- confidence
- fallback

这一阶段的核心原则：

1. 先冻结页面骨架，再填装饰。
2. 先冻结 canonical role，再写视觉细节。
3. 不要一边发明 role、一边写 HTML、一边猜颜色关系。

如果模型置信度低：

- 减少装饰节点
- 减少平行模块数量
- 收缩成更稳的 layout archetype
- 优先用已验证的 canonical role，不要自造平行 role 体系

### 4. Draft HTML

这一阶段先把页面结构、风格方向、模块层级拉开。

允许：

- 先做更有设计感的 HTML 第一版
- 先确定页面骨架和组件家族
- 先把标题条、面板、卡片、按钮等风格分叉做出来

但仍然必须保证：

- 设计空间严格等于 `source/site.json` 里的 `designWidth x designHeight`
- 允许竖屏或横屏；不要把竖屏页面塞进横屏壳里
- 页面根节点有 `data-ui-page`、`data-ui-name`、`data-ui-role`
- 关键交互节点有稳定命名
- 文本必须是真实 DOM 文本
- 不依赖 JS 动态拼 DOM

### 5. Normalize to Unity-safe HTML

这一阶段把 Draft 稿收敛到当前 Unity 落地端稳定支持的子集。

优先保留：

- 结构
- 布局
- 语义节点
- 关键视觉语言

统一降级顺序：

1. `special-effects`
2. `ornament-overdraw`
3. `shape-complexity`
4. `layout`

也就是说，先收浏览器特效，再收装饰，最后才允许改布局。

Normalize 时必须主动满足下面这些 Unity-safe authoring 规则，不要等 validate 报错后再回改：

1. 页面 HTML 内不要写 `<style>`；局部样式放进 `shared/widgets.css` 或 `theme.css`
2. 所有已创作节点都必须完全落在页面根画布内
3. primitive `button` 只能保留安全文本子结构
4. 复杂交互卡片拆成“视觉层 + 透明按钮覆盖层”
5. 不要把视觉 role 和 primitive element 强绑定
6. 不要把关键风格语言收成普通灰圆角面板

### 6. Validate + Compile + Theme Health Check

规范化完成后，必须继续跑本地工具链：

```powershell
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/generate_site_contract.py <SiteRoot> --write-self-check
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/validate_and_prepare_repair.py <SiteRoot>
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/compile_site_bundle.py <SiteRoot>
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/audit_compiled_theme_health.py <SiteRoot>
```

当前校验有两个严重级别：

- `error`：阻断 compile 的 contract / geometry 问题
- `warn`：不阻断 compile 的 style / browser-only 问题

`compile_report.json.downgrades[]` 会列出编译阶段自动降级或丢弃的样式。
关键视觉元素如果被意外降级，必须继续修，而不是只看 `pass`。

当校验失败时：

- 优先读取 `reports/repair_package/repair_summary.md`
- 按修复摘要修正 HTML/CSS
- 继续重跑，直到 `validation_report.json.status == pass`

正式可交付的判断标准：

- `reports/validation_report.json.status == pass`
- `reports/compile_report.json.status == pass`
- 根目录存在 `compiled_site_bundle.json`
- `reports/theme_health_report.json.status == pass` 或至少没有 theme fallback 警告

## 站点包目录结构

站点包根目录下只保留 3 个正式入口：

- `preview.html`
- `compiled_site_bundle.json`
- `任务报告.md`

其余内容统一收进子目录：

```text
YourSite/
  preview.html
  compiled_site_bundle.json
  任务报告.md
  source/
    site.json
    theme.css
    ui_contract.json
    ui_self_check_report.json
    pages/
    shared/
  reports/
    validation_report.json
    compile_report.json
    compile_repair_prompt.md
    repair_package/
    page_validation/
  snapshots/
    compiled_site.json
    compiled_pages/
    layout_snapshots/
```

站点包可以放在任何工作目录下，不要把路径写死成某个历史案例目录。

## 当前和 Unity 落地端对齐的能力

已经对齐：

- `compiled_site_bundle.json` 作为唯一正式 Unity 输入
- 页面 / 节点 / layout / visual / textStyle / motion 结构合同
- 纯色、单层线性渐变、程序化圆角、非均匀圆角、程序化边框、稳定阴影、发光
- `data-ui-shape`
- `data-ui-frame`
- `cut-corner / plate / banner`
- `opacity`（0..1）
- `transform: rotate(<deg>)`
- `animation: rotate/float/pulse` 预设
- `animation-delay`
- `flex-grow` / `flex-shrink` / `flex:1`
- `border-style: dashed`
- `-ai-translate-x` / `-ai-translate-y`
- 有明确语义的多层 `box-shadow`
- `overflow:hidden` 内容裁剪容器
- `radial-gradient` 的近似支持
- 显式 SVG 资产引用（icon、印章、圆环、旋转背景、复杂边框、复杂 plate）

部分对齐：

- `data-ui-motion` 合同已保留，Unity 落地端支持基础运行时动效
- 当前 skill 侧仍不建议把视觉成立建立在复杂动画 authoring 上

未对齐就不要假装支持：

- 任意浏览器 `background-image`（仅显式本地 SVG 资产引用除外）
- `clip-path`
- `mask`
- `mix-blend-mode`
- `filter`
- `backdrop-filter`
- `grid`
- `calc()`
- `flex-wrap`
- `conic-gradient`
- `repeating-linear-gradient`
- `repeating-radial-gradient`
- `position:fixed`
- `aspect-ratio`
- 复杂 `@keyframes`

## SVG 正式 authoring 规则

SVG 现在是正式支持的 authoring 路径，不再只是临时旁路。
它的职责是补足 Unity 程序化圆角、边框、简单 shape 难以稳定还原的视觉，不是替代所有基础控件。
它是增彩能力，不是强制步骤。页面规划里确实需要 icon、复杂边框、旋转背景或复杂 ornament 时再用；不需要就不要为了“用上 SVG”而额外制造资产。

优先使用 SVG 的场景：

1. `icon`、徽记、印章、圆环、法阵、复杂符号
2. 旋转背景、非对称装饰图形、复杂 ornament
3. 边框、外框、按钮底板、复杂 `plate`，且后续需要导入成 Sprite 或 Nine Slice
4. Unity 程序化路径难以保真的复杂轮廓、描边、装饰细节

不要使用 SVG 的场景：

1. 普通纯色 panel / button / roundrect，这类先走程序化表达
2. 需要运行时改单词、数值、标题的文本，不要烤进 SVG
3. 整页截图、整卡截图，或把布局和内容一起画死的大图
4. 依赖 `filter`、`mask`、`mix-blend-mode`、浏览器特有滤镜链的效果

强制写法：

- SVG 文件统一放在 `source/assets/*.svg`
- 页面节点通过显式资产宿主节点引用 SVG，不要把复杂 SVG 代码直接内联到页面主体结构里
- 资产宿主节点至少写 `data-ui-asset-id`、`data-ui-asset-type`、`data-ui-asset-usage`、`data-ui-asset-import`
- `data-ui-asset-import` 正式推荐只写 `sprite` 或 `nineslice`
- 需要切片的 frame / border / button skin 必须补 `data-ui-asset-slice`
- 需要稳定栅格尺寸的 icon / badge / frame 必须补 `data-ui-asset-width`、`data-ui-asset-height`
- 不希望被 Unity `Image.color` 继承污染时，补 `data-ui-asset-tint="none"`
- HTML 预览阶段直接引用 SVG；Skill 只负责编译资产引用；最终 `SVG -> PNG / Sprite / Nine Slice` 由 Unity Editor 扩展处理
- Unity 导入后生成的 PNG/Sprite 统一落到 `Assets/AIToUGUI_Generated/<siteId>/Sprites`；不要假设站点包根目录会被 Skill 直接写出一个 `sprites/`

## 当前最重要的 authoring 约束

1. 结构和命名从第一稿就要稳定
2. 页面根节点必须锁在 `site.json.designWidth x site.json.designHeight`
3. 所有按钮都必须有 `data-ui-element`
4. 重要按钮、卡片、标题、资源块都要显式尺寸
5. `data-ui-shape` 表达轮廓拓扑，不要把普通圆角矩形硬标成异形
6. 边框颜色必须显式写在 `border` 或稳定语义上
7. 复杂交互卡片优先使用“视觉卡片 + 透明按钮覆盖层”
8. 文本按钮优先用显式定位文本节点，不要靠文本兜底自然撑开
9. 不要默认新建 `button` 变体名，先确认当前 primitive 合同是否允许
10. 优先复用 canonical role / text role，不要轻易自造平行 role 体系
11. `pass` 不是唯一目标；compile 后 theme 回退默认值同样算失败

## 禁止事项

1. 不要手写 `compiled_site_bundle.json`
2. 不要跳过 `validate` 直接 `compile`
3. 不要把 Draft HTML 直接送进 Unity
4. 不要把 Workbench 当成正式交付链路
5. 不要为了风格发散破坏 `data-ui-name`、`data-ui-role`、`data-ui-element`
6. 不要把旧模板、旧示例、旧案例当成构图起点
