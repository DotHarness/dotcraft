# .NET SDK 参考

`DotCraft.Sdk` 的包标识与语言特定细节。如何使用请从[快速开始](./quickstart)入手。

## 包

| | |
|---|---|
| 包名 | `DotCraft.Sdk`（NuGet） |
| 目标框架 | `net10.0` |
| 序列化 | `System.Text.Json`，camelCase web 默认（`DotCraftJson.Options`） |

```bash
dotnet add package DotCraft.Sdk
```

## 命名空间

| 命名空间 | 公开面 |
|----------|--------|
| `DotCraft.Sdk.AppServer` | `DotCraftClient`、`DotCraftThread`、`DotCraftRunEvent`/`DotCraftRunResult`、options、线程/轮次/模型 wrapper、动态工具模型、typed error。 |
| `DotCraft.Sdk.AppBinding` | `AppBindingHandoff`、`DotCraftAppBindingClient`、`AppBindingErrorCodes`。 |
| `DotCraft.Sdk.Hub` | `HubClient`、Hub DTO、`HubClientException`。 |
| `DotCraft.Sdk.Wire` | `IJsonRpcTransport`、`DotCraftWireClient`、stdio/WebSocket 传输、`JsonRpcException`。 |

## 连接

`DotCraftClient.ConnectLocalAsync`（Hub 托管）、`ConnectRemoteAsync`（已知 WebSocket）、`ConnectAsync`（自定义 `IJsonRpcTransport`，用于测试与嵌入式 host）。审批/用户输入处理器及能力开关在 `DotCraftClientOptions` 上设置。

## 验证

```powershell
cd sdk/dotnet
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```

## 参见

- [快速开始](./quickstart) · [线程与运行](./runs) · [工具与审批](./tools) · [构建应用](../integrations/build-an-app)
- .NET 绑定规范：`specs/sdk/dotnet.md`
