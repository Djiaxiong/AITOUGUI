# AIToUGUI Contract 与自检规范

这份文档定义两份作者态元数据：

- `ui_contract.json`
- `ui_self_check_report.json`

它们不是草图阶段的主输入，而是 **Normalize 完成后的结构快照和校验快照**。也就是说：

- `Draft HTML` 阶段可以先自由设计
- `Normalize` 完成后，再生成和修订 contract
- 进入 Unity 前，contract 必须和最终 HTML 对齐

## 为什么需要 Contract

如果只写 HTML/CSS，常见问题是：

- 浏览器里看着成立，Unity 里失真
- 关键节点命名漂移
- 交互节点没有稳定语义
- 文本和容器尺寸依赖浏览器天然行为
- 后续修复时无法快速锁定节点

`ui_contract.json` 的作用就是把 **Normalize 后的真结构** 固定下来。

## 推荐工作流

先做：

1. `Draft HTML`
2. `Normalize to Unity-safe HTML`

再做：

```powershell
python PythonTool/generate_site_contract.py <SiteRoot> --write-self-check
```

之后如果需要，再人工微调 `ui_contract.json`。

不要在 Draft 阶段手工硬写完整 contract。

## ui_contract.json 顶层结构

- `contractVersion`
- `workflow`
- `site`
- `pages`
- `usedRoles`
- `usedClasses`
- `notes`

## workflow

推荐包含：

- `mode`
- `currentStage`
- `normalizePolicy`

建议固定为：

- `mode = "draft-first"`
- `currentStage = "normalized"`

`normalizePolicy` 推荐记录：

- `preserve`
- `degradeOrder`
- `supportedSelectors`
- `fontBuckets`
- `avoidEffects`

## page 结构

每个 `pages[*]` 至少包含：

- `pageId`
- `displayName`
- `html`
- `prefabName`
- `targetLayer`
- `visualLanguage`
- `root`
- `namedNodes`
- `relativeSizeNodes`
- `templateSizeNodes`
- `notes`

## visualLanguage

推荐显式记录：

- `layoutArchetype`
- `shapeLanguage`
- `frameLanguage`
- `ornamentLanguage`

这里记录的是 **Normalize 后落地版本** 的风格归属，不是 Draft 阶段所有未收敛的视觉幻想。

## root / namedNodes 结构

常用字段：

- `name`
- `tag`
- `role`
- `elementId`
- `variantId`
- `shapeId`
- `frameId`
- `classes`
- `text`
- `sizePolicy`
- `style`

## sizePolicy

只允许：

- `fixed`
- `relative`
- `auto-text`

含义：

- `fixed`
  关键节点，必须有明确像素尺寸
- `relative`
  少量真实需要相对尺寸的节点，例如进度填充
- `auto-text`
  允许文本高度由内容自然展开，但仍然要求命名和语义稳定

## relativeSizeNodes

只放真正必须用百分比的节点，例如：

- 进度条填充
- 明确受父容器约束的内容填充层

每项至少包含：

- `name`
- `reason`
- `parentConstraint`
- `style`

## templateSizeNodes

只有节点写了：

```html
data-ui-template-size="true"
```

才允许出现在 `templateSizeNodes` 中。

## Contract 编写原则

1. `site.json` 是页面清单真源
2. `ui_contract.json` 负责记录 Normalize 后的页面结构快照
3. Contract 不应描述 Draft 阶段已经被放弃的视觉分支
4. 页面根、交互节点、会被脚本读取的文本/图片/容器节点必须进入 contract
5. 高重复模块应按统一家族收敛，不要在 contract 中鼓励“同组元素逐项异形”

## ui_self_check_report.json

这份文件只表达一件事：**当前站点包是否已通过作者态自检**。

顶层字段：

- `reportVersion`
- `siteId`
- `status`
- `summary`
- `violations`
- `pageReports`
- `notes`

每个 `pageReports[*]` 常用字段：

- `pageId`
- `unsupportedTags`
- `unsupportedAttributes`
- `unsupportedSelectors`
- `unsupportedProperties`
- `unsupportedValuePatterns`
- `missingRequiredSizeNodes`
- `missingNameNodes`
- `missingRoleNodes`
- `undefinedClasses`
- `undefinedRoles`
- `relativeSizeNodes`
- `templateSizeNodes`
- `browserOnlyPatterns`
- `warnings`

## 通过标准

只有同时满足下面条件，`status` 才能为 `pass`：

- 顶层 `violations` 为空
- 每页 `unsupported*` 为空
- 每页 `missingRequiredSizeNodes` 为空
- 每页 `missingNameNodes` 为空
- 每页 `missingRoleNodes` 为空
- 每页 `undefinedClasses` 为空
- 每页 `undefinedRoles` 为空
- 每页 `browserOnlyPatterns` 为空

建议在 `notes` 里补充这三类作者态自检结论，便于 repair 回合保持口径一致：

- 当前真正用于 Unity 的选择器范围
- 当前使用的字体桶
- 当前已主动避开的浏览器特效

## 正式闭环

作者态自检不是正式交付。正式闭环必须继续跑：

```powershell
python PythonTool/validate_and_prepare_repair.py <SiteRoot>
python PythonTool/compile_site_bundle.py <SiteRoot>
```

规则：

1. `validation_report.json.status != pass` 时，不允许进 Unity
2. `compile_report.json.status != pass` 时，不允许进 Unity
3. 没有 `compiled_site_bundle.json` 时，不算交付完成

## 修复原则

当验证失败时：

1. 先看 `validate_and_prepare_repair.py` 打印出来的 repair digest
2. 优先读 `repair_cycle/repair_package/repair_summary.md` 或 `repair_prompt.md`
3. 只有摘要不够时，才回退读取 `validation_report.json` 或 `compile_report.json`
4. 锁定具体 page 和 node
5. 优先修 Normalize 后的 HTML/CSS
6. 再重新生成 contract/self-check
7. 再重跑 validate / compile

不要在 repair 回合整站推翻重做。
