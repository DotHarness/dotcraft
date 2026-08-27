# Channel Module 集成

本文档面向把 TypeScript 外部社交渠道模块嵌入宿主（Desktop、CLI 工具或其他调度进程）的开发者，基于 `@dotcraft/channel` 模块契约。Channel authoring packages 仅在仓库内使用；请先构建 `sdk/typescript`，再安装所需的本地包目录。已发布的客户端包见 [TypeScript SDK 设置](../sdks/typescript)。

## 1. 概览

模块契约为宿主提供稳定的集成边界：

- 从 `manifest` 读取模块元数据
- 通过 `createModule(context)` 创建可运行实例
- 以机器可读方式观察生命周期状态与错误
- 基于 `configGroups` 与 `configDescriptors` 渲染分组配置界面
- 通过 `moduleId` 切换变体，同时保持 `channelName` 作为运行时逻辑身份

只从包根导入，不要依赖包内私有路径，也不要通过源码目录结构推断行为。

## 2. 加载模块

宿主只从包根导入。

```typescript
import { configDescriptors, configGroups, createModule, manifest } from "@dotcraft/channel-feishu";
import type { ModuleFactory, ModuleManifest } from "@dotcraft/channel";

const moduleManifest: ModuleManifest = manifest;
const moduleFactory: ModuleFactory = createModule;

console.log(moduleManifest.moduleId);
console.log(configGroups.length);
console.log(configDescriptors.length);
```

## 3. 模块发现

宿主可以通过“已知包列表”或“`moduleId` 配置映射”构建模块注册表。

推荐流程：

1. 加载允许接入的包根。
2. 读取每个包导出的 `manifest`。
3. 按 `moduleId` 建立索引。
4. 可选按 `channelName` 做分组展示。

选择键是 `moduleId`，运行时逻辑身份保持为 `channelName`。

## 4. 创建并启动模块实例

显式构造 `WorkspaceContext` 并传给模块工厂。

```typescript
import { createModule, manifest } from "@dotcraft/channel-feishu";
import type { ModuleInstance, WorkspaceContext } from "@dotcraft/channel";

const context: WorkspaceContext = {
  workspaceRoot: "F:/workspace/demo",
  craftPath: "F:/workspace/demo/.craft",
  channelName: manifest.channelName,
  moduleId: manifest.moduleId,
};

const instance: ModuleInstance = createModule(context);
await instance.start();
```

启动输入由宿主明确传入，模块不依赖当前工作目录来定位工作区。

## 5. 生命周期观察

在调用 `start()` 之前注册状态回调，以免漏掉早期状态切换。

```typescript
import type { LifecycleStatus, ModuleError, ModuleInstance } from "@dotcraft/channel";

function mapStatusToHostAction(status: LifecycleStatus, error?: ModuleError): string {
  switch (status) {
    case "configMissing":
      return "提示用户创建模块配置";
    case "configInvalid":
      return `展示配置错误：${error?.message ?? "配置无效"}`;
    case "starting":
      return "展示连接中状态";
    case "ready":
      return "标记模块已就绪";
    case "authRequired":
      return "启动交互式认证流程";
    case "authExpired":
      return "提示认证过期并引导重新认证";
    case "degraded":
      return "展示降级告警";
    case "stopped":
      return "标记模块已停止";
  }
}

function observeLifecycle(instance: ModuleInstance): void {
  instance.onStatusChange((status, error) => {
    const action = mapStatusToHostAction(status, error);
    console.log(`[module-status] ${status} -> ${action}`);
  });
}
```

宿主可随时通过 `instance.getStatus()` 获取当前状态，通过 `instance.getError()` 获取最近结构化错误。

## 6. 渲染配置界面

若包导出了 `configGroups` 与 `configDescriptors`，宿主可据此构建配置表单，无需解析包内私有 schema。按导出顺序渲染非空分组，并保持展开。

