# 插件、MCP 与 LSP

## 插件

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Plugins.DisabledPlugins` | 在当前工作区关闭的插件 id 列表 | `[]` |
| `Plugins.PluginRoots` | `.craft/plugins/` 之外额外维护的 plugin root 目录 | `[]` |
| `Plugins.PluginRegistries` | 用于发现插件目录的 plugin marketplace 来源 | `[]` |
| `Plugins.DisableDefaultPluginRegistry` | 忽略宿主提供的默认官方 plugin registry | `false` |

官方 DotCraft Desktop 和 Docker host 会通过 `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL` 提供默认官方插件市场。通过 Desktop 添加的市场来源保存在全局配置中。工作区中的 `PluginRegistries` 值遵循普通的工作区覆盖全局规则。Docker Stack 部署会把全局配置和 Marketplace 缓存持久化到 `state/dotcraft`。

`Plugins.PluginRegistries` 条目字段：

| 字段 | 说明 | 默认值 |
|---|---|---|
| `Name` | 市场标识。手动配置时必填且必须与市场文档一致，通过 Desktop 或 AppServer 添加时自动维护 | 空 |
| `SourceType` | 来源类型：`git`、`local` 或 `archive` | 省略时自动推断 |
| `Url` | Git URL、本地目录、归档 URL 或归档文件 | 空 |
| `Ref` | 要检出的 Git 分支、标签或 commit | 来源默认值 |
| `SparsePaths` | Git checkout 中包含的仓库内相对路径 | `[]` |
| `MarketplacePath` | 来源根目录内的市场文档路径 | `.craft/plugins/marketplace.json` |
| `LastUpdated` | 最近一次成功添加或刷新的 UTC 时间 | 空 |
| `LastRevision` | 最近一次成功获取的 Git revision | 空 |

省略 `SourceType` 时，已存在的目录或归档文件按本地来源读取，其他值按归档 URL 处理。`Ref` 和 `SparsePaths` 只适用于 Git 来源。

来源格式、市场文档和生命周期见[插件市场](../extend/plugin-market)。

本地插件开发覆盖示例：

```json
{
  "Plugins": {
    "PluginRoots": ["/path/to/local/plugins"]
  }
}
```

## 插件设置文件

插件自定义设置不放在主 `config.json` 的 `Plugins` 下。插件在 `.craft-plugin/plugin.json` 中声明 `"settings": "./settings.schema.json"`，宿主读取两个独立文件：

| 作用域 | 路径 |
|---|---|
| 个人 | `<UserDataPath>/plugin-config.json`，官方应用使用 `~/.craft/plugin-config.json` |
| 工作区 | `<DataPath>/plugin-config.json`，默认是 `<workspace>/.craft/plugin-config.json` |

根对象直接以 canonical plugin id 为键：

```json
{
  "acme.review-core": {
    "checklistLimit": 5,
    "tone": "concise"
  }
}
```

有效设置依次解析 schema 默认值、个人值和工作区值。对象递归合并，数组与标量整体替换。namespace 一旦包含未声明字段或非法值，就会整体失效。移除工作区值后，会重新显露下层的个人值或 schema 默认值。

这些文件只用于小型 JSON 配置，不能存放 blob、数据库或缓存。插件数据单独保存在：配置了 `UserDataPath` 时使用 `<UserDataPath>/plugin-data/<id>`，否则使用 `<DataPath>/plugin-data/<id>`。禁用、移除或重装插件都不会删除其配置 namespace 或数据目录。

## MCP 服务

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `McpServers` | MCP 服务配置集合 | `{}` |
| `Tools.DeferredLoading.Strategy` | 工具延迟加载策略：`Off`、`Auto`、`Simulated` 或 `Native` | `Auto` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | 始终预加载的 MCP 工具名列表 | `[]` |
| `Tools.DeferredLoading.DeferThreshold` | MCP 工具数量达到该阈值后才延迟加载 MCP 工具 | `10` |
| `Tools.DeferredLoading.MaxSearchResults` | 每次延迟工具搜索最多返回的结果数 | `5` |

每个 `McpServers` 条目只接受当前 schema 定义的字段。出现未知属性时，配置解析会失败。

MCP 示例：

```json
{
  "McpServers": {
    "everything": {
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-everything"]
    }
  },
  "Tools": {
    "DeferredLoading": {
      "Strategy": "Auto",
      "DeferThreshold": 10
    }
  }
}
```

`Tools.DeferredLoading.Strategy = Auto` 时，所有模式都使用规范名称 `SearchTools`。OpenAI Responses 将它映射为 Provider 的 client-executed `tool_search` wire 类型，Anthropic 返回原生 tool reference，chat-completions 则在下一次模型请求中注入已发现的 schema。

### 预注册 OAuth 客户端

如果 HTTP MCP 服务要求使用已有的 OAuth 应用，在设置 → MCP 中打开该服务器，将 **OAuth → 客户端注册** 设为 **预注册**。填写客户端 ID，选择密钥值或环境变量，并设置在服务中注册的回调 URL。也可以直接配置文件：

```json
{
  "McpServers": {
    "registered-service": {
      "transport": "http",
      "url": "https://mcp.example.com/mcp",
      "oauth": {
        "clientId": "your-registered-client-id",
        "clientSecretEnvVar": "MCP_CLIENT_SECRET",
        "callbackUrl": "http://127.0.0.1:8080/callback/"
      }
    }
  }
}
```

在启动 DotCraft 宿主前设置引用的环境变量。`clientSecretEnvVar` 优先于 `clientSecret`，引用的变量缺失或为空时会失败，不会回退到明文密钥。URL 已包含端口时无需填写 `callbackPort`。固定回调端口必须可用。不配置 OAuth 时，继续使用现有的自动注册流程。保存服务后，使用认证操作完成授权。

## LSP

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `LspServers` | LSP 服务配置集合 | `{}` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |
| `Tools.Lsp.MaxFileSize` | LSP 打开或同步文件时允许的最大文件大小 | `10485760` |

每个 `LspServers` 条目同样只接受当前 schema 定义的字段。
