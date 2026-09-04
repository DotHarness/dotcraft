# DotCraft Harness 总览

DotCraft Harness 将 DotCraft 的 Agent Runtime 嵌入你的 .NET 进程。应用负责 Host、配置、workspace、生命周期和用户体验，Harness 提供 Agentic Loop、持久化会话、工具、审批与模型集成。

当 Console 应用、桌面应用、服务或测试环境需要直接运行 Agent，而不是把这项职责交给另一个进程时，使用 Harness。

![DotCraft Harness 进程内拓扑图](/harness-runtime-topology.svg)

## 它如何工作

Harness 通过一个公共入口组合运行 DotCraft 所需的服务。

| 能力 | Harness 的职责 | 应用的职责 |
| --- | --- | --- |
| 托管 | 在 .NET Generic Host 中注册 Runtime 服务。 | 构建、启动、停止并释放 Host。 |
| 配置 | 使用最终生效的 `AppConfig`。 | 在注册前加载或构造配置。 |
| 路径 | 验证并提供 workspace 与可选用户数据根目录。 | 选择 workspace 和应用持有的数据位置。 |
| 会话 | 提供持久化 Thread、Turn、Item 与事件流。 | 将应用用户和 UI 操作映射到会话操作。 |
| 扩展 | 组合内置 Provider 与应用工具。 | 提供凭据、自定义工具与审批交互。 |

## 最小组合

先准备 `AppConfig`，再将 Harness 加入 Generic Host。配置可以来自文件、环境变量、数据库或应用自己的设置系统。

```csharp
using DotCraft.Configuration;
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

AppConfig appConfig = LoadApplicationConfig();
var workspacePath = Directory.GetCurrentDirectory();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});

using var host = builder.Build();
await host.StartAsync();

// 在这里解析并使用 Harness 服务。

await host.StopAsync();
```

`AddDotCraftHarness` 会注册 Runtime、内置配置 schema、OpenAI 与 Anthropic 模型 Provider、一个经过验证的 `DotCraftPaths`，以及一个由 Host 持有的 `ISessionService`。Runtime 通过一个 `IHostedService` 接入 Host 生命周期，在 Host 启动时完成初始化。

> [!TIP]
> 将组合逻辑留在应用边界。领域服务应依赖 `ISessionService` 或 `DotCraftPaths` 等专用服务，而不是依赖 Host 本身。

## 继续了解 Harness

- [托管与生命周期](./hosting-lifecycle)介绍 Generic Host 的所有权与桌面应用集成。
- [配置与路径](./configuration-paths)说明配置所有权、`.craft`、自定义数据目录与用户数据隔离。
- [线程与轮次](./threads-turns)展示如何创建持久化对话并处理流式事件。
- [工具与审批](./tools-approvals)介绍应用自有工具与审批处理。
- [模型 Provider](./model-providers)介绍 OpenAI、Anthropic 与兼容端点的配置。
- [NuGet 包](./nuget-package)介绍安装方式与包内附带的程序集。

`$dotcraft-api` 同样覆盖进程内托管这条路线。嵌入 Harness 时，可以在 DotCraft 对话里直接问它。

## 查看桌面集成示例

仓库包含 **DotCraft Trace Viewer**。这是一个把 `DotCraft.Harness` 嵌入 WinUI 3 应用的示例，用于审查持久化的 Agent Trace。它用 Timeline 呈现执行过程，用 Finding 关联证据，并保持被检查的 workspace 只读。

Trace Viewer 是集成示例，不是受支持的 DotCraft 客户端产品。从源码运行：

```bash
dotnet run --project src/DotCraft.TraceViewer/DotCraft.TraceViewer.csproj
```

## 相关文档

- [Runtime 架构](../architecture/overview)——Harness 所嵌入的程序集分层与边界。
- [Session Core](../architecture/session-core)——`ISessionService` 背后的 Thread、Turn、Item 模型。
