# 插件与工具

工具就是 Agent 真正能做的事——读文件、跑命令、搜网页。DotCraft 的工具来自三处：**内置工具**（文件、Shell、Web、搜索、Plan、Todo 等，DotCraft 自带）、**插件**（你或第三方打包的额外工具和 skills），以及 **MCP servers**。开哪些，由你决定。

![DotCraft tool surface topology](/tool-surface-topology.svg)

## 内置工具

DotCraft 默认提供一组通用工具，覆盖大多数 coding agent 需求：

| 类别 | 代表工具 | 控制方式 |
|---|---|---|
| 文件 | `ReadFile` / `WriteFile` / `EditFile` / `GrepFiles` / `FindFiles` | 工作区边界与安全策略 |
| Shell | `Exec` | 审批策略、超时和沙箱 |
| Web | `WebSearch` / `WebFetch` | 网络与抓取限制 |
| LSP | 内置 LSP 工具 | 可选 LSP 工具设置 |
| Plan / Todo | `CreatePlan` / `UpdateTodos` / `TodoWrite` | 由 SubAgent role 策略控制 |

`ReadFile` 读取文本文件，并把支持的图片作为视觉输入返回。PDF 和其他二进制文件会被拒绝并返回提示信息，不会被解码为文本。

工具开关、allow-list、Web 限制和 LSP 设置见 [配置完整参考](../../developing/configuration#tools-security-与-sandbox)。

## 安装与使用插件

DotCraft 插件用来把可复用的工作区能力打包成可安装扩展，一个插件可以提供：

| 内容 | 说明 |
|---|---|
| Dynamic tool | Agent 可调用的工具，可由本地 stdio 进程执行 |
| Skill | 随插件分发的 plugin-contained skill，启用时进入 skill 列表 |
| Desktop extension | 受信任的本地 UI bundle，可向 Desktop 增加 sidebar main view 等界面 |
| 元数据 | 名称、描述、开发者、分类、图标、默认 prompt、相关链接 |

插件内置的 skill 跟随插件生命周期：启用插件时可用，禁用或移除插件后不再进入 Agent 上下文。
Desktop extension 也跟随插件生命周期：只有插件安装并启用后，Desktop 才会加载它的本地 bundle。

### 在 Desktop 中安装

![Plugin page](https://github.com/DotHarness/resources/raw/master/dotcraft/plugins.png)

1. 打开 DotCraft
2. 进入 **Plugins / 插件** 页面
3. 搜索或浏览插件，打开详情页
4. 点击 **Install / 安装**
5. 安装完成后可点 **Try in chat / 在对话中试用**，或在新对话中直接描述你想完成的任务

### 启用、禁用与移除

| 操作 | 含义 |
|---|---|
| 安装 | 将插件加入当前工作区可用能力 |
| 启用 / 禁用 | 保留插件文件，控制是否进入 Agent 上下文 |
| 移除 | 从当前工作区移除插件；对 `.craft/plugins/<plugin-id>` 下的本地插件，会删除对应目录 |

插件管理页可批量启用或禁用已安装插件。

### 安装本地插件

开发或测试本地插件时，有两种方式：

```text
.craft/plugins/<plugin-id>/.craft-plugin/plugin.json
```

把 plugin root 复制到当前工作区 `.craft/plugins/<plugin-id>/` 后，打开 Plugins 页面点击 **Refresh / 刷新**。这种安装可以在 Desktop 的插件详情页移除，移除时会删除该插件目录。

也可以让 DotCraft 直接读取你正在维护的外部目录。Desktop 不会主动删除外部 plugin roots；需要移除时请从配置中移除对应 root 或在文件系统中自行管理。完整字段见 [配置完整参考](../../developing/configuration#plugins-mcp-与-lsp)。

### 验证插件

1. 在 Plugins 页面点击 **Refresh / 刷新**
2. 搜索插件名称或 ID
3. 打开详情页确认 tools、skills 和链接信息
4. 如果插件没有出现，查看页面上的 diagnostics 提示，通常包含 manifest 路径和错误原因

## 创建插件

最快方式是让内置 `$plugin-creator` 先搭好结构，再补充说明、工具逻辑和验证步骤。

如果只是给当前项目补充一段工作流，优先创建普通 skill。需要把 skills、dynamic tools、图标和安装页信息一起打包分发时，再创建 plugin。

### 用 plugin creator 起步

在对话中直接描述你想要的插件：

```text
$plugin-creator 创建一个名为 External Process Echo 的插件，包含一个 skill 和一个外部进程 tool。
```

也可以把运行方式、语言和验证方式一起说清楚：

```text
$plugin-creator 创建一个本地插件，用 Python 进程提供 EchoText dynamic tool，并生成安装验证说明。
```

`plugin-creator` 会生成 DotCraft 插件目录、`.craft-plugin/plugin.json`、plugin-contained skill、可选 MCP 配置，以及可选 Desktop extension descriptor。生成后通常只需要：

1. 替换 TODO 和示例文案
2. 实现或调整 tool 进程逻辑
3. 把插件安装到本地并在 Plugins 页面刷新验证

### 插件结构

DotCraft 使用 `.craft-plugin/plugin.json` 作为插件入口。插件可以贡献 skills、通过 MCP 暴露 Agent 可调用工具，也可以贡献 Desktop UI surface。

Desktop UI surface 通过 `desktopExtensions` 声明：

```json
{
  "capabilities": ["desktopExtension"],
  "desktopExtensions": "./desktop-extensions.json"
}
```

descriptor 指向插件内的 ESM bundle，并声明它贡献的 Desktop surface。当前已经落地的第一类 surface 是 `mainView`，插件安装并启用后会出现在 Desktop sidebar：

```json
{
  "extensions": [
    {
      "id": "desktop",
      "displayName": "Project Board Desktop",
      "entry": "./desktop/main-view.mjs",
      "surfaces": [
        {
          "type": "mainView",
          "viewId": "main",
          "label": "Project Board",
          "placement": "sidebar",
          "order": 80
        }
      ],
      "permissions": ["navigation"]
    }
  ]
}
```

需要最小脚手架时可使用 `plugin-creator --with-desktop-extension`。

一般不需要手写完整 manifest。建议让 `plugin-creator` 生成结构，再把生成的 manifest 作为排查或分发时的高级参考。

### 高级参考

需要处理这些高级内容时：

- process-backed dynamic tool 的 JSON-RPC 协议
- tool 的 `approval` 元数据
- manifest 路径规则
- 完整字段和 schema

建议先使用 `plugin-creator` 生成 manifest，再按业务修改生成的文件。

## MCP Servers

除了内置工具和插件 dynamic tool，DotCraft 也支持通过 MCP 协议接入外部工具。MCP server 注册和延迟加载选项见 [配置完整参考](../../developing/configuration#plugins-mcp-与-lsp)。

## 安全与信任

安装插件会把新的 tools 和 skills 加入工作区能力范围。启用带 `process` backend 的插件后，DotCraft 可以启动插件 manifest 中声明的本地 stdio 进程来执行 dynamic tools。**只安装和启用你信任来源、代码和依赖的插件**。

- 插件 tool 调用仍会经过 DotCraft 的会话、审批和工具调用记录。
- Desktop extension bundle 会作为受信任本地 UI 代码运行在 Desktop renderer 中，只能调用 descriptor 允许的 Host SDK 能力。
- 插件详情中的网站、隐私政策和服务条款链接用于帮助你确认插件来源和行为边界。
- 黑名单、工作区边界、沙箱等限制对插件 tools 同样生效。详见 [安全与沙箱](../self-hosted/security)。

## 相关文档

- [Skills 与自学习](./skills) — Skill 与 Plugin 的关系
- [可观测性](../self-hosted/observability) — 在 Dashboard 看插件 tool 调用与审批
- [安全与沙箱](../self-hosted/security) — 工具能力的全局约束
