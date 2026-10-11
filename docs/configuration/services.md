# Entry points and services

Switches and listen addresses for ACP, Dashboard, and AppServer, plus operational logging. For AppServer in a running host, read [AppServer mode](../contribute/lifecycle/appserver); channel registration is in [Channels](./channels).

## Entry points

| Field | Description | Default |
|-------|-------------|---------|
| `Acp.Enabled` | Enables ACP mode | `false` |
| `DashBoard.Enabled` | Enables Dashboard | `true` |
| `DashBoard.Host` | Dashboard listen address | `127.0.0.1` |
| `DashBoard.Port` | Dashboard listen port | `8080` |
| `AppServer.Mode` | AppServer transport mode: `Disabled`, `Stdio`, `WebSocket`, or `StdioAndWebSocket` | `Disabled` |
| `AppServer.WebSocket.Host` | WebSocket listen host | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket listen port | `9100` |
| `AppServer.WebSocket.Token` | Token required by remote WebSocket clients | Empty |

Dashboard example:

```json
{
  "DashBoard": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 8080
  }
}
```

## Logging

DotCraft writes workspace host diagnostics to `<workspace>/.craft/logs` and Hub
diagnostics to `~/.craft/logs`.

| Field | Description | Default |
|---|---|---|
| **`Logging.Enabled`** | Writes operational diagnostics to rolling files | `true` |
| **`Logging.Console`** | Also writes diagnostics to the console. Protocol hosts use stderr so stdout remains protocol-only | `false` |
| **`Logging.MinLevel`** | Minimum level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical` | `Information` |
| **`Logging.Directory`** | Log directory relative to the host's `.craft` directory | `logs` |
| **`Logging.RetentionDays`** | Deletes older rolled files at startup; `0` disables cleanup | `7` |
| **`DebugMode`** | Prints untruncated tool arguments in the console | `false` |

```json
{
  "Logging": {
    "Enabled": true,
    "Console": false,
    "MinLevel": "Information",
    "Directory": "logs",
    "RetentionDays": 7
  }
}
```

Operational logs contain timestamps, severity, process ID, category, messages, exceptions, and
active diagnostic scopes. Raw ACP traffic and opt-in session stream debug records use separate
files because they can contain sensitive or high-volume payloads.
