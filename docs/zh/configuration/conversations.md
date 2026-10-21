# 对话

对话运行中的排队与中断、输入建议、上下文压缩，以及自定义命令。

## 排队与中断

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `MaxSessionQueueSize` | 每个 Session 最大排队请求数，`0` 表示无限制 | `3` |
| `InstantInterruptEnabled` | 运行中的 turn 收到消息时，即使模型正在思考或回复，也立即打断当前请求并带上该消息重新发起；正在等待重试时同样立即重发 | `true` |
| `AgentInterruptMessageEnabled` | 向模型说明 turn 被主动中断，包括从运行中的 turn 分叉 | `true` |

## 建议

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `WelcomeSuggestions.Enabled` | 根据工作区记忆生成欢迎页建议，依赖 `Memory.Enabled` | `true` |
| `PromptSuggestions.Enabled` | 一次对话结束后，在空输入框中建议下一条消息，不依赖工作区记忆 | `true` |

两项设置都可在 **设置 → 个性化** 中修改。输入框建议可以编辑，只有用户提交后才会发送。

## 上下文压缩

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Compaction.AutoCompactEnabled` | 启用基于阈值的自动压缩 | `true` |
| `Compaction.ReactiveCompactEnabled` | 启用对 `prompt_too_long` 错误的反应式压缩 | `true` |
| `Compaction.MaxContextWindow` | 客户端上下文预算（token）。`-1` 使用模型目录窗口，正数限制使用窗口 | `-1` |
| `Compaction.SummaryReserveTokens` | 为摘要输出预留的 Token | `20000` |
| `Compaction.SummaryMaxOutputTokens` | 压缩摘要请求的最大输出 Token 数 | `12000` |
| `Compaction.AutoCompactBufferTokens` | 低于硬上限多少 Token 时触发自动压缩 | `13000` |
| `Compaction.PostTurnCompactThresholdPercent` | 达到有效上下文窗口的该百分比时，Turn 结束也触发压缩，`0` 表示禁用 | `0` |
| `Compaction.WarningBufferTokens` | 到达自动阈值前多少 Token 发出 warning | `20000` |
| `Compaction.ErrorBufferTokens` | 到达自动阈值前多少 Token 发出 error | `10000` |
| `Compaction.ManualCompactBufferTokens` | 上报上下文压力上限时，预留在有效上下文窗口之下的余量 | `3000` |
| `Compaction.KeepRecentMinTokens` | 局部摘要后尾部至少保留的 Token 数 | `10000` |
| `Compaction.KeepRecentMinGroups` | 局部摘要后尾部至少保留的 API 轮次数 | `3` |
| `Compaction.KeepRecentMaxTokens` | 局部摘要后尾部最多保留的 Token 数 | `40000` |
| `Compaction.MicrocompactEnabled` | 启用微压缩 | `true` |
| `Compaction.MicrocompactKeepRecent` | 微压缩时保留的最近工具结果数 | `8` |
| `Compaction.MicrocompactGapMinutes` | 距离上次助理消息超过该分钟数也触发微压缩，`0` 表示禁用 | `20` |
| `Compaction.MaxConsecutiveFailures` | 连续失败次数达到该值时熔断 | `3` |

在全局或工作区的 `config.json` 中设置上下文预算。例如，将客户端使用窗口限制为 256K token：

```json
{
  "Compaction": {
    "MaxContextWindow": 256000
  }
}
```

未填写时使用模型目录窗口。工作区配置覆盖全局配置，在工作区将 `MaxContextWindow` 设为 `-1`
可清除继承的限制。预算不能扩大模型容量。摘要预留和安全缓冲会在预算基础上继续扣除，
因此该值不是自动压缩的精确触发点。思考程度不会改变这个预算。

模型的上下文窗口来自[模型能力目录](./models#模型能力目录)。

## 自定义命令

自定义命令是 Markdown 文件，不是 `config.json` 里的字段。DotCraft 从全局 `~/.craft/commands/` 目录和工作区 `.craft/commands/` 目录加载它们，同名时工作区文件优先。每个文件对应一条 `/name` 命令，可以在 CLI、Desktop 和其他入口使用。

命令文件开头可以写 YAML frontmatter，发送前会被剥离。正文中 `$ARGUMENTS` 展开为完整参数串，`$1` 到 `$9` 展开为位置参数。