```typescript
import { configDescriptors, configGroups } from "@dotcraft/channel-feishu";
import type { ConfigDescriptor, ConfigGroupDescriptor } from "@dotcraft/channel";

type FormGroup = {
  group: ConfigGroupDescriptor;
  fields: ConfigDescriptor[];
};

const groups: FormGroup[] = configGroups
  .map((group) => ({
    group,
    fields: configDescriptors.filter((descriptor) => descriptor.group === group.id),
  }))
  .filter(({ fields }) => fields.length > 0);
```

让宿主 UI 遵循：

- Group id 必须非空且唯一，`ConfigDescriptor.group` 必须引用已声明的 Group
- `required`：必填校验
- `masked` 与 `dataKind: "secret"`：敏感字段掩码展示
- `displayLabel` / `description`：作为用户可读提示
- 使用结构化 `options` 提供本地化枚举名称与预览，其优先级高于 `enumValues`
- 使用 `allowCustomValue` 渲染“预设 + 自定义”枚举控件
- 已存配置缺少字段时，将 `defaultValue` 作为界面显示的有效值

显示 `defaultValue` 时不得初始化或保存该字段。只有用户明确编辑控件后才写入配置。

没有 `group` 的字段进入隐式 `Configuration` Group。针对旧 manifest，只有 `advanced: true` 且没有 `group` 的字段进入隐式 `Advanced` Group。新模块应显式声明所有 Group 及字段归属。

## 7. 交互式初始化

交互式初始化需求应通过生命周期状态表达，而不是绑定某个固定 UI。

```typescript
import type { ModuleInstance } from "@dotcraft/channel";

function attachInteractiveSetupHandlers(instance: ModuleInstance): void {
  instance.onStatusChange((status, error) => {
    if (status === "authRequired") {
      console.log("展示二维码路径或初始化引导");
      return;
    }
    if (status === "authExpired") {
      console.log("提示会话过期并触发重新认证流程");
      return;
    }
    if (status === "configMissing" || status === "configInvalid") {
      console.log(`需要处理配置问题：${error?.message ?? status}`);
    }
  });
}
```

具体交互方式（Desktop 面板、CLI 提示、Dashboard 通知）由宿主决定。契约只要求状态是结构化、可识别的。

## 8. 停止模块

通过 `await instance.stop()` 停止模块，并将 `stopped` 视为该实例的终止状态。

推荐宿主行为：

1. 禁用该实例的发送/工具调用入口。
2. 将连接状态标记为离线。
3. 保留最近结构化错误用于排障与审计。

## 9. 变体替换

变体替换允许宿主切换同一渠道族的实现，同时保持逻辑渠道身份不变。

选择模型：

- 通过 `moduleId` 选择具体实现
- 通过 `channelName` 保持运行时身份
- 默认配置命名仍按渠道约定（除非 manifest 明确声明不同）

示例：

- 标准版：`moduleId = "feishu-standard"`，`channelName = "feishu"`
- 企业版：`moduleId = "feishu-enterprise"`，`channelName = "feishu"`

因此宿主只需切换 `moduleId`，无需重写集成模型。

## 10. 模块接入要求

第三方包满足以下导出即可被同一宿主模型加载：

- `manifest`
- `createModule`
- 可选 `configGroups`
- 可选 `configDescriptors`

建议接入清单：

1. 实现 `@dotcraft/channel` 模块契约类型。
2. 保持宿主只依赖包根导出。
3. 提供机器可读生命周期与错误信号。
4. 在模块边界内完成配置验证。
5. 提供包级测试与一致性测试。

这样可保证一方、企业版与第三方模块都能在同一宿主边界下热插拔与替换。

## 相关文档

- [Channel adapters](../sdks/channels)——模块所基于的适配器基类。
- [TypeScript SDK](../sdks/typescript)——模块内部使用的 `@dotcraft/sdk` 客户端。
- [飞书渠道适配器](../channels/feishu)——实现本契约的完整模块示例。
