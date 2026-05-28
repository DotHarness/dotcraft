---
name: cpp-lsp
description: Use when setting up, repairing, or explaining the sample DotCraft C/C++ LSP plugin, including installing clangd into the plugin-local server/clangd directory and configuring C/C++ language intelligence.
---

# C/C++ LSP

Install `clangd` into this plugin before expecting the LSP server to start. Do not commit downloaded clangd files; the sample keeps generated binaries under `server/`.

From the plugin root, run the platform script:

```powershell
.\skills\cpp-lsp\scripts\prepare.ps1
```

```bash
bash ./skills/cpp-lsp/scripts/prepare.sh
```

The script downloads clangd version `22.1.0` into `server/clangd` by default. Override with `CLANGD_VERSION` or set `CLANGD_URL` to use an exact archive URL.

Requirements:

- A supported standalone clangd archive for the current platform: Windows, macOS, or Linux x64 by default.
- DotCraft `Tools.Lsp.Enabled=true`.
- A C/C++ project with `compile_commands.json`, `compile_flags.txt`, or a usable `.clangd` setup for best results.
- Refresh or restart DotCraft after installation so the plugin-local command can be loaded.

If clangd starts but diagnostics are poor, inspect the project's compile database first. The plugin installs the language server only; it does not infer compiler flags for arbitrary build systems.
