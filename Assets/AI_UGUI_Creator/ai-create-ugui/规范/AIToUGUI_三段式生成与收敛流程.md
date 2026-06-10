# AIToUGUI 三段式生成与收敛流程

这份文档定义新的主流程：

1. `Understand Style + Freeze Intent`
2. `Recognize UI Scope + Freeze Page Set`
3. `Plan Pages + Freeze Semantics`
4. `Draft HTML`
5. `Normalize to Unity-safe HTML`
6. `Validate + Compile + Theme Health`

目标不是让第一稿就背满 Unity 约束，而是先让风格成立、先把页面规划冻结，再逐步收敛。

## 阶段一：Understand Style + Freeze Intent

### 目标

先解决这些问题：

- 题材是否理解正确
- 配色关系是否清楚
- 哪些视觉语义必须保住
- 哪些装饰在低置信度时可以主动删减

### 建议产物

- `draft_visual_intent.md`

### 低置信度时的处理

- 先删装饰
- 再减并行模块
- 再减 motion
- 不要先改主题关系

## 阶段二：Recognize UI Scope + Freeze Page Set

### 目标

在进入页面规划前，先判断这次任务到底是单页、核心流程，还是整套 UI。

### 建议产物

- `page_scope.md`

### 这一阶段至少要冻结

- 游戏类型
- 运行模式
- 产品定位
- `scopeMode`
- 页面结构树
- 页面清单
- 优先级
- 最小页面集
- 完整页面集
- 本轮实际进入设计的页面范围

### 关键规则

- 用户明确指定单页或单模块：`single-page`
- 用户要求“做一套 UI”或某类型游戏 UI：`full-suite`
- 用户明确说只做核心流程：`core-flow`
- 需求模糊但未限定单页：默认 `full-suite`

后续 `site_plan.md`、`site.json.pages[]`、`page_plans/` 必须以这一步的结果为准。

## 阶段三：Plan Pages + Freeze Semantics

### 目标

把多页面复杂度先收口成稳定的 planning artifact，再让模型落 HTML。

### 建议产物

- `site_plan.md`
- `page_plans/<pageId>.md`

### 这一阶段至少要冻结

- 页面列表
- 每页 layout budget
- 每页 main CTA
- approved canonical role
- confidence / fallback

## 阶段四：Draft HTML

### 目标

先解决这些问题：

- 页面像不像一个真正有设计语言的游戏 UI
- 布局骨架是否有辨识度
- 模块节奏是否成立
- 组件家族是否真的和旧风格不同

### 允许做什么

- 先按题材和美术意图发散
- 先做更强的装饰语言和构图
- 先用更明显的标题条、贴纸、腰线、票券、笔记页、海报板
- 先做更有风格的组件家族

### 仍然必须保留的最小硬约束

- `site.json.designWidth x site.json.designHeight`
- 页面根节点稳定命名
- 所有交互节点稳定命名
- 运行时会读取的节点稳定命名
- 关键节点真实 DOM 文本
- 不依赖 JS 成立

### Draft 产物建议

- `site.json`
- `theme.css`
- `shared/widgets.css`
- `pages/*.html`
- 可选的 `draft_notes.md` 或 `draft_visual_intent.md`

这一阶段的页面可以还没有完全通过 validate。

## 阶段五：Normalize to Unity-safe HTML

### 目标

把 Draft 稿转成当前能力池可稳定消费的最终 HTML/CSS。

### 优先级

必须按这个顺序保：

1. 结构
2. 布局
3. 语义节点
4. 关键文案
5. 颜色 / 圆角 / 描边 / 阴影 / 渐变
6. 装饰细节
7. 特殊效果

### 统一降级顺序

1. `special-effects`
2. `ornament-overdraw`
3. `shape-complexity`
4. `layout`

### Normalize 时要特别收的点

- 重复模块统一家族
- 背包格子统一
- 英雄候选卡统一
- 技能按钮按组统一
- 设置页条目统一
- 异形优先留给标题条、主 CTA、特色大卡、页头、侧栏头

### 完成后立刻执行

```powershell
python PythonTool/generate_site_contract.py <SiteRoot> --write-self-check
```

这一步把 Normalize 后的 HTML 自动反推出：

- `ui_contract.json`
- `ui_self_check_report.json`

## 阶段六：Validate + Compile + Theme Health

对 Normalize 后的正式包执行：

```powershell
python PythonTool/validate_and_prepare_repair.py <SiteRoot>
python PythonTool/compile_site_bundle.py <SiteRoot>
python PythonTool/audit_compiled_theme_health.py <SiteRoot>
```

只有当：

- validate 通过
- compile 通过
- theme health 没有 fallback 警告
- 根目录存在 `compiled_site_bundle.json`

才允许进入 Unity。

## 不要做的事

- 不要把 Draft 稿直接送进 Unity
- 不要在第一稿就把所有 Unity 子集限制塞给模型
- 不要让风格发散破坏命名和交互语义
- 不要用特效堆叠代替布局和模块设计
- 不要把高重复模块做成“每个实例一个轮廓”

## 适用场景

这个流程特别适合：

- 需要明显风格差异的同类游戏 UI
- 便宜模型或中文模型更容易被强约束压死的场景
- 先追求设计感，再追求 Unity 可落地的链路
