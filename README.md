# AITOUGUI Experimental

**简体中文 | [English](README_EN.md)**

> 当前仓库是一个实验性版本，仍在迭代中，功能与资源都**不完整**，不代表正式版本的最终能力与质量。

`AITOUGUI` 是一个面向 Unity UGUI 的 AI UI 落地实验项目。  
这个仓库包含两部分核心内容：

- 一套用于生成站点包 / 页面包的 AI 工作流与 Python 工具链
- 一个可在 Unity 内解析 `compiled_site_bundle.json` 并生成预览层级的 `AIToUGUI Lite` 插件
- 一个额外挂载在仓库根目录下、用于承载正式链路能力的 `AIToUGUI/` 插件目录

## 项目定位

这个仓库公开的是一个**实验性的、未完整发布的 Lite 版本**，用于展示整体思路、数据结构和基础落地链路。

正式版本在内部实际落地时，依赖了一些**付费插件**和更完整的运行时能力，所以这里提供的 Unity 插件不是完整商用版，而是一个去依赖、可独立运行的精简版本。

因此需要明确：

- 这里的 `AIToUGUI Lite` 主要用于**解析与预览**
- 它保留了基础 UGUI/TMP 搭建流程
- 但正式版中的一部分高级视觉效果、适配层和动画能力，在这个仓库里**没有一起放出**
- 根目录新增的 `AIToUGUI/` 只是正式链路插件的公开骨架，**不包含所需付费插件**
- 当前仓库仍然是**实验性公开版本**，并不是一个完整、稳定、面向正式交付的发布包

## Lite 版本说明

Lite 版本的目标不是 1:1 还原正式版效果，而是提供一个不依赖付费资源的最小可用闭环：

1. AI 侧生成站点包
2. Unity 侧读取 `compiled_site_bundle.json`
3. 在场景中的预览挂点下生成 UGUI/TMP 预览层级
4. 用于检查页面结构、文本、基础布局、基础控件和部分视觉信息

从现有测试和代码可以确认，Lite 版本会主动避开正式版依赖的若干能力，包括但不限于：

- 高级阴影与发光效果
- 形状适配与更复杂的视觉包边
- 动画绑定与页面入场效果
- 正式版里的部分页面级运行时组件
- 某些依赖第三方付费插件的表现层能力

这意味着：

- 页面**结构**通常可以看
- 页面**基础布局**通常可以看
- 页面**文字与基础控件**通常可以看
- 页面**最终质感和完整特效**不能等同于正式版

如果你是为了评估工作流、数据协议、Unity 落地方式，这个仓库是足够的。  
如果你期待直接复现正式商用品质，请把它理解成一个能力受限的演示版。

## AIToUGUI 插件说明

仓库根目录下新增了一个外围插件目录：

- `H:\AIProject\AITOUGUI\AIToUGUI`

这个目录对应的是更接近正式链路的 `AIToUGUI` 插件骨架，用于展示更完整的运行时、适配器和编辑器组织方式。  
但当前 GitHub 仓库**没有附带其依赖的付费插件**，所以它默认不是开箱即用状态。

请先明确以下几点：

- 如果你只想体验公开仓库里的最小闭环，请优先使用 `AIToUGUI Lite`
- 如果你要尝试 `AIToUGUI/` 这套插件，需要先把你自己合法持有的付费插件放入 `H:\AIProject\AITOUGUI\AIToUGUI\Plugins`
- 当前仓库里已经放入的 `PrimeTween`、`WindinatorLite` 仅代表公开版本中可一并提供的部分依赖或裁剪版本，**不等于完整正式依赖集**
- 在缺少付费插件的情况下，`AIToUGUI/` 相关功能没有效果、Unity 编译报错、Inspector 缺脚本、材质或运行时行为异常，均属于预期现象
- 因缺失付费插件引发的集成问题、编译错误或兼容性问题，需要由使用者根据自己导入的版本**自行修复**

### 付费插件放置位置

请将你自行购买并导出的相关付费插件内容放到：

- `H:\AIProject\AITOUGUI\AIToUGUI\Plugins`

导入后如果仍然存在命名空间、程序集定义、脚本执行顺序、Shader、材质或版本兼容问题，请自行调整。  
公开仓库不会附带这些付费依赖，也不会保证不同来源版本之间的直接兼容。

### 使用介绍视频

