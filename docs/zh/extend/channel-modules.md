# 渠道模块

把[渠道适配器](./sdks/channels)打包成插件，让 DotCraft 为工作区安装、配置和运行它。模块提供静态描述文件和可执行产物，适配器仍负责平台连接和消息投递。

![渠道模块在宿主中从启动、就绪到停止的生命周期](/typescript-module-lifecycle.svg)

## 声明渠道插件

在 `.craft-plugin/plugin.json` 的[插件清单](./plugin-manifest)中添加 `channel` 贡献：

```json
{
  "schemaVersion": 1,
  "id": "channel-acme",
  "version": "0.1.0",
  "displayName": "Acme Chat",
  "description": "Connects a workspace to Acme Chat.",
  "capabilities": ["channel"],
  "channel": {
    "manifest": "./channel/manifest.json",
    "entry": "./channel/dist/cli.bundle.mjs",
    "minHostVersion": "0.14.0"
  },
  "interface": {
    "displayName": "Acme Chat",
    "shortDescription": "Send and receive messages in Acme Chat",
    "developerName": "Acme",
    "category": "Channels",
    "capabilities": ["Channel"],
    "defaultPrompt": "Summarize the status update from Acme Chat.",
    "websiteUrl": "https://example.com/acme-chat",
    "logo": "./assets/icon.svg",
    "composerIcon": "./assets/icon.svg"
  }
}
```

`manifest` 指向模块描述文件，`entry` 指向打包后的 Node.js 启动入口。两个文件都必须在插件根目录内，路径不能经过符号链接。`minHostVersion` 是支持此产物的最低 DotCraft 版本。

## 描述模块

把下面的描述文件保存为 `channel/manifest.json`：

```json
{
  "moduleId": "acme-chat-standard",
  "channelName": "acme-chat",
  "displayName": "Acme Chat",
  "packageName": "@acme/dotcraft-channel",
  "configFileName": "acme-chat.json",
  "variant": "standard",
  "channelContractVersion": "1.0.0",
  "supportedChannelProtocolVersions": ["0.2"],
  "supportedTransports": ["websocket"],
  "requiresInteractiveSetup": false,
  "capabilitySummary": {
    "hasChannelTools": false,
    "requiresInteractiveSetup": false,
    "capabilitySetMayVaryByEnvironment": false
  },
  "launcher": {
    "bin": "acme-chat",
    "supportsWorkspaceFlag": true,
    "supportsConfigOverrideFlag": false
  }
}
```

`channelName` 标识消息路由，`moduleId` 标识具体实现及其状态。更新时保持这两个值不变。`configFileName` 是非保留的 JSON 文件名，不是路径。

用 `configDescriptors` 声明可编辑字段，用 `configGroups` 为字段分组排序。字段使用带点号的 `key`，例如 `acme.token`，并包含 `displayLabel`、`dataKind`、`required`、`masked` 和 `interactiveSetupOnly`。凭据用 `dataKind: "secret"`，对象或列表中含有凭据的字段用 `masked: true`。不要把凭据作为默认值分发。`dotcraft.*` 由宿主管理，不能编辑。配置描述就是插件的设置 schema，不要另行声明 `settings` 文件。

## 启动与校验

构建可独立运行的 Node.js 产物。宿主传入 `--workspace <path>`，并把 `DOTCRAFT_CHANNEL_CRAFT_PATH` 设为工作区数据目录。用 `DOTCRAFT_CHANNEL_WS_URL` 和 `DOTCRAFT_CHANNEL_WS_TOKEN` 完成 [AppServer 握手](./protocols/appserver#渠道适配器)。Desktop 和 Docker 提供 Node.js，独立宿主需配置 Node.js 22 或更高版本。

校验打包后的插件：

```bash
dotcraft plugin verify --candidate ./channel-acme --for-publish
```

安装通过校验的候选插件。在**插件 → 渠道**完成设置，点**连接**，然后测试 Bot 能否收发消息。

## 相关文档

- [渠道适配器](./sdks/channels) — 消息路由、AppServer 交互与平台投递。
- [插件市场](./plugin-market) — 通过市场发布插件。
