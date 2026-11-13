# 插件清单

本页面向插件开发者，定义 `.craft-plugin/plugin.json`，以及在使用或分享插件前用来检查它的 `dotcraft plugin verify` 命令。

想让 Agent 帮你写插件，可以从 `$plugin-creator` 开始。它会遵循这些规则并运行这个命令。

## 示例

```json
{
  "schemaVersion": 1,
  "id": "review-tools",
  "version": "0.1.0",
  "displayName": "Review Tools",
  "description": "Reviews pull requests against your team's written rules.",
  "capabilities": ["skill", "mcp"],
  "skills": "./skills/",
  "mcpServers": "./.mcp.json",
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review pull requests against team rules",
    "developerName": "Acme",
    "category": "Coding",
    "capabilities": ["Skill", "MCP"],
    "defaultPrompt": "Review this branch against our review rules.",
    "logo": "./assets/logo.svg",
    "composerIcon": "./assets/icon.svg"
  }
}
```

## 清单字段

加载插件必须有 `schemaVersion`、`id` 和 `displayName`。插件还必须声明至少一种贡献或 `interface`。

| 字段 | 类型 | 含义 |
|---|---|---|
| `schemaVersion` | number | 必须为 `1` |
| `id` | string | 插件标识。可用 ASCII 字母、数字、`.`、`_`、`-` 或 `:` |
| `version` | string | 带 Desktop 或 .NET 的插件必须使用 `MAJOR.MINOR.PATCH` 格式 |
| `displayName` | string | 没有 `interface.displayName` 时显示的名称 |
| `description` | string | 插件做什么 |
| `capabilities` | string[] | 插件提供的贡献类型，例如 `skill`、`mcp` 或 `desktop` |
| `skills` | path | 技能目录，每个技能一个子目录 |
| `mcpServers` | path | MCP server 配置。默认读取根目录的 `.mcp.json` |
| `lspServers` | path | 语言服务器配置。默认读取根目录的 `.lsp.json` |
| `hooks` | path、array 或 object | 生命周期 hooks。默认读取根目录的 `hooks/hooks.json` |
| `apps` | path | [App Binding](./app-binding) 描述文件 |
| `workflows` | path | Dynamic Workflow 目录。默认读取根目录的 `workflows/` |
| `settings` | path | 设置 schema |
| `desktop` | object | [桌面扩展](./desktop-extensions)模块 |
| `dotnet` | object | [.NET 扩展](./dotnet-extensions)元数据 |
| `dependencies` | object | 直接依赖的插件，仅可与 `dotnet` 一起使用 |
| `interface` | object | 目录元数据，见下文 |

所有路径都以 `./` 开头、不包含 `..`，并保持在插件根目录内。不支持 `tools`、`functions` 和 `processes`。可复用的外部工具请使用 MCP。

## MCP 启动路径

在插件的 `.mcp.json` 中，用 `${PLUGIN_ROOT}` 引用插件内文件，用 `${PLUGIN_DATA}` 存放插件的可写状态：

```json
{
  "mcpServers": {
    "review": {
      "command": "node",
      "arguments": ["${PLUGIN_ROOT}/server.js"],
      "environmentVariables": {
        "CACHE_DIR": "${PLUGIN_DATA}/cache"
      },
      "cwd": "${PLUGIN_ROOT}"
    }
  }
}
```

对于 stdio servers，DotCraft 会在 `command`、`arguments`、`environmentVariables` 的值和 `cwd` 中展开这些变量，并向进程环境注入 `PLUGIN_ROOT` 和 `PLUGIN_DATA`。同一插件的 Hooks、MCP servers 和 LSP 进程共享数据目录。相对 `cwd` 以插件根目录为基准解析。

展开区分大小写且只执行一遍。未知占位符保持原样，HTTP URL 和 headers 不做展开。

## Interface 字段

缺少其中大部分字段时 DotCraft 仍能加载插件，但 `dotcraft plugin verify` 会要求填写，这样插件在目录中才显得完整。

| 字段 | 校验规则 |
|---|---|
| `displayName` | 必填，最多 30 个字符。顶层 `displayName` 的上限相同 |
| `shortDescription` | 必填，单行，最多 48 个字符 |
| `developerName` | 必填。真实的作者或团队 |
| `category` | 必填。取值为 `Coding`、`Design`、`Engineering`、`Security`、`Lifestyle`、`Productivity` 或 `Research` |
| `capabilities` | 必填。显示标签 |
| `defaultPrompt` | 必填，单行，最多 128 个字符 |
| `logo` | 必填。见下文 |
| `composerIcon` | 必填。可以与 `logo` 使用同一个文件 |
| `longDescription` | 可选，最多 300 个字符 |
| `websiteUrl`、`privacyPolicyUrl`、`termsOfServiceUrl` | 可选链接 |

校验同样要求填写顶层 `description`：单行，最多 160 个字符。

文案用一句平实的话说明插件做什么，不写假设、建议、配置步骤和实现细节。

图标是插件内的正方形 SVG、PNG、JPEG 或 WebP 文件，每个最大 512 KiB。位图的 `logo` 至少 256 × 256，`composerIcon` 至少 48 × 48。SVG 不能包含脚本、事件处理器或外部引用。需要背景才能保证对比度时，把背景画进图标。

## 校验插件

```bash
dotcraft plugin verify --candidate <plugin-root> [--for-publish] [--json]
```

该命令不写入任何文件、不访问网络，也不加载插件代码。.NET 项目的校验对象是 `CSharp.Build` 之后的 `plugin/` 目录。

它会检查清单以及声明的每个文件、描述文件、Desktop 产物和插件技能是否符合运行时规则，是否还留有 `[TODO: ...]` 占位符，以及上述字段和图标是否有效。

`--for-publish` 会增加面向公开分发的检查：

- `version` 是严格的 SemVer，`id` 是连字符格式且最多 64 个字符。
- 插件不包含 `.git`、`node_modules`、`.builtin`、构建中间文件或符号链接。
- 描述文件不含明文凭据或本机绝对路径，文本文件不含可识别的令牌。

没有错误时退出码为 0。警告不影响退出码，例如 `defaultPrompt` 只是重复插件名称。`--json` 输出：

```json
{
  "isValid": false,
  "candidatePath": "/path/to/plugin",
  "pluginId": "review-tools",
  "version": "0.1.0",
  "forPublish": false,
  "diagnostics": [
    {
      "severity": "error",
      "code": "PluginIconMissing",
      "message": "interface.logo is not declared.",
      "path": ".craft-plugin/plugin.json"
    }
  ]
}
```

`code` 保持稳定，`message` 是供人阅读的英文文本。

## 相关文档

- [插件市场](./plugin-market)：通过自己的市场或官方市场发布插件。
- [开发桌面扩展](./desktop-extensions)：添加 Desktop 界面。
- [开发 .NET 扩展](./dotnet-extensions)：添加进程内能力。
