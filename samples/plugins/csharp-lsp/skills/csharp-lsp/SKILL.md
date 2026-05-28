---
name: csharp-lsp
description: Use when setting up, repairing, or explaining the sample DotCraft C# LSP plugin, including installing csharp-ls into the plugin-local server/csharp-ls directory and enabling C# language intelligence.
---

# C# LSP

Install `csharp-ls` into this plugin before expecting the LSP server to start. Do not install the server globally for this sample unless the user explicitly asks for a PATH-based setup.

From the plugin root, run the platform script:

```powershell
.\skills\csharp-lsp\scripts\prepare.ps1
```

```bash
bash ./skills/csharp-lsp/scripts/prepare.sh
```

The script installs `csharp-ls` version `0.24.0` into `server/csharp-ls` by default. Override with `CSHARP_LS_VERSION` when the user needs a different version.

Requirements:

- .NET 10 SDK or a compatible runtime capable of running the selected `csharp-ls` version.
- DotCraft `Tools.Lsp.Enabled=true`.
- Refresh or restart DotCraft after installation so the plugin-local command can be loaded.

If the server fails to start, first verify `server/csharp-ls/csharp-ls` or `server/csharp-ls/csharp-ls.exe` exists, then run `dotnet --info` and check that the selected `csharp-ls` version is compatible with the installed .NET runtime.
