# C/C++ LSP 插件示例

这个示例通过插件提供安装 skill，并声明 C 和 C++ 文件的 LSP server。

## 前置要求

仓库不会包含 `clangd` binary。复制插件后，使用内置的 `cpp-lsp` skill，或运行：

```powershell
.\skills\cpp-lsp\scripts\prepare.ps1
```

```bash
bash ./skills/cpp-lsp/scripts/prepare.sh
```

脚本会把 clangd 下载到 `server/clangd`。为了获得较好的 C/C++ 体验，项目应提供 `compile_commands.json`、`compile_flags.txt` 或 `.clangd`。

## 试用

把这个目录复制到工作区插件目录：

```powershell
Copy-Item -Recurse samples/plugins/cpp-lsp .craft/plugins/cpp-lsp
```

然后在 Desktop 中启用 LSP 工具，或在 `.craft/config.json` 中设置 `Tools.Lsp.Enabled`。
