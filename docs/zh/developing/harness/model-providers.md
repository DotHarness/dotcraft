# 配置模型 Provider

DotCraft Harness 内置 OpenAI 与 Anthropic 两套 Provider 集成。Host 通过 `AppConfig` 选择 Provider 和模型，再把最终生效的配置传给 [`AddDotCraftHarness`](./)。

## 配置 OpenAI

使用同一个稳定的 Provider ID 定义 Provider 记录与模型偏好：

```csharp
using DotCraft.Configuration;

const string providerId = "openai";

var appConfig = new AppConfig
{
    ProviderId = providerId,
    ProviderPreferences =
    {
        [providerId] = new ModelPreference
        {
            Model = "gpt-5.1"
        }
    },
    Providers =
    {
        [providerId] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "OpenAI",
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("Set OPENAI_API_KEY.")
        }
    }
};
```

端点实现 Chat Completions 协议时用 `ModelProviderProtocols.OpenAIChatCompletions`，实现 Responses 协议时用 `ModelProviderProtocols.OpenAIResponses`。

## 用 ChatGPT 订阅认证

OpenAI Provider 还支持第二种认证方式。`AuthMethod = "chatgptOAuth"` 用 OpenAI 的 Sign in with ChatGPT 流程代替静态 API key，请求以 ChatGPT 订阅账号的身份认证：

```csharp
appConfig.Providers["openai"] = new AppConfig.ModelProviderConfig
{
    DisplayName = "OpenAI (ChatGPT)",
    Protocol = ModelProviderProtocols.OpenAIResponses,
    AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
};
```

这种模式下 Provider 的解析规则不同：

- 协议必须是 OpenAI 协议。`anthropic` 搭配 `chatgptOAuth` 会在解析 Provider 时抛出 `ModelProviderConfigurationException`，最终生效的协议始终是 `openai-responses`。
- `ApiKey` 与 `EndPoint` 会被忽略，请求发往 ChatGPT 后端 `https://chatgpt.com/backend-api/codex`。
- 每个请求都会附带最新的 access token，来自用户数据目录下 `auth.json` 中保存的 token 包。token 自动刷新，收到 `401` 时会先采用其他进程轮换后的凭据，再向签发方刷新。

登录只需一次，且发生在 Provider 配置之外。与 OpenAI Provider 一同注册的 `IOpenAIAuthService` 通过 `LoginAsync` 执行 PKCE 浏览器登录并持久化 token。装有 DotCraft 应用的机器上，`dotcraft auth openai login` 完成同样的事，还会把 Provider 条目写进全局配置。`ChatGptAccountId` 与 `ChatGptPlanType` 由该流程写入，当作只读元数据即可。用 `LogoutAsync` 或 `dotcraft auth openai logout` 退出登录会删除 token，并把 Provider 还原为 `apiKey`。

## 配置 Anthropic

Anthropic 使用相同的 Provider 注册表与偏好结构：

```csharp
const string providerId = "anthropic";

var appConfig = new AppConfig
{
    ProviderId = providerId,
    ProviderPreferences =
    {
        [providerId] = new ModelPreference
        {
            Model = "claude-sonnet-4-5"
        }
    },
    Providers =
    {
        [providerId] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "Anthropic",
            Protocol = ModelProviderProtocols.Anthropic,
            ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY.")
        }
    }
};
```

`EndPoint` 为空时会使用所选协议的内置默认端点。

## 使用兼容端点

如果 OpenAI 兼容或 Anthropic 兼容的 Provider 由其他服务转发，可以设置 `EndPoint`：

```csharp
appConfig.Providers["company-models"] = new AppConfig.ModelProviderConfig
{
    DisplayName = "Company models",
    Protocol = ModelProviderProtocols.OpenAIChatCompletions,
    ApiKey = secretStore.Get("company-models"),
    EndPoint = "https://models.example.com/v1"
};

appConfig.ProviderId = "company-models";
appConfig.ProviderPreferences["company-models"] = new ModelPreference
{
    Model = "engineering-agent"
};
```

端点必须实现 `Protocol` 选中的协议。自定义 Provider ID 只是给配置记录起名，不会定义新的传输协议——可选协议只有上面这三个。

## 为单个 Thread 选择模型

新建 Thread 时会捕获当前 workspace 最终生效的 Provider 与模型。用 `ThreadConfiguration` 可以只为这一个 Thread 覆盖它们：

```csharp
var thread = await sessions.CreateThreadAsync(
    identity,
    new ThreadConfiguration
    {
        ProviderId = "anthropic",
        Model = "claude-sonnet-4-5"
    },
    ct: cancellationToken);
```

Host 配置发生变化时，不会改写已有 Thread 的模型快照。

> [!TIP]
> 不要在 workspace 配置中保存 API key。请在 Host 中解析 Secret，并在组合前构造最终生效的 `AppConfig`。

## Provider 注册

`AddDotCraftHarness` 以幂等方式注册两个内置 Provider 实现，重复注册不会产生重复项。

每个协议只能有一个 `IModelProvider`。同一协议出现多个实现时，Runtime 会在组合期间抛错，Host 启动失败。

## 相关文档

- [配置与路径](./configuration-paths)——应用如何在注册之前准备最终生效的 `AppConfig`。
- [线程与轮次](./threads-turns)——Thread 在哪一步捕获 Provider 与模型快照。
