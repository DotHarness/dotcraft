# C# LSP Plugin Sample

This sample contributes a setup skill and plugin-bundled LSP server declaration for C# files.

## Requirements

The repository does not include the `csharp-ls` binary. After copying the plugin, use the bundled `csharp-lsp` skill or run:

```powershell
.\skills\csharp-lsp\scripts\prepare.ps1
```

```bash
bash ./skills/csharp-lsp/scripts/prepare.sh
```

The script installs `csharp-ls` into `server/csharp-ls`. It requires .NET 10 or a compatible runtime for the selected `csharp-ls` version.

## Try it

Copy this folder into a workspace plugin root:

```powershell
Copy-Item -Recurse samples/plugins/csharp-lsp .craft/plugins/csharp-lsp
```

Then enable the LSP tool from Desktop or by setting `Tools.Lsp.Enabled` in `.craft/config.json`.
