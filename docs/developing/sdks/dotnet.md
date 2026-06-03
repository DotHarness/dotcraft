# .NET SDK reference

Package identity and language-specific details for `DotCraft.Sdk`. For how-to, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `DotCraft.Sdk` (NuGet) |
| Target framework | `net10.0` |
| Serialization | `System.Text.Json`, camelCase web defaults (`DotCraftJson.Options`) |

```bash
dotnet add package DotCraft.Sdk
```

## Namespaces

| Namespace | Public surface |
|-----------|----------------|
| `DotCraft.Sdk.AppServer` | `DotCraftClient`, `DotCraftThread`, `DotCraftRunEvent`/`DotCraftRunResult`, options, thread/turn/model wrappers, dynamic tool models, typed errors. |
| `DotCraft.Sdk.AppBinding` | `AppBindingHandoff`, `DotCraftAppBindingClient`, `AppBindingErrorCodes`. |
| `DotCraft.Sdk.Hub` | `HubClient`, Hub DTOs, `HubClientException`. |
| `DotCraft.Sdk.Wire` | `IJsonRpcTransport`, `DotCraftWireClient`, stdio/WebSocket transports, `JsonRpcException`. |

## Connection

`DotCraftClient.ConnectLocalAsync` (Hub-managed), `ConnectRemoteAsync` (known WebSocket), and `ConnectAsync` (custom `IJsonRpcTransport`, for tests and embedded hosts). Approval and user-input handlers, and capability flags, are set on `DotCraftClientOptions`.

## Validation

```powershell
cd sdk/dotnet
dotnet test .\DotCraft.Sdk.sln
dotnet pack .\src\DotCraft.Sdk\DotCraft.Sdk.csproj -c Release
```

## See also

- [Quickstart](./quickstart) · [Threads & runs](./runs) · [Tools & approvals](./tools) · [Build an App](../integrations/build-an-app)
- .NET binding spec: `specs/sdk/dotnet.md`
