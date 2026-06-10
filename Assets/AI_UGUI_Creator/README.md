# AI_UGUI_Creator

`AI_UGUI_Creator` 是当前准备对外带出的完整目录。

它包含三块对外带出内容，外加一份仅留在项目内的实现文档：

- `ai-create-ugui/`
  - AI 侧工作流、规范文档、检查清单和 Python 工具链（Skill 主体）
- `AIToUGUI/`
  - Unity 侧导入、预览、烘焙、Prefab 导出和运行时承接
- `Samples/`
  - 代表性展示样本（FPS、Casual）
- `Doc/`（仅项目内保留，不随包带出）
  - 总说明、Skill 实现说明、Unity 实现说明等实现细节文档

## 当前发布立场

当前版本已经明确收口：

1. Unity 侧是稳定落地层
   - 不再把主要精力放在继续给 Unity 增加隐式兜底修设计
2. Skill 侧是最后一轮主要补强层
   - 重点放在规划、约束、校验、修复、编译闭环
3. 正式目标模型是中上能力模型
   - 低能力模型不再作为发布适配范围

## 当前正式支持模式

1. `Single Page Text Mode`
2. `Single Page Image Mode`
3. `Full Suite Mode`

其中 `core-flow` 继续保留，但它是整套 UI 难度过高时的收缩交付版本，不再作为单独的主要产品模式。

## 近期新增

正式版现在已经把 SVG 资产链路纳入标准流程：

- Skill 可显式 authoring 本地 `source/assets/*.svg`
- Unity Studio 可预处理 SVG，并按 `sprite / nineslice` 导入
- 栅格化产物统一隔离到 `Assets/AIToUGUI_Generated/<siteId>/Sprites`
- 推荐用于 icon、复杂边框、旋转背景、复杂 plate 和 ornament
- 普通纯色 panel / button / roundrect 仍优先走程序化表达，不强行上 SVG

## 发布前最后一轮工作

发布前不再无限补强，而是按下面顺序收口：

1. 完成最后一轮 Skill 补强
2. 通过发布前核查
3. 生成代表性展示样本
4. 完成文档与 README 最后一轮修改
5. 按带出边界对外发布（Skill + Unity + Samples + 中英 README）

样本说明：

- [Samples/FPS/README.md](./Samples/FPS/README.md)
- [Samples/Casual/README.md](./Samples/Casual/README.md)

更深入的实现细节见项目内 `Doc/`（不随包带出）：

- `Doc/README.md`
- `Doc/README_Skill实现.md`
- `Doc/README_Unity实现.md`

## 对外带出的目录边界

正式对外带出只包含以下内容：

- `ai-create-ugui/`（Skill）
- `AIToUGUI/`（Unity 插件）
- `Samples/`（展示样本）
- `README.md`（中文说明）
- `README_EN.md`（英文说明）

`Doc/` 仅留在项目内，不随包带出。
也不要把“历史实验目录、旧 Workbench 资料、零散临时 case”混进正式带出包。
