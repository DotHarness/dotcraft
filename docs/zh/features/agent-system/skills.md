# Skills 与自学习

Skills 把"怎么做这件事"教给 Agent 一次，下次它就不用重新摸索。每个 skill 是一份带 frontmatter 的 Markdown，Agent 遇到匹配场景时主动加载、照着步骤执行。Skills 可以系统内置、可以你手写，也可以让 Agent 在成功完成任务后自己保存——万一坏了，永远能回到原版。

## Skills 来源

![DotCraft skill sources overview](/skills-sources-overview.svg)

| 来源 | 路径 | 说明 |
|---|---|---|
| **系统** | DotCraft 安装包内 | 内置 skill，随应用提供，所有工作区共用 |
| **个人** | `~/.craft/skills/` | 用户全局技能，可跨工作区使用 |
| **工作区** | `.craft/skills/` | 当前项目专属技能，跟随仓库走 |
| **插件自带** | `.craft/plugins/<id>/skills/` | 由插件分发的 skill，跟随插件生命周期 |
| **市场安装** | `.craft/skills/<id>/`（带 `.dotcraft-market.json`） | 来自 SkillHub / ClawHub 的第三方技能 |

来源不是默认开关。是否启用由 Skills 管理页面控制，从市场搜到的技能必须先安装才进入本地列表。

## Agent Skill 自学习

启用 skill 自学习后，DotCraft 会给 Agent 一个受控的 skill 编辑能力，让它在完成任务后创建、修补和维护工作区 skill。创建和破坏性变更会走审批，完整开关和限制见 [配置完整参考](../../developing/configuration#workspace-memory-与-skills)。

### 边界与目录约束

- 自学习只写当前工作区 skill 目录。**系统 skill 与个人 skill 视为只读**——需要修改时由 Agent 创建工作区副本。
- supporting file 只能写在 `scripts/` 或 `assets/` 子目录。
- 工具会拒绝绝对路径和 `..` 路径穿越。

### 适合保存为 skill 的情况

- 完成复杂任务后总结出可复用流程
- 修复了一个以后可能再次遇到的棘手错误
- 用户纠正了 Agent 的做法，并形成了稳定步骤
- 使用已有 skill 时发现其步骤过期、不完整或存在坑点

简单的一次性回答不应保存为 skill。

## 在 Desktop 中搜索与安装

DotCraft 的 Skills 页面同时搜索本地已安装技能和外部技能市场：

![Skills 页面](https://github.com/DotHarness/resources/raw/master/dotcraft/skills.png)

搜索框会同时做两件事：

- 过滤当前已安装的本地技能
- 当有查询词时，搜索 SkillHub 和 ClawHub 的市场结果

来源筛选可切换 `全部 / 系统 / 个人 / 市场`，只影响浏览结果，不改变启用状态。

### 从市场安装

1. 在搜索结果中点击一个市场技能
2. 在详情页阅读 README、描述和来源链接
3. 点击 **Install with DotCraft**
4. DotCraft 会启动一个安装 Agent：检查工作区、系统环境和可用工具，并在发现差异时为本地环境生成优化版本
5. 安装完成后刷新本地技能列表

![Skill market 搜索结果](https://github.com/DotHarness/resources/raw/master/dotcraft/skill-hub.png)

<p class="caption">Desktop 通过 DotCraft 安装市场技能并生成本地变体</p>

市场技能落地为：

```text
.craft/skills/<skill-name>/
.craft/skills/<skill-name>/.dotcraft-market.json
```

`.dotcraft-market.json` 用来识别来源、版本和更新状态，工作区已存在同名技能时 Desktop 会要求确认后再覆盖。

### Skill Variant：保留原版，叠加优化

通过 **Install with DotCraft** 安装时，Agent 会先保留原始技能再生成针对当前环境的优化版本（Variant），不直接覆盖原版。后续使用时 DotCraft 优先用当前有效 Variant。想回到市场安装时的原始内容，可在 Skills 页面随时恢复。

这样自学习带来的收益保留下来，风险也有清晰的回退路径。

![Skill Variant](https://github.com/DotHarness/resources/raw/master/dotcraft/skill_variant.gif)

## 启用 / 禁用管理

Skills 页面右上角 **管理** 是批量启用/禁用入口：

1. 点击 **管理**
2. 在管理页搜索已安装技能
3. 用每行右侧开关控制启用状态

管理页只管理已安装到本地的技能，不会去搜索 SkillHub / ClawHub。被禁用的技能不会进入 Agent 上下文，但文件仍保留。

## 内置工作流 skill：`skill-authoring`

启用自学习后，DotCraft 会给 Agent 一份按需的 `skill-authoring` 参考：如何组织 skill 的 frontmatter、supporting file 该放在哪里、常见坑，以及如何验证结果。关闭自学习，这份参考也会随之消失。

## 官方开发工作流插件

官方开发技能按作用域拆分：

| 插件 | 作用域 |
|---|---|
| `dotcraft` | DotCraft 专属开发、文档、发布、简化、故障诊断、上下文交接与问题报告工作流。 |
| `harness-workflow` | 遵循当前项目约定的共享功能规划与隔离 UI 原型工作流。 |

根据当前工作从 Plugins catalog 启用相应插件。开发大型 DotCraft 功能时，通常会同时启用两者。

## 安全与信任

- 系统 skill 与插件自带 skill 默认可信。
- SkillHub / ClawHub 是外部来源，安装前阅读 README 与来源链接，必要时先在分支或受控工作区验证。
- 市场搜索失败或断网时，本地技能搜索和管理仍可继续使用。

## 何时用什么

| 场景 | 推荐 |
|---|---|
| 项目内固定流程（"提交 PR 前必跑 lint+test"） | 工作区 skill，可手写也可让 Agent 创建 |
| 跨项目通用偏好（自己的代码风格） | 个人 skill |
| 受控分发的能力包（含工具 + 多 skill） | 创建 [Plugin](./plugins-tools) |
| 想让 Agent 把刚解决的问题沉淀下来 | 启用自学习，让 Agent 自己保存 |
| 想用社区已有方案 | Skills 页面市场搜索 + Install with DotCraft |

## 相关文档

- [Plugins 与工具](./plugins-tools) — 用插件分发 skills + tools 的能力包
- [Spec-Driven Development](../../developing/workflow/spec-driven-development) — `dotcraft` 与 `harness-workflow` 如何划分产品规则与共享工作流
