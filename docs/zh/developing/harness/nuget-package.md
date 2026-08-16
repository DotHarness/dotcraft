# 安装 DotCraft Harness 包

当应用需要在同一个 .NET 进程中托管并定制 DotCraft 时，请安装 `DotCraft.Harness`。

## 安装

将 Harness 与 .NET Generic Host 实现加入应用项目：

```bash
dotnet add package DotCraft.Harness
dotnet add package Microsoft.Extensions.Hosting
```

应用必须以 .NET 10 或兼容的目标框架为目标。

## 注册 Harness

安装后，先准备 `AppConfig`，再将 Harness 注册到应用服务集合：

```csharp
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

接下来可以阅读[托管与生命周期](./hosting-lifecycle)，构建并启动 Host。

## 集成场景

| 应用 | 常见集成方式 |
| --- | --- |
| Console 工具 | 启动一个 Host，运行一个或多个 Thread，然后停止。 |
| WPF 或 WinUI 3 | 将 Host 的启动与停止绑定到应用生命周期。 |
| 后台服务 | 在同一个 Generic Host 中注册应用服务与 Harness。 |
| 集成测试 | 使用临时路径和由测试持有的模型 Provider 配置。 |

无论使用哪种 Host 模型，应用都负责渲染流式事件、收集审批，以及决定配置的存储方式。

## 相关文档

- [Harness 总览](./)
- [托管与生命周期](./hosting-lifecycle)
- [配置与路径](./configuration-paths)
