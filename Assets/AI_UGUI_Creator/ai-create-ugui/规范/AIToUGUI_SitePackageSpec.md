# AIToUGUI Site Package Spec

这份文档定义当前正式站点包目录、正式入口，以及进入 Unity 前后的产物边界。

## 1. 正式目录结构

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
      main-menu.html
      main-lobby.html
    shared/
      widgets.css
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

## 2. 根目录只保留什么

根目录只保留这 3 个正式入口：

- `preview.html`
- `compiled_site_bundle.json`
- `任务报告.md`

用户第一眼只需要看到这 3 个文件。
不要再把 `site.json`、`theme.css`、`ui_contract.json`、`repair_cycle/`、`bundle_compile/` 直接摊在根目录。

## 3. source/ 的职责

`source/` 是 AI authoring 源包。

必须包含：

- `site.json`
- `theme.css`
- `shared/widgets.css`
- 至少一个 `pages/*.html`

建议包含：

- `assets/`（如使用 SVG 资产则为必需）
- `ui_contract.json`
- `ui_self_check_report.json`
- `draft_visual_intent.md`
- `page_scope.md`
- `site_plan.md`
- `page_plans/`

Unity 不直接吃 `source/`，但 validate / compile 会消费它。

其中：

- `page_scope.md` 负责冻结页面范围
- `site_plan.md` 负责把已确认页面集合转成站点级规划
- `page_plans/` 负责把单页布局和语义预算冻结下来
- `assets/` 负责承载本地 SVG 原始资源；页面中引用时统一使用相对 `source/` 的路径

## 4. reports/ 的职责

`reports/` 存放所有校验、修复、编译报告：

- `validation_report.json`
- `compile_report.json`
- `compile_repair_prompt.md`
- `repair_package/`
- `page_validation/`

这些文件是过程产物，不是最终 Unity 导入入口。

## 5. snapshots/ 的职责

`snapshots/` 存放中间快照：

- `compiled_site.json`
- `compiled_pages/*.compiled_page.json`
- `layout_snapshots/*.layout_snapshot.json`

它们用于排查和复盘，不作为最终交付入口。

## 6. Unity 正式入口

当前 Unity 正式入口只有：

- 根目录下的 `compiled_site_bundle.json`

Unity 落地端不应该依赖 `source/`、`reports/` 或 `snapshots/` 才能导入。

## 7. site.json

`source/site.json` 是源包清单，至少包含：

```json
{
  "siteId": "guardian_ui",
  "displayName": "Guardian UI",
  "designWidth": 1920,
  "designHeight": 1080,
  "themeCss": "theme.css",
  "sharedStyles": [
    "shared/widgets.css"
  ],
  "prefabOutputRoot": "Assets/Prefabs/UI/Generated",
  "metadataOutputRoot": "Assets/DataConfig/UI/Generated",
  "pages": [
    {
      "pageId": "main-menu",
      "displayName": "Main Menu",
      "html": "pages/main-menu.html",
      "prefabName": "MainMenuPanel",
      "targetLayer": "Normal",
      "localStyles": []
    }
  ]
}
```

注意：

- `themeCss` 和 `sharedStyles` 都是相对 `source/` 的路径
- `pages[].html` 也是相对 `source/` 的路径

## 8. preview.html

`preview.html` 是给人看的站点入口，不是 Unity 消费文件。

它的职责只有两个：

- 让人快速打开页面预览
- 把页面入口列清楚

不要把复杂逻辑塞进这里。

## 9. compiled_site_bundle.json

这是本地工具编译后的正式 Unity 输入。

至少包含：

- `site`
- `theme`
- `assets`
- `downgrades`
- `pages`

其中：

- 顶层不再单独暴露 `nodes / layout / visual / textStyle / motion`
- 这些编译结果按页面收纳在 `pages[].root` 及其子节点结构里
- 单页对象至少包含 `pageId / runtimePageId / displayName / prefabName / targetLayer / logicalPath / root`
- `assets` 用来收纳显式 SVG / image 资产引用及其导入元数据，供 Unity Editor 导入侧继续处理

不要手写这个文件。

## 10. 路径独立性

站点包可以位于任何工作目录。
不要把工具链、文档或提示词写死成某个历史案例路径，也不要把“案例目录”当作正式输入前提。
