# Review Tools MCP 插件示例

这个示例展示当前 DotCraft 插件形态：

- `skills/` 下的插件内置 Skills
- `.mcp.json` 中的插件随附 MCP 配置
- `.craft-plugin/plugin.json` 中的可选界面元数据

插件 manifest 不再声明原生 `tools`、`functions` 或 `processes`。可复用的可执行能力应该通过 MCP 暴露；线程级客户端回调应该使用 AppServer Runtime Dynamic Tools。

## 试用

把这个目录复制到工作区插件目录：

```powershell
Copy-Item -Recurse samples/plugins/review-tools-mcp .craft/plugins/review-tools-mcp
```

内置的示例 MCP server 不需要额外依赖，复制后可以直接试用。如果要接入真实 review 服务，再编辑 `.mcp.json`，让 `review` server 指向你的 MCP 命令或 HTTPS endpoint。插件 MCP 配置中命令行参数推荐使用 `arguments`；DotCraft 读取已有配置时也兼容 `args` 等常见 MCP 别名。
