# AIToUGUI Plan-First 风格与页面规划规范

这份文档专门解决弱模型和中文模型在多页面 UI 生成中常见的两个问题：

1. 第一轮 HTML 直接爆出大量 contract / geometry / role 错误
2. repair 后虽然 pass，但风格被修成普通页面

## 核心原则

不要让模型同时承担这 4 件事：

1. 理解题材
2. 设计配色关系
3. 发明页面结构
4. 落低层 HTML / role / size 细节

正确顺序必须是：

1. 冻结风格意图
2. 冻结页面范围
3. 冻结页面规划
4. 冻结 canonical semantics
5. 再写 HTML

## 必备 planning artifact

### 1. `draft_visual_intent.md`

至少写清楚：

- styleAxes
- 题材关键词
- 禁止项
- 配色关系
- normalizePriority
- 低置信度 fallback

### 2. `page_scope.md`

在 `site_plan.md` 之前，先生成 `page_scope.md`。

至少写清楚：

- `scopeMode`
- `inputMode`
- 游戏类型
- 运行模式
- 产品定位
- 页面结构树
- 页面清单
- 每页优先级
- 页面来源
- 最小页面集
- 完整页面集
- 本轮进入设计的页面范围

规则：

- 用户明确指定某一页：按 `single-page`
- 用户提供页面截图、线框图或明确要求按图复刻：`inputMode = image-reference`
- 其余文字描述：`inputMode = text-brief`
- 用户说整套 UI 或某类游戏 UI：按 `full-suite`
- 用户只要核心流程：按 `core-flow`
- 需求模糊但没有明确说单页：默认按 `full-suite`

### 3. `site_plan.md`

至少写清楚：

- 页面列表与优先级
- 页面间风格关系
- 共享组件家族
- `shared invariants`
- `page delta matrix`
- canonical role 策略
- 哪些页面必须保住强风格
- 哪些页面允许保守收口

其中：

- `shared invariants` 用来冻结整套 UI 必须共享的骨架语言、组件家族、语义策略和配色关系
- `page delta matrix` 用来冻结每页与其他页的核心差异，防止多页最后只剩“换坐标”

### 4. `page_plans/<pageId>.md`

每页至少写清楚：

- page goal
- confidence
- fallback
- layout budget
- main CTA
- key named nodes
- approved roles
- inherited invariants
- unique anchors
- forbidden drift

## 页面规划时必须冻结什么

### 风格关系

不是只写“月白、青玉、墨青、鎏金”，而是要写：

- 月白是不是主面积
- 墨青是不是只做锚点
- 鎏金是不是只做细边和标题
- 青玉是不是负责材质过渡而不是整块涂满

### 语义关系

优先使用 canonical role：

- `window/main`
- `panel/hero`
- `panel/section`
- `panel/resource-bar`
- `card/info`
- `card/entry`
- `card/slot`
- `card/accent`
- `button/primary`
- `button/secondary`
- `button/gold`
- `button/ghost`
- `text/title`
- `text/body`
- `text/label`
- `text/value`
- `text/gold`

除非明确必要，不要新造一套平行 role 体系。

### 布局关系

先写大矩形预算，再写内部细节。

前提是页面集合已经在 `page_scope.md` 中被冻结。

错误做法：

- 一边发明模块，一边猜位置
- 一边猜位置，一边修 role
- 一边修 role，一边再调色

### 整套 UI 的一致性与差异控制

`full-suite` 模式下，不允许只做“同一页骨架的平移复制”。

最低要求：

1. 每页至少写出 3 个清晰差异点
2. 至少 1 个差异点必须来自页面骨架、主导模块或交互节奏
3. 不允许把差异全部压成配色、标题文案或局部装饰
4. 不允许所有页面都复用同一组大卡片布局，只换位置和文案

如果模型难以同时维持“一致性”和“页间差异”，先缩成 `core-flow`，不要硬做完整 `full-suite`

## 低置信度 fallback 规范

如果模型在题材、结构、能力池映射上把握不稳，必须主动 fallback：

1. 先删过量装饰
2. 再删重复装饰节点
3. 再降低模块并行数
4. 再降低 motion
5. 最后才允许缩页面范围

不要在低置信度时继续增加：

- 自定义 role
- 大量 banner / plate / seal
- 多层主题色
- 高密度页面入口

## 验收标准

Plan-first 做得好，通常会出现这些结果：

- 第一轮 validator 错误显著减少
- repair 更集中在局部 HTML，而不是全站语义回修
- compile 通过后，theme 不会回退到默认值
- 页面之间更像同一套 UI，而不是 5 张能过编译的散页
- `site.json.pages[]` 不会出现临时加页、漏页、错页
