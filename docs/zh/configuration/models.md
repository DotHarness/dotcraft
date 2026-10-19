# 模型与 Provider

模型 Provider、每个 Provider 的模型偏好、推理与 prompt 缓存，以及模型能力目录。

## Provider 与模型选择

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Providers` | 个人模型 Provider 字典，通常写在 `~/.craft/config.json` | 空 |
| `ProviderId` | 当前选择的个人 Provider id。为空表示未选择 Provider | 空 |
| `ProviderPreferences` | 按 provider id 保存的完整 MainAgent 偏好。当前 provider 必须存在有效条目 | `{}` |
| `NetworkTimeoutSeconds` | 全局模型请求超时时间，单位秒。Provider 可单独覆盖 | `600` |

个人 Provider 示例：

```json
{
  "Providers": {
    "anthropic": {
      "DisplayName": "Anthropic",
      "Protocol": "anthropic",
      "ApiKey": "${ANTHROPIC_API_KEY}"
    },
    "openrouter": {
      "DisplayName": "OpenRouter",
      "Protocol": "openai-chat-completions",
      "ApiKey": "${OPENROUTER_API_KEY}",
      "EndPoint": "https://openrouter.ai/api/v1"
    }
  }
}
```

工作区模型选择示例：

```json
{
  "ProviderId": "anthropic",
  "ProviderPreferences": {
    "anthropic": {
      "Model": "claude-sonnet-4-5",
      "Reasoning": {
        "Enabled": true,
        "Effort": "High",
        "Output": "Full"
      },
      "Speed": "Fast"
    }
  }
}
```

`ProviderPreferences` 按 provider id 整条合并，不做字段级合并。全局和工作区同时配置同一 provider id 时，工作区记录完整替换全局记录。

| 偏好字段 | 可选值 | 说明 |
|----------|--------|------|
| **`Model`** | 非空模型 id | 新 MainAgent 线程使用的模型 |
| **`Reasoning.Enabled`** | `true`、`false` | 模型支持时启用或关闭 reasoning |
| **`Reasoning.Effort`** | `Low`、`Medium`、`High`、`ExtraHigh`、`Max`、`Ultra` | 请求的思考程度 |
| **`Reasoning.Output`** | `None`、`Summary`、`Full` | 请求的 reasoning 输出 |
| **`Speed`** | `Standard`、`Fast` | 请求的推理速率。不支持 Fast 时按 Standard 执行 |

Max 请求原生最高思考档位。Ultra 使用相同的 Max 思考程度，并启用主动 Dynamic Workflow 编排。

Provider 对象字段：

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `DisplayName` | 面向用户显示的 Provider 名称。为空时使用 provider id | 空 |
| `Protocol` | Provider 协议：`anthropic`、`openai-chat-completions` 或 `openai-responses`。空值默认使用 `openai-chat-completions` | `openai-chat-completions` |
| `ApiKey` | Provider API Key。建议使用 `${ENV_NAME}` 引用环境变量 | 空 |
| `AuthMethod` | 认证方式。`apiKey` 使用静态 `ApiKey`，`chatgptOAuth` 以 ChatGPT 订阅账号认证（仅限 OpenAI 协议，见下文）。无法识别的取值回退为 `apiKey` | `apiKey` |
| `EndPoint` | Provider base URL。为空时使用协议默认地址 | OpenAI 协议：`https://api.openai.com/v1`。`anthropic`：`https://api.anthropic.com` |
| `NetworkTimeoutSeconds` | 单个 Provider 请求超时时间，覆盖全局 `NetworkTimeoutSeconds` | 空 |
| `MaxOutputTokens` | 单个 Provider 的默认最大输出 token 数，请求自己没有指定时使用该值 | 空 |
| `StreamMaxRetries` | 单个 Provider 的流式响应断线重连次数，设为 `0` 可关闭 stream retry | `5` |
| `StreamIdleTimeoutMs` | 单个 Provider 的流式响应空闲超时时间，单位毫秒 | `300000` |
| `SupportsImageGeneration` | 该提供商是否支持 OpenAI Images API。省略时，ChatGPT OAuth 和使用官方 OpenAI endpoint 的 API-key 提供商默认按 `true` 处理，其他 endpoint 默认按 `false` 处理。 | 提供商默认值 |
| `SupportsFreeformTools` | 该提供商在 Responses 协议下是否接受带语法约束的 `custom` 工具。为 `false` 时，代码模式的 `CodeMode` 等工具以普通 function 工具发送。省略时，`chatgptOAuth` 提供商和使用官方 OpenAI endpoint 的 API-key 提供商默认按 `true` 处理，其他 endpoint 默认按 `false` 处理。 | 提供商默认值 |

## 使用 ChatGPT 登录

```json
{
  "Providers": {
    "openai": {
      "DisplayName": "OpenAI (ChatGPT)",
      "Protocol": "openai-responses",
      "AuthMethod": "chatgptOAuth"
    }
  }
}
```