- [AI 生成 Unity UGUI 的 UI 界面也能 1:1 还原设计稿效果](https://www.bilibili.com/video/BV1Gi7L61EQU/?share_source=copy_web&vd_source=0b0af9c6e554b56c26123d46e123e37f)

## 当前包含内容

### 1. AI 工作流与工具链

目录：`Assets/AI_UGUI_Creator/ai-create-ugui/`

包含内容：

- 生成规范
- 检查清单
- 使用说明
- 站点包约束文档
- Python 工具链

这些内容负责把 UI 规划、页面 HTML/CSS 表达、中间结构和自检流程收敛为 Unity 可消费的站点包结果。

相关入口文档：

- [Assets/AI_UGUI_Creator/ai-create-ugui/使用指南.md](H:/AIProject/AITOUGUI/Assets/AI_UGUI_Creator/ai-create-ugui/使用指南.md)
- [Assets/AI_UGUI_Creator/ai-create-ugui/SKILL.md](H:/AIProject/AITOUGUI/Assets/AI_UGUI_Creator/ai-create-ugui/SKILL.md)

### 2. Unity Lite 插件

目录：`Assets/AI_UGUI_Creator/AIToUGUI_LITE/`

包含内容：

- `Runtime/`：Lite 数据结构、解析器、预览挂点组件
- `Editor/`：Unity 编辑器窗口与预览构建逻辑
- `Tests/`：基础编辑器测试

Unity 菜单入口：

- `Tools/AIToUGUI/Lite Studio`

Lite Studio 的主要用途：

- 读取 `compiled_site_bundle.json`
- 解析多页面 bundle
- 选择单页或整套页面进行预览生成
- 将预览内容挂到场景中的 `AIToUGUILitePreviewMount` 下

### 3. AIToUGUI 外围插件骨架

目录：`AIToUGUI/`

包含内容：

- `Core/`：运行时、Authoring、UI System 与适配器骨架
- `Editor/`：站点包导入、Studio 窗口、代码生成等编辑器逻辑
- `Plugins/`：第三方依赖放置区
- `Test/`：部分测试和示例场景

使用前提：

- 默认状态下它**不是可直接运行的完整商业版**
- 需要你自行补齐放在 `AIToUGUI/Plugins/` 下的付费插件
- 导入后的编译或兼容问题需要自行排查和修复

## 示例内容

目录：`Assets/Examples/`

当前仓库里包含了一些示例站点包与产物，用来展示不同 UI 风格和页面结构，例如：

- `FPSLoadout_SkillGenerated`
- `FPSOperatorSemanticUI`
- `SunnyPopTrails_PortraitSuite`

这些示例通常包含：

- `source/` 原始页面与规划文件
- `compiled_site_bundle.json` 编译后的站点包
- `preview.html` HTML 预览
- `reports/` 检查与修复报告
- `snapshots/` 编译快照

## 适用场景

这个仓库更适合下面几类用途：

- 研究 AI 到 Unity UGUI 的中间协议设计
- 阅读一个从页面描述到 Unity 预览生成的简化实现
- 为自己的正式版工具搭建一个 Lite 原型
- 参考多页面 bundle 的解析与预览流程
- 检查如何把 HTML/CSS 风格表达约束到 Unity 可落地的数据结构

## 运行环境

- Unity `2022.3.62f2c1`
- 使用到的主要包包括：
  - `com.unity.ugui`
  - `com.unity.textmeshpro`
  - `com.unity.vectorgraphics`
  - `com.unity.test-framework`

## 快速开始

1. 用 Unity `2022.3.62f2c1` 打开项目
2. 等待 Package 导入完成
3. 打开任意测试场景，或自己准备一个带 `Canvas` 的场景
4. 在场景中准备一个挂有 `AIToUGUILitePreviewMount` 的对象
5. 打开 `Tools/AIToUGUI/Lite Studio`
6. 指定一个 `compiled_site_bundle.json`
7. 选择 `Parse JSON` 后再执行 `Parse All` 或 `Parse Selected`

## 仓库结构

- `AIToUGUI/`：外围完整插件骨架，依赖使用者自行补齐付费插件
- `Assets/AI_UGUI_Creator/AIToUGUI_LITE/`：Unity Lite 插件
- `Assets/AI_UGUI_Creator/ai-create-ugui/`：AI 工作流、规范与 Python 工具
- `Assets/Examples/`：示例站点包与预览产物
- `Assets/Scenes/`：Unity 场景
- `Packages/`：Unity 包依赖
- `ProjectSettings/`：项目配置

## 已知边界

- 这不是正式商用版插件
- 这不是完整特效还原版
- 示例中的视觉结果不能代表正式版最终表现
- 某些正式版依赖的组件、表现器和第三方插件能力已被移除
- `AIToUGUI/` 目录下缺失的付费插件不会随仓库分发
- 由于依赖未补齐导致的 `AIToUGUI/` 报错，需要使用者自行完成依赖导入与修复

如果你在 GitHub 上看到“效果不如正式版本”，这是预期内的。  
原因不是数据链路中断，而是这个公开仓库有意保留在 Lite 可分发范围内。

## 补充说明

- 仓库已经排除了 Unity 本地缓存目录，如 `Library/`、`Logs/`、`UserSettings/`
- 根目录是仓库说明；更细的模块文档在 `Assets/AI_UGUI_Creator/` 下
- 如果后续要公开对外展示，建议再补一轮截图、About 描述和 Topics
