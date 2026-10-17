# 配置

本节每页列出一类配置的字段、默认值和 JSON 示例。第一次配置请先读[快速开始](../start/)。想知道某个功能做什么、什么时候用它，先读对应的功能页，需要准确字段时再来这里。

## 配置文件

DotCraft 先读取全局 `~/.craft/config.json`，再叠加工作区 `.craft/config.json`，工作区字段优先生效。配置字符串支持 `$VAR` 和 `${VAR}` 环境变量占位。变量不存在时保留原始占位符。

有些设置有自己的文件，放在同样的两个位置：全局在 `~/.craft/` 下，工作区在 `.craft/` 下，工作区优先。

| 文件 | 内容 | 参考 |
|---|---|---|
| `config.json` | 绝大多数设置 | 本节各页 |
| `hooks.json` | 生命周期 hooks | [Hooks](./hooks) |
| `models.json` | 模型上下文窗口与 Fast Mode 支持 | [模型与 Provider](./models#模型能力目录) |
| `model-thinking-adapters.json` | 推理请求的形状 | [模型与 Provider](./models#reasoning-与-promptcaching) |
| `plugin-config.json` | 插件声明的设置 | [插件、MCP 与 LSP](./plugins#插件设置文件) |
| `commands/` | 自定义 `/` 命令 | [对话](./conversations#自定义命令) |
| `<渠道>.json` | 渠道适配器，仅工作区 | [渠道](./channels) |

改了设置之后何时生效，见[设置生效层级](./settings-lifecycle)。

## 从 CLI 查看配置

`dotcraft config schema` 打印配置 schema 中的配置节和字段，包括类型、默认值、是否敏感和重载方式。`dotcraft config show` 打印某个工作区合并后的配置，其中 `ApiKey`、`Password`、`Token` 的值会被遮蔽为 `***`。

```bash
dotcraft config schema --section Tools.Web
dotcraft config schema --json
dotcraft config show --json
```

`--section` 接受配置节的显示名或 JSON 路径。`--json` 输出机器可读的格式，`config show` 无论是否加 `--json` 都是缩进的 JSON。`config show` 默认读取当前目录的工作区，用 `--workspace` 指定其他目录。

## 从客户端修改配置

AppServer 客户端按 key path 修改设置，而不是直接编辑文件。`config/read` 返回遮蔽了敏感值的生效配置，也可以同时返回用户层和工作区层。`config/value/write` 修改一个 key path，例如 `Tools.CodeMode.Mode`，`config/batchWrite` 一次把多处修改写入同一层。写入默认落在工作区层，指定用户层的文件路径时才写入用户层。API Key、Token、密码等敏感值只能写入、不能读回：写入会保存新值，`config/read` 将其显示为 `***`，把 `***` 原样写回则保留已存储的值。模型 Provider 的定义仍通过 Provider 相关方法修改。参数和错误码见 [AppServer 协议](../extend/protocols/appserver)。

服务端在保存前校验写入：结果必须仍能加载为配置，每个变更字段都要满足其类型、范围和可选值。被拒绝的写入不会改动文件。通过校验的写入会按各字段的重载方式更新正在运行的 AppServer，并通过 `config/changed` 广播变更的 key path。

Dashboard 设置页通过同一路径保存整层配置文件，因此它的保存同样会被校验、应用和广播。保存的文档中被遮蔽为 `***` 的值会保留已存储的密钥。
