# Binding MCP Apps sample

This sample is a minimal binding-scoped Streamable HTTP MCP server. It exposes one tool and one
`text/html;profile=mcp-app` resource. It does not attach tools through App Binding: DotCraft learns
the tool and its presentation exclusively from the binding MCP session.

```powershell
dotnet run --project sdk/dotnet/samples/InteractiveToolSample -- 5199 optional-one-time-bearer
```

Use an authenticated app-principal connection to submit the printed endpoint and bearer with
`app/binding/activate`. The endpoint is loopback HTTP, which is allowed by the App Binding
transport policy. A production app should use HTTPS for non-loopback endpoints and rotate the
bearer on every rebind.
