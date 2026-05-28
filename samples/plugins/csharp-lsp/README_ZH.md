# C# LSP 插件示例

这个示例通过插件提供安装 skill，并声明 C# 文件的 LSP server。

## 前置要求

仓库不会包含 `csharp-ls` binary。复制插件后，使用内置的 `csharp-lsp` skill，或运行：

```powershell
.\skills\csharp-lsp\scripts\prepare.ps1
```

```bash
bash ./skills/csharp-lsp/scripts/prepare.sh
```

脚本会把 `csharp-ls` 安装到 `server/csharp-ls`。它需要 .NET 10，或与所选 `csharp-ls` 版本兼容的 runtime。

## 试用

把这个目录复制到工作区插件目录：

```powershell
Copy-Item -Recurse samples/plugins/csharp-lsp .craft/plugins/csharp-lsp
```

然后在 Desktop 中启用 LSP 工具，或在 `.craft/config.json` 中设置 `Tools.Lsp.Enabled`。
