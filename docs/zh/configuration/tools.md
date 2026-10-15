# 工具

内置工具的上限与开关。Agent 能碰什么由[权限](./permissions)决定。

## 文件与 Shell

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Tools.File.MaxFileSize` | 最大可读取文件大小（字节） | `10485760` |
| `Tools.File.RipgrepPath` | 可选 `rg` 路径。为空时依次尝试 `DOTCRAFT_RG_PATH`、`PATH` 和内置回退 | `""` |
| `Tools.File.SearchTimeoutSeconds` | `GrepFiles` 内容搜索最长运行时间，超时后返回超时结果 | `30` |
| `Tools.Shell.Timeout` | agent 等待命令的最长时间（秒），超时后停止命令。运行超过 `Tools.Shell.Background.ForegroundBudgetMs` 的命令会改为把会话交还给 agent，并继续运行直到结束或被停止，后台终端被关闭时除外 | `300` |
| `Tools.Shell.MaxOutputLength` | Shell 命令最大输出长度（字符） | `10000` |

## 后台终端

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Tools.Shell.Background.Enabled` | 是否启用后台终端会话 | `true` |
| `Tools.Shell.Background.ForegroundBudgetMs` | agent 等待命令多久后让它转入后台继续运行。等待期间你发送的消息会让命令立即转入后台 | `120000` |
| `Tools.Shell.Background.DefaultYieldTimeMs` | 后台启动的命令返回会话前的默认等待时间 | `10000` |
| `Tools.Shell.Background.MaxYieldTimeMs` | 后台命令返回前，以及后台会话读取或写入可接受的最长等待时间 | `300000` |
| `Tools.Shell.Background.CompletionDelivery` | `wake` 会在 agent 交给后台的命令结束时通知它，线程空闲时会开启新的回合。`off` 则由 agent 自行查看命令状态 | `wake` |
| `Tools.Shell.Background.MaxConsecutiveWakes` | 后台命令结束最多能连续自动开启几个回合，之后 agent 会等你发消息 | `3` |
| `Tools.Shell.Background.MaxSessionsPerThread` | 每个线程可同时运行的后台终端上限 | `8` |
| `Tools.Shell.Background.MaxSessionsPerWorkspace` | 每个工作区可同时运行的后台终端上限 | `32` |
| `Tools.Shell.Background.IdleTimeoutSeconds` | 预留字段。后台终端服务当前不执行该限制 | `1800` |
| `Tools.Shell.Background.OutputMaxBytes` | 预留字段。后台终端服务当前不执行该限制 | `67108864` |
| `Tools.Shell.Background.OutputRetentionDays` | 已完成或丢失终端的元数据与输出保留天数，不清理正在运行的终端 | `7` |
| `Tools.Shell.Background.StallWatchdogSeconds` | 命令输出停在 `(y/n)` 这类提示行后，静默多少秒即视为在等待输入并报告。设为 `0` 关闭 | `45` |
| `Tools.Shell.Background.DefaultReadMaxOutputChars` | 终端快照默认返回的最大字符数 | `10000` |

## Web

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Tools.Web.MaxChars` | Web 抓取最大字符数 | `50000` |
| `Tools.Web.Timeout` | Web 请求超时时间（秒） | `300` |
| `Tools.Web.SearchMaxResults` | 联网搜索默认返回结果数 | `5` |
| `Tools.Web.SearchProvider` | 搜索引擎提供商：`Bing` / `Exa` | `Exa` |

## 工具结果

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Tools.ResultLimits.MaxToolResultChars` | 工具结果落盘前的默认长度上限（字符）。`0` 表示对使用全局默认值的工具不做限制 | `50000` |
| `Tools.ResultLimits.SpillPreviewLines` | 结果落盘时，预览中保留的首尾行数 | `40` |

## 图片生成

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Tools.ImageGeneration.Enabled` | 允许智能体通过支持的 OpenAI 提供商生成和编辑图片 | `true` |
| `Tools.ImageGeneration.Provider` | 负责生成图片的提供商 ID。留空时使用当前对话的提供商 | 空 |
| `Tools.ImageGeneration.Model` | 生成和编辑图片使用的图片模型 | `gpt-image-2` |
| `Tools.ImageGeneration.MaxReferenceImages` | 单次编辑最多使用的参考图数量，取值 `1` 到 `5` | `5` |

生成的图片默认保存在 Agent 数据目录的 `generated_images/<threadId>/<callId>.png`。连接远程电脑后，文件改为保存在远端工作区的 `.craft/generated_images/<threadId>/<callId>.png`。如果保存失败，对话仍会显示已生成的图片，并提示文件保存失败。

使用支持的提供商时，你可以在普通对话里直接让 DotCraft 生成图片，或编辑附件、本地文件和之前生成的图片。DotCraft 会调用 OpenAI Images API，保存 PNG 文件，并在支持富内容的客户端中以内联图片展示。

`image_gen.imagegen` 工具由两个开关共同决定，两者都为真才会提供：全局的 `Tools.ImageGeneration.Enabled`，以及图片提供商自己的 `SupportsImageGeneration`。图片提供商还必须使用 OpenAI 协议。省略提供商字段时，ChatGPT OAuth 和官方 OpenAI API-key endpoint 视为开启，其他 endpoint 视为关闭。只有确认其他 endpoint 支持 `images/generations` 和 `images/edits` 时再开启。

图片提供商默认就是当前对话的提供商，除非 `Tools.ImageGeneration.Provider` 指定了另一个。当你通过没有 Images API 的提供商对话时（例如 Anthropic 或自定义的 OpenAI 兼容 endpoint），可以用它继续保留图片生成：

```json
{
  "Tools": {
    "ImageGeneration": {
      "Provider": "openai"
    }
  }
}
```

如果指定的提供商不存在或无法生成图片，就不会提供该工具；DotCraft 不会退回到对话的提供商。

## 代码模式

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Tools.CodeMode.Mode` | 脚本化工具调用：`only`、`on` 或 `off` | `only` |

`Tools.CodeMode.Mode` 控制脚本化工具调用。`CodeMode` 工具运行一段 JavaScript 程序，在程序里调用对话中的其他工具，只把模型需要的结果返回给它。这样一批调用只需要一次模型往返，大段中间结果也不会进入上下文。设为 `only` 时，脚本能调用的工具都会从模型的直接工具列表中移除，只能通过 `CodeMode` 调用。`on` 会加入 `CodeMode`，同时保留所有工具的直接调用，`off` 则不提供 `CodeMode`。在 Desktop 中，可以在 **设置 › 常规 › 工具** 中修改。

脚本在由 DotCraft 可执行文件启动的工作进程中运行。如果工作进程无法启动，`on` 会退回为直接调用工具，而 `only` 会让本轮以 `code_mode_unavailable` 失败。宿主无法启动工作进程时，请改用 `on` 或 `off`。
