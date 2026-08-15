# 配置模型 Provider

DotCraft Harness 包含 OpenAI 与 Anthropic Provider 集成。Host 通过 `AppConfig` 选择 Provider 和模型，再将最终生效的配置传给 `AddDotCraftHarness`。

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

实现 Chat Completions 协议的端点应使用 `ModelProviderProtocols.OpenAIChatCompletions`。实现 Responses 协议的端点应使用 `ModelProviderProtocols.OpenAIResponses`。

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

端点必须实现 `Protocol` 选择的协议。自定义 Provider ID 只是为配置记录命名，不会定义新的传输协议。

## 为单个 Thread 选择模型

新 Thread 会捕获当前 workspace 最终生效的 Provider 与模型。可以通过 `ThreadConfiguration` 为单个 Thread 覆盖它们：

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

Host 配置发生变化时，不会静默重写已有 Thread 的模型快照。

::: tip
不要在 workspace 配置中保存 API key。请在 Host 中解析 Secret，并在组合前构造最终生效的 `AppConfig`。
:::

## Provider 注册

`AddDotCraftHarness` 会以幂等方式注册两个内置 Provider 实现。

每个协议只能注册一个 `IModelProvider`。如果同一协议存在多个实现，Runtime 会在组合期间拒绝启动。

## 相关文档

- [Harness 总览](./)
- [配置与路径](./configuration-paths)
- [线程与轮次](./threads-turns)