`chatgptOAuth` Provider 以 ChatGPT 订阅账号认证，不使用 API key。先保存上面的 Provider 条目，再运行 `dotcraft auth login openai` 完成登录：它会打开浏览器，并把 OAuth token 包保存为用户数据目录下的 `auth.json`。登录不会修改配置，一次登录即可供本机所有 `chatgptOAuth` Provider 使用。这种模式下 `ApiKey` 与 `EndPoint` 会被忽略，最终生效的协议始终是 `openai-responses`，完整解析规则见[配置模型 Provider](../extend/harness/model-providers)。`dotcraft auth status openai` 显示当前登录的账号，`dotcraft auth logout openai` 会删除 token，同时退出所有 `chatgptOAuth` Provider 的登录。

## Reasoning 与 PromptCaching

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Reasoning.Enabled` | 是否请求 Provider 的推理支持 | `false` |
| `Reasoning.Effort` | 推理深度：`None` / `Low` / `Medium` / `High` / `ExtraHigh` / `Max` / `Ultra` | `Medium` |
| `Reasoning.Output` | 推理内容是否暴露在响应中：`None` / `Summary` / `Full` | `Full` |
| `PromptCaching.Enabled` | 是否为匹配模型注入 prompt cache marker | `true` |
| `PromptCaching.ModelPatterns` | 大小写不敏感的模型名片段。为空则不匹配任何模型 | `["claude"]` |
| `PromptCaching.Placement` | marker 放置策略，当前仅支持 `ConversationTail` | `ConversationTail` |
| `PromptCaching.Ttl` | Anthropic cache TTL。为空使用默认 5 分钟，`1h` 使用长缓存 | 空 |
| `PromptCaching.Warming` | 在一轮对话等待长时间工具运行或审批时刷新 Anthropic prompt cache，让下一次请求复用缓存而不是重新写入 | `true` |

Deep-thinking adapter 文件：

- 全局：`~/.craft/model-thinking-adapters.json`
- 工作区：`.craft/model-thinking-adapters.json`

内置 catalog 会为未列入的 Anthropic 协议模型开放完整思考选项，但不会假设它们支持 Anthropic `adaptive` 请求形状。只有明确支持该形状的模型或 endpoint，才应添加 `anthropicThinking` 条目。

对 Anthropic-compatible provider，`anthropicMessageContent` 可以声明 DotCraft 推理历史应如何表示。内置 DeepSeek Anthropic adapter 会在发送历史前，把历史 `TextReasoningContent` 映射为 Anthropic-compatible `thinking` block。它不是通用的 unsupported-block 过滤器。

```json
{
  "deepThinking": {
    "models": ["deepseek", "mimo", "my-thinking-model-"],
    "endpoints": ["deepseek", "my-thinking-gateway"]
  },
  "anthropicThinking": {
    "adapters": [
      {
        "models": ["my-adaptive-anthropic-model-"],
        "thinking": { "type": "adaptive", "display": "fromReasoningOutput" },
        "outputConfig": { "effort": "fromReasoningEffort" }
      }
    ]
  },
  "anthropicMessageContent": {
    "adapters": [
      {
        "models": ["deepseek"],
        "endpoints": ["deepseek"],
        "reasoningHistory": { "blockType": "thinking" }
      }
    ]
  }
}
```

## 模型能力目录

DotCraft 内置一份模型能力目录，用于配置上下文窗口和 Fast Mode 支持。使用以下文件补充或覆盖：

- 全局：`~/.craft/models.json`
- 工作区：`.craft/models.json`

```json
{
  "defaultContextWindow": 256000,
  "models": {
    "my-256k-model": {
      "contextWindow": 256000
    },
    "custom-fast-model": {
      "contextWindow": 1048576,
      "fast": {
        "protocols": ["openai-responses"]
      }
    },
    "custom-anthropic-model": {
      "fast": {
        "protocols": ["anthropic"]
      }
    }
  }
}
```

workspace 条目覆盖全局条目，全局条目覆盖内置目录。同一模型规则的字段会独立合并。将
`fast` 设为 `null` 可禁用继承的 Fast 能力。模型规则使用不区分大小写的最长前缀匹配，
也会匹配 `provider/custom-fast-model` 这样的命名空间后缀。

内置上下文窗口值从 provider-agnostic 的 [models.dev](https://models.dev/) 目录同步，范围仅限
支持工具调用、输出包含文本且上下文窗口不少于 1,000 token 的模型。规范模型 ID 的最后一段
会转换为小写 key。若某个 provider 实际提供不同限制，请在全局或 workspace 目录中覆盖。
更具体的 key 优先于家族前缀，因此具体模型可以安全地使用不同于家族的窗口值。

模型上下文容量直接来自合并后的目录。未知模型使用 `defaultContextWindow`，未配置时回退到
256,000 token。压缩流程先应用可选的客户端预算，再计算摘要预留和安全缓冲。
