# 托管 DotCraft Harness

DotCraft Harness 遵循标准的 .NET Generic Host 生命周期。应用负责构造 Host，在依赖准备完成后启动它，并在应用关闭时停止它。

## 构建 Host

在配置服务集合时注册 Harness。传入的 `AppConfig` 必须已经是这个 Host 最终使用的配置。

```csharp
using DotCraft.Configuration;
using DotCraft.Harness;
using Microsoft.Extensions.Hosting;

AppConfig appConfig = LoadApplicationConfig();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = Directory.GetCurrentDirectory();
});

using var host = builder.Build();
```

`AddDotCraftHarness` 在注册阶段就解析并校验路径选项，workspace 或数据目录不合法会立即抛出异常。构建 Host 创建服务容器，Runtime 持有的 workspace 状态则在 Host 启动时初始化。

## 启动与停止

先启动 Host，再解析依赖已初始化 Runtime 的服务。Host 启动之前解析 `ISessionService` 会抛出异常。应用资源释放前应先停止 Host。

```csharp
await host.StartAsync(cancellationToken);

var sessions = host.Services.GetRequiredService<ISessionService>();
// 在 Host 运行期间使用会话服务。

await host.StopAsync(cancellationToken);
```

如果 Console 应用不需要自己的运行循环，可以用 `RunAsync` 管理完整的等待与关闭流程：

```csharp
await host.RunAsync(cancellationToken);
```

> [!CAUTION]
> 不要在注册期间构建临时服务容器来解析 Harness 服务。请在最终 Host 构建完成后从 `host.Services` 解析。

## 集成桌面应用生命周期

WPF 和 WinUI 3 应用可以把 Host 作为应用状态持有。在应用启动流程中启动它，在退出流程中停止它。

```csharp
public sealed class AgentHost : IDisposable
{
    private readonly IHost _host;

    public AgentHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public Task StartAsync(CancellationToken ct = default) =>
        _host.StartAsync(ct);

    public Task StopAsync(CancellationToken ct = default) =>
        _host.StopAsync(ct);

    public void Dispose() => _host.Dispose();
}
```

从 UI 框架提供的启动与退出 Hook 调用这些方法。

## 选择服务生命周期

| 服务 | 所有权建议 |
| --- | --- |
| `IHost` | 每个由应用持有的 Harness 实例使用一个。 |
| `WorkspaceRuntime` | 由 Harness 注册，由 Host 持有。 |
| `ISessionService` | 从运行中的 Host 解析，并在当前 workspace 内复用。 |
| 应用 UI 服务 | 需要 Harness 依赖时，在同一服务集合中注册。 |

如果应用需要多套独立配置的 Runtime，请创建不同的 Host。每个实例都必须明确持有自己的 workspace 与数据路径。

## 处理启动失败

把 Host 启动视为应用的初始化步骤。在开始接收用户任务前，先暴露依赖缺失与配置错误。

```csharp
try
{
    await host.StartAsync(cancellationToken);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "DotCraft Harness failed to start.");
    throw;
}
```

## 相关文档

- [配置与路径](./configuration-paths)——`WorkspacePath`、`DataPath`、`UserDataPath` 的语义与校验规则。
- [线程与轮次](./threads-turns)——Host 启动之后如何创建对话并消费流式事件。
