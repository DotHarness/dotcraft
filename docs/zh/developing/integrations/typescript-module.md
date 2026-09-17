# 渠道模块

渠道模块把[渠道适配器](../sdks/channels)封装为 Desktop 等 DotCraft 宿主可以发现、配置、启动和停止的单元。适配器仍然负责消息路由和平台投递。模块只在适配器外补充宿主需要的元数据与生命周期边界。

![渠道模块在宿主中的生命周期：从 starting 进入 ready，最后到 stopped。配置、认证和 degraded 状态从主路径分出](/typescript-module-lifecycle.svg)

## 适配器与模块层级

两个层级共同组成一个 Channel 实现：

| 层级 | 职责 |
| --- | --- |
| **渠道适配器** | 连接 AppServer、解析 thread、运行 turn、处理审批并投递平台消息。 |
| **渠道模块** | 描述具体实现、提供配置元数据、创建适配器实例，并向宿主报告生命周期状态。 |

适配器需要模块级配置与生命周期支持时，继承 `ModuleChannelAdapter`。可由宿主加载的包还需要从根入口导出模块契约，并提供用于发现的 `manifest.json`。

## 导出模块契约

从包根入口导出以下内容：

- `manifest`：稳定身份、传输方式、launcher 和能力元数据
- `createModule(context)`：返回运行模块边界的工厂函数
- `configDescriptors`：供宿主校验和渲染配置的字段
- `configGroups`：可选的字段分组及顺序

工厂函数包装适配器，无需实现第二套 Channel Runtime：

```ts
import type { ModuleFactory } from "@dotcraft/channel";
import { MyChannelAdapter } from "./my-channel-adapter.js";

export const createModule: ModuleFactory = (context) => {
  const adapter = new MyChannelAdapter();

  return {
    start: () => adapter.startWithContext(context),
    stop: () => adapter.stop(),
    onStatusChange: (handler) => adapter.onStatusChange(handler),
    getStatus: () => adapter.getStatus(),
    getError: () => adapter.getError(),
  };
};
```

`WorkspaceContext` 提供 `workspaceRoot`、`craftPath`、`channelName` 和 `moduleId`。使用这些值定位运行环境，不要依赖当前工作目录。这样同一个模块才能由不同宿主运行。

## 描述模块

使用公共 `ModuleManifest` 类型定义 manifest。`moduleId` 用于选择具体实现，`channelName` 则保留兼容变体共享的逻辑 Channel 身份。

```ts
import type { ModuleManifest } from "@dotcraft/channel";
import { CHANNEL_CONTRACT_VERSION } from "@dotcraft/channel/meta";

export const manifest: ModuleManifest = {
  moduleId: "acme-chat-standard",
  channelName: "acme-chat",
  displayName: "Acme Chat",
  packageName: "@acme/dotcraft-channel",
  configFileName: "acme-chat.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: false,
    hasStructuredDelivery: false,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: false,
  },
  channelContractVersion: CHANNEL_CONTRACT_VERSION,
  supportedChannelProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-acme",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
```

只从文档列出的包入口导入。不要导入包内私有文件，也不要从宿主源码结构推断契约字段。

## 提供配置元数据

宿主使用 `configGroups` 和 `configDescriptors` 构建配置界面，无需理解平台特有设置。Group id 必须非空且唯一。带有 `group` 的 descriptor 必须引用已声明的 Group。

各字段按以下规则使用：

- `required` 控制校验。
- `masked` 与 `dataKind: "secret"` 保护敏感输入。
- `displayLabel`、`description` 及其本地化形式提供界面文案。
- `options` 描述本地化枚举选项，`allowCustomValue` 允许额外的自定义值。
- 未保存值时，`defaultValue` 提供界面使用的有效显示值。

显示默认值时不要自动保存。只有用户编辑字段后才持久化。

## 报告生命周期状态

在调用 `start()` 前注册状态 handler，确保宿主能观察到早期状态变化。模块报告 `LifecycleStatus` 定义的结构化状态，其中包括需要宿主介入的配置和认证状态。

```ts
const instance = createModule(context);

instance.onStatusChange((status, error) => {
  console.log(manifest.moduleId, status, error);
});

await instance.start();
```

调用 `stop()` 结束实例。`stopped` 是该实例的终止状态。再次启动前应创建新实例。

## 供宿主发现

已安装模块目录包含包文件和 `manifest.json`。该文件承载发现元数据与配置 descriptor。Desktop 扫描配置的模块目录，按 `channelName` 对兼容实现分组，再通过 `moduleId` 选择具体变体。

模块包提供 manifest 所描述的 launcher。宿主发现与适配器的平台逻辑保持分工，宿主选择、启动和消息投递仍属于同一条生命周期。

## 相关文档

- [渠道适配器](../sdks/channels)——实现消息路由、AppServer 交互和平台投递。
- [渠道配置](../../features/channels/reference)——在 DotCraft 中配置已安装的 Channel。
