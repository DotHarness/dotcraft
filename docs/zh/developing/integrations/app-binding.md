# App Binding

App Binding 让你接入一个已安装的原生应用——[Oratorio](#示例)、IDE 插件，或你自己写的工具——并把它的工具授予**某一个指定线程**使用。账号、授权和高风险操作始终由应用自己掌控；DotCraft 只负责控制模型能看到哪些工具，并对每一次调用做审批与审计。

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

> [!NOTE]
> 一次绑定只作用于单个线程。除非你另行授予，其他线程、其他应用都看不到它。子代理（SubAgent）和分叉线程不会继承绑定，导入的线程也不会自动重新激活绑定。

## 三个独立步骤

应用访问被刻意拆成几个独立步骤，避免任何意外授予：

| 步骤 | 它做什么 | 它**不**做什么 |
|---|---|---|
| **1. 安装插件** | 让应用及其工具目录在 DotCraft 中可见。 | 不给任何线程授权。 |
| **2. 安装或打开原生应用** | 让应用可通过其注册的系统标识被拉起。 | 不连接你的账号。 |
| **3. 连接，再绑定线程** | 连接应用账号，再把选定的 scope 授予所选线程。 | 只授予你选中的那个线程。 |

连接会通过 deep link（例如 `oratorio://dotcraft/connect?…`）打开应用，由应用展示它自己的确认界面。DotCraft 不会要求你选择可执行文件、源码目录或命令行。

## 你授予了什么

绑定线程时，你批准的是一组 **scope**。每个 scope 带有风险级别，决定其工具如何暴露给模型：

| 风险 | 含义 | 默认暴露方式 |
|---|---|---|
| **Read（读）** | 只读取应用状态，不做任何修改。 | 直接加载。 |
| **Mutate（改）** | 修改应用自有状态或排队操作。 | 延迟加载——按需才出现。 |
| **External write（外部写）** | 可发布、发送或写入外部系统。 | 延迟加载，且通常走应用内确认。 |

高风险工具遵循"先提议、后确认"：Agent 排队一个操作，由你在应用内批准或发布。每次工具调用都会记入 DotCraft 的审计链路，应用还会在此之上保留自己的授权记录。

## 在 Desktop 中

App Binding 出现在三个位置：

- **插件详情页**——安装插件、查看原生应用是否已安装、连接、绑定当前线程、重连或撤销。
- **线程头部**——为当前线程绑定、刷新、查看、打开应用或撤销。
- **Welcome 流程**——在发送第一条消息之前，就用一个或多个已绑定的应用新建线程。

连接状态与绑定状态始终分开展示，因此"我的账号已连接"和"这个线程有访问权"是两回事，一眼就能区分。

## 绑定状态一览

| 状态 | 含义 |
|---|---|
| **Active（活跃）** | 授权有效，应用的工具对该线程可用。 |
| **Offline（离线）** | 授权仍在，但应用已关闭或不可达。调用会快速失败；重新打开应用即可重连。 |
| **Expired（过期）** | 授权已超时。工具会在下一个安全点被移除。 |
| **Revoked（已撤销）** | 你或应用切断了访问。工具立即被禁用。 |

关闭原生应用会把绑定置为 **offline**，而不是删除——重新打开即可重连并重新挂载工具。你可以随时在线程或插件面板中**刷新**或**撤销**绑定。

## 示例

- **Oratorio**——把 Oratorio 看板接入某个线程，让 Agent 列出条目、查看卡片、创建任务、排队评审轮次。
- **Teams**——DotCraft 的多 Agent 看板本身就是一个托管式 App Binding 运行时。参见 [Teams](../../features/agent-system/teams)。
- **你自己的工具**——用 SDK 把任意服务封装成一个应用。参见 [App Binding 集成](#app-binding-集成) 自行构建。

## App Binding 集成

App Binding 是把**外部原生应用**接入 DotCraft、并将应用自有工具授予某一个线程的平台化流程——而应用无需交出自己的账号、授权和高风险操作。本节面向插件与原生应用构建者。

### 产品模型

App Binding 有四个明确的层级。把它们分开，正是访问从"默认拥有"变成"显式授予"的关键：

| 层级 | 作用域 | 归属 | 含义 |
|---|---|---|---|
| 插件安装 | 工作区 | DotCraft | 让应用元数据与工具目录可见。 |
| 原生应用安装 | 机器 / 用户 | 操作系统 + 应用 | 让应用可通过其注册的系统标识被拉起。 |
| 应用连接 | 工作区 + 用户 + 应用 | 应用 + DotCraft | 经应用侧同意连接一个账号 / 工作区。 |
| 线程绑定 | 线程 + 应用 + 授权 | 应用 + DotCraft | 把选定 scope 与工具授予单个线程。 |

### 权责划分

真实授权始终归应用所有。DotCraft 负责校验与门控，但它**不能**替代应用自身的检查。

| DotCraft 负责 | 应用负责 |
|---|---|
| 目录、插件、连接与绑定记录 | 账号选择、认证与同意界面 |
| 线程范围内、模型可见的工具暴露 | 真实授权与资源策略 |
| 描述符 / scope / namespace / 风险校验 | 授权凭据、撤销与应用侧审计 |
| 派发工具调用前的审批门控 | 对每次挂载工具调用的最终校验 |
| 生命周期与工具调用审计 | 原生应用生命周期及任何本地服务 |

### 你要构建什么

两部分：

1. **一个 DotCraft 插件**，贡献 app 描述符。没有归属插件的裸描述符不会获得产品流程。
2. **一个原生应用**，注册 OS 协议、处理 deep-link 交接、通过 AppServer 检视请求、接受请求并挂载自己的工具。

#### 1. 贡献 App 描述符

插件在其 manifest 中指向一个 `apps` 文档：

```json
{
  "schemaVersion": 1,
  "id": "oratorio",
  "displayName": "Oratorio",
  "capabilities": ["skill", "app"],
  "skills": "./skills/",
  "apps": "./apps.json"
}
```

`apps.json` 声明应用身份、原生应用元数据、scope 以及静态工具目录：

```json
{
  "apps": [
    {
      "appId": "com.dotharness.oratorio",
      "toolNamespace": "oratorio",
      "displayName": "Oratorio",
      "developerName": "DotHarness",
      "description": "从选定的 DotCraft 线程管理 Oratorio 看板。",
      "nativeApplication": {
        "protocol": "oratorio",
        "installUrl": "https://github.com/DotHarness/oratorio/releases"
      },
      "connection": {
        "handoffModes": [
          { "mode": "customProtocol", "uriTemplate": "oratorio://dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}" }
        ]
      },
      "scopes": [
        { "id": "board.read", "displayName": "读取看板", "description": "读取看板条目与轮次。", "risk": "read", "defaultSelected": true },
        { "id": "board.manage", "displayName": "管理看板", "description": "创建任务并排队评审轮次。", "risk": "mutate" }
      ],
      "toolCatalog": [
        { "name": "ListBoardItems", "scope": "board.read", "risk": "read", "defaultExposure": "direct" },
        { "name": "QueueReviewRound", "scope": "board.manage", "risk": "mutate", "defaultExposure": "deferred" }
      ]
    }
  ]
}
```

关键规则：

- `appId` 采用反向 DNS、全小写、至少三段。`toolNamespace` 匹配 `^[A-Za-z_][A-Za-z0-9_]*$`，在目录内唯一，并作为每个应用绑定工具的前缀。
- 静态 `toolCatalog` 是用于发现、同意与校验的粗粒度声明，**不是**可执行 schema。具体 schema 在挂载阶段才提供。若要在运行时挂载目录，设置 `dynamicToolCatalog.enabled`。
- 工具的 `risk` 不得低于其 scope 的风险。`mutate` 与 `externalWrite` 默认延迟暴露。

#### 2. 在原生应用中处理交接

DotCraft 会拉起你注册的协议（它绝不直接 spawn 可执行文件）。应用解析 URL，通过短时的交接端点检视请求，接受请求并挂载工具。使用 [.NET SDK](../sdks/dotnet)：

```csharp
var handoff = AppBindingHandoff.Parse(handoffUrl, expectedScheme: "oratorio", expectedAppId: "com.dotharness.oratorio");

await using var client = await DotCraftClient.ConnectRemoteAsync(
    handoff.AppServerUrl!, options: new DotCraftClientOptions { ClientName = "oratorio" }, ct);

// 从可信来源检视请求——绝不要只凭 deep-link 查询文本。
var request = await client.AppBindings.GetBindingRequestAsync<JsonElement>(new {
    appId = handoff.AppId, bindingRequestId = handoff.RequestId, requestToken = handoff.RequestToken
}, ct);

// 在应用侧授权后再接受。scope 只能收窄，绝不能扩大。
await client.AppBindings.AcceptBindingAsync<JsonElement>(new {
    bindingRequestId = handoff.RequestId, requestToken = handoff.RequestToken,
    grantId = "grant_" + Guid.NewGuid().ToString("N"),
    grantedScopes = new[] { "board.read" },
    approvalMode = "appAccepted", approvedBy = Environment.UserName
}, ct);

// 挂载具体工具规格，然后持续消费通知以保持连接存活。
```

完整 RPC 在 [.NET SDK](../sdks/dotnet) 与 [SDK 总览](../sdks/) 中。同样的流程可通过 [AppServer 协议](../protocols/appserver-protocol) 用任意语言实现。

### 绑定流程

![DotCraft App Binding 交接流程](/app-binding-flow.svg)

连接（`app/connection/*`）在 工作区+用户+应用 作用域以相同方式工作，且必须在绑定请求之前完成。DotCraft 在拉起交接前要求用户确认；应用在接受前检视并授权。

### 工具暴露

- **Dynamic-first 传输。** 应用绑定工具基于 Runtime Dynamic Tools，但绑定到持久化的线程绑定（而非临时连接），因此应用重连后可重新挂载。客户端将其渲染为 `dynamicToolCall` 条目。
- **校验。** 对每个挂载工具，DotCraft 检查：插件已安装/启用、绑定对该线程可用、namespace 等于 `toolNamespace`、工具名在目录中、所授 scope 覆盖工具 scope、风险/暴露符合策略。
- **直接 vs 延迟。** `read` 可直接；`mutate` 与 `externalWrite` 默认延迟。DotCraft 可因策略或 prompt 缓存稳定性覆盖该放置。
- **审批。** 应用绑定工具复用 `DynamicToolSpec.approval`。DotCraft 在派发**之前**门控；应用在**之后**仍要校验。外部写入优先采用 提议 → 记录 → 人工批准 → 应用写入 的模式。
- **离线桩。** 绑定处于 `offline` 时，调用以结构化错误快速失败。标准错误码：`AppBindingOffline`、`AppBindingExpired`、`AppBindingRevoked`、`AppBindingScopeDenied`、`AppBindingToolUnavailable`、`AppBindingProtocolViolation`。

### App Context

Runtime `thread/start.additionalContext` 和 `thread/resume.additionalContext` 是客户端 runtime hint。它适合配合 Runtime Dynamic Tools 使用，例如连接中的客户端需要放一条短提示，让 agent 先搜索某个 deferred tool。

App Binding context blocks 使用 `app/binding/context/*`。它们是已接受绑定提供的、持久化在线程 + app 作用域下的业务上下文，例如选中的项目元数据或需要跨重连保留的应用侧状态。两者都使用 App Context prompt 语义，但生命周期和写入 API 不同。

### 安全要点

- **交接 token** 默认 10 分钟 TTL，单一用途，绑定到一个 请求/应用/工作区/用户/操作（绑定 token 还绑定线程 + scope），成功后即消费，绝不暴露给模型。
- **交接端点** 是短时的，仅允许检视/完成对应请求并保持工具通道存活——不允许任意线程执行或配置修改。
- **连接凭据** 作用域为 工作区 + 用户 + appId，仅允许 App Binding 方法。
- **授权凭据（grant proof）** 归应用所有。DotCraft 仅存储足以请应用重新校验的最小数据；把 `grantId` / `grantProof` 视为需应用侧校验的引用。
- **Deep link 是激活提示，而非授权。** 在渲染确认或接受之前，务必通过 AppServer 检视。

### 能力检查

服务端通过 `capabilities.appBinding: true` 通告。调用任何 `app/*` 或 `thread/appBindings/*` 方法前先检查它。App 上下文块（`app/binding/context/*`）需要 `capabilities.appContextBlocks`；线程输入派发（`app/threadInput/enqueue`）需要 `capabilities.appThreadInputEnqueue`。

### RPC 速查

| 领域 | 方法 |
|---|---|
| 发现 | `app/list`、`app/view` |
| 连接 | `app/connection/{start,request/get,connect,status,revoke}` |
| 绑定 | `app/binding/{request/create,request/get,request/cancel,accept,attachTools}` |
| 上下文与输入 | `app/binding/context/{upsert,remove}`、`app/threadInput/enqueue` |
| 线程管理 | `thread/appBindings/{list,revoke,refresh}`、`thread/appContextBlocks/list` |
| 通知 | `app/list/updated`、`app/connection/changed`、`thread/appBindings/changed` |

类型化的参数与返回值见 [.NET SDK](../sdks/dotnet) 或 [AppServer 协议](../protocols/appserver-protocol)。持久化的 App Binding 状态位于 `.craft/app-bindings/state.json`。

### 参见

- [SDK](../sdks/) 与 [.NET SDK](../sdks/dotnet)——客户端库与 App Binding 辅助方法。
- [AppServer 协议](../protocols/appserver-protocol)——每个 SDK 背后的线协议契约。
- [插件与工具](../../features/agent-system/plugins-tools)——插件如何打包应用。
