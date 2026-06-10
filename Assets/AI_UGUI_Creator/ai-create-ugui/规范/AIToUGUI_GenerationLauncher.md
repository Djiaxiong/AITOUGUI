# AIToUGUI Generation Launcher

你的任务不是自由写网页，而是生成一套能通过本地工具链校验，并最终进入 Unity 落地端的 AIToUGUI 站点包。

这套流程现在明确采用规则驱动，不采用固定模板驱动。
不要从旧示例、旧案例、旧页面壳开始，只能从当前需求冻结风格和语义。

## 当前正式目标

1. 明确用户需求
2. 冻结风格意图
3. 识别 UI 范围并冻结页面集合
4. 完成页面规划和语义冻结
5. 生成 Draft HTML
6. 收敛为 Unity-safe HTML
7. 通过本地 Validate
8. 通过本地 Compile
9. 通过 theme health check
10. 交付根目录下的 `compiled_site_bundle.json`

当前不再把 Workbench 当成正式链路的一部分。

## 开始前先读

1. `规范/UI风格参考.md`
2. `规范/游戏UI页面识别标准文档.md`
3. `规范/AIToUGUI_形态语言与风格分叉规范.md`
4. `规范/AIToUGUI_SitePackageSpec.md`
5. `规范/AIToUGUI_能力池与当前上限.md`
6. `规范/AIToUGUI_Contract与自检规范.md`
7. `规范/AIToUGUI_CSS与HTML实现细则.md`

## 当前硬约束

1. 页面设计空间必须严格等于 `source/site.json` 的 `designWidth x designHeight`
2. 页面根节点必须有 `data-ui-page`、`data-ui-name`、`data-ui-role`
3. 关键交互节点必须有稳定 `data-ui-name`
4. 所有按钮都必须带 `data-ui-element`
5. 不要手写 `compiled_site_bundle.json`
6. 不要跳过 `Validate` 直接 `Compile`
7. 页面内不要写 `<style>`，局部样式必须外置
8. 所有已创作节点必须完全位于页面根画布内，不允许负坐标和越界装饰
9. primitive `button` 只允许安全文本子结构；复杂卡片交互拆成视觉层 + 透明按钮层
10. 不要假设所有视觉按钮都能直接映射为新的 primitive variant；必要时分离 `data-ui-role` 和 `data-ui-element`
11. 不要把旧模板、旧示例、旧案例当成构图起点

## 当前正式目录

```text
YourSite/
  preview.html
  compiled_site_bundle.json
  任务报告.md
  source/
  reports/
  snapshots/
```

站点包可以位于任何工作目录，不要把路径写死成某个历史案例目录。

## 允许表达的重点

- 页面骨架差异
- 标题条和面板家族差异
- 按钮家族差异
- 卡片家族差异
- `data-ui-shape` / `data-ui-frame` / glow 语义

## 当前不要依赖

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
- 复杂 `@keyframes`

## 当前强制写法

- 先写 `draft_visual_intent.md` 和页面规划，再写 HTML
- 整套 UI 或模糊需求时，先写 `page_scope.md`，再写 `site_plan.md`
- 单页任务也要保留 `page_scope.md`，但 `scopeMode` 写成 `single-page`
- 先按 Unity-safe authoring 生成，不要先做 browser-first 再指望 normalize 挽救
- 标题、卡片、资源块、CTA 一律显式尺寸
- 竖屏项目就按真实竖屏尺寸写，不要套在横屏舞台里
- 复杂入口卡片优先做成非 primitive 视觉卡，再用按钮覆盖点击区
- 外溢云气、法阵、光环要在画布内完成，不允许越界后依赖裁切
- 优先复用 canonical role / text role，不要自造平行 role 体系
- `site.json.pages[]` 必须来自 `page_scope.md` 已冻结的页面范围
- icon、旋转背景、复杂边框、复杂 plate 可以走显式 SVG 资产路径
- 使用 SVG 时，文件统一放 `source/assets/`，宿主节点补齐 `data-ui-asset-*` 元数据

## 弱模型保守策略

如果模型对题材风格或页面结构把握不稳，必须主动 fallback：

1. 先减少装饰密度
2. 再减少页面并行模块数量
3. 再减少 motion
4. 最后才减少页面数量

不要在低置信度时硬做“满配 5 页复杂系统 + 自定义语义 + 高装饰密度”。

## 正式命令

```powershell
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/generate_site_contract.py <SiteRoot> --write-self-check
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/validate_and_prepare_repair.py <SiteRoot>
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/compile_site_bundle.py <SiteRoot>
python Assets/AI_UGUI_Creator/ai-create-ugui/PythonTool/audit_compiled_theme_health.py <SiteRoot>
```

## 通过标准

- `reports/validation_report.json.status == pass`
- `reports/compile_report.json.status == pass`
- 根目录存在 `compiled_site_bundle.json`
- `reports/theme_health_report.json.status == pass` 或没有关键 fallback
