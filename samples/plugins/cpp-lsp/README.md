# C/C++ LSP Plugin Sample

This sample contributes a setup skill and plugin-bundled LSP server declaration for C and C++ files.

## Requirements

The repository does not include the `clangd` binary. After copying the plugin, use the bundled `cpp-lsp` skill or run:

```powershell
.\skills\cpp-lsp\scripts\prepare.ps1
```

```bash
bash ./skills/cpp-lsp/scripts/prepare.sh
```

The script downloads clangd into `server/clangd`. C/C++ projects should provide `compile_commands.json`, `compile_flags.txt`, or a `.clangd` file for best results.

## Try it

Copy this folder into a workspace plugin root:

```powershell
Copy-Item -Recurse samples/plugins/cpp-lsp .craft/plugins/cpp-lsp
```

Then enable the LSP tool from Desktop or by setting `Tools.Lsp.Enabled` in `.craft/config.json`.
