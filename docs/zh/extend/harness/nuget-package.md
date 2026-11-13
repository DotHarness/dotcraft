# 安装 DotCraft Harness 包

当应用需要在同一个 .NET 进程中托管并定制 DotCraft 时，请安装 `DotCraft.Harness`。

## 安装

将 Harness 与 .NET Generic Host 实现加入应用项目：

```bash
dotnet add package DotCraft.Harness
dotnet add package Microsoft.Extensions.Hosting
```

应用需要以 .NET 10 或更高版本为目标框架。

`DotCraft.Harness` 以包依赖的形式带入 Runtime、Core，以及内置的 OpenAI、Anthropic、OpenRouter 与远程模型服务 Provider。所有 DotCraft 包共用同一个版本号，请让应用中的 DotCraft 包引用保持同一版本。

## 按需选择更少的包

每个 DotCraft 程序集也都单独发布为一个包。应用需要的能力更少时，可以改为引用更底层的包，而不引用 Harness：

| 需求 | 包 |
| --- | --- |
| 只用模型 Provider 与 chat client | `DotCraft.Agents.OpenAI`、`DotCraft.Agents.Anthropic`、`DotCraft.Agents.OpenRouter` 中的一个或多个 |
| 在自己的组合中使用 Session 内核 | `DotCraft.Core` 与所用的 Provider |
| Generic Host 生命周期、配置校验与插件 | `DotCraft.Runtime` 与所用的 Provider |

可选功能是独立的包，由宿主显式注册：

| 功能 | 包 | 注册方式 |
| --- | --- | --- |
| Code Mode | `DotCraft.CodeMode` | `AddDotCraftCodeMode`，见[让模型用脚本调用工具](./tools-approvals#让模型用脚本调用工具) |
| 由 Agent 编写 .NET 扩展 | `DotCraft.CSharp` | `AddDotCraftCSharp`，见[开发 .NET 扩展](../dotnet-extensions#使用-dotcraft-创建) |

## 注册 Harness

安装后先准备 `AppConfig`，再把 Harness 注册到应用服务集合：

```csharp
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

接着按[托管与生命周期](./hosting-lifecycle)构建并启动 Host。

## 集成场景

| 应用 | 常见集成方式 |
| --- | --- |
| Console 工具 | 启动一个 Host，运行一个或多个 Thread，然后停止。 |
| WPF 或 WinUI 3 | 将 Host 的启动与停止绑定到应用生命周期。 |
| 后台服务 | 在同一个 Generic Host 中注册应用服务与 Harness。 |
| 集成测试 | 使用临时路径和由测试持有的模型 Provider 配置。 |

无论使用哪种 Host 模型，应用都负责渲染流式事件、收集审批，以及决定配置的存储方式。

## 相关文档

- [托管与生命周期](./hosting-lifecycle)——安装之后如何构建、启动并停止 Host。
- [配置与路径](./configuration-paths)——注册时必须给出的 `AppConfig` 与路径选项。
