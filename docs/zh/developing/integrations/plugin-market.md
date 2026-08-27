# 插件市场

本页面向插件发布者、集成开发者和自托管运维人员，说明 DotCraft 的市场文档、来源类型和生命周期。

如需在 Desktop 中添加和使用市场，请阅读[插件市场](../../features/agent-system/plugin-marketplaces)。

## 仓库结构

一个市场仓库包含一份目录文件和一个或多个插件目录：

```text
marketplace-root/
├── .craft/
│   └── plugins/
│       └── marketplace.json
└── plugins/
    └── example-plugin/
        └── .craft-plugin/
            └── plugin.json
```

默认目录文件为 `.craft/plugins/marketplace.json`。其中列出的每个插件都必须指向市场根目录内的插件目录。

## 市场文档

```json
{
  "name": "example-marketplace",
  "interface": {
    "displayName": "Example Plugins"
  },
  "plugins": [
    {
      "name": "example-plugin",
      "source": {
        "source": "local",
        "path": "./plugins/example-plugin"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      }
    }
  ]
}
```

| 字段 | 类型 | 必填 | 默认值 |
|---|---|---|---|
| `name` | string | 是 | — |
| `interface` | object | 否 | `{}` |
| `interface.displayName` | string | 否 | Marketplace `name` |
| `plugins` | array | 是 | — |
| `plugins[].name` | string | 是 | — |
| `plugins[].source` | object | 是 | — |
| `plugins[].source.source` | `"local"` | 是 | — |
| `plugins[].source.path` | string | 是 | — |
| `plugins[].policy` | object | 是 | — |
| `plugins[].policy.installation` | `"AVAILABLE"` | 是 | — |
| `plugins[].policy.authentication` | `"ON_INSTALL"` | 是 | — |

### 文档规则

- `name` 是市场标识，且必须能安全用作目录名。
- `interface.displayName` 是 Desktop 中显示的市场名称。
- 每个插件的 `name` 必须与其 `.craft-plugin/plugin.json` 中的 `id` 一致。
- `source.source` 必须为 `local`。
- `source.path` 必须以 `./` 开头、保持在市场根目录内，并指向有效插件目录。
- 只有 `policy.installation` 为 `AVAILABLE` 的插件才会显示为可安装。
- 当前发布格式中的 `policy.authentication` 为 `ON_INSTALL`。App 和账号配置行为由插件的 App descriptors 定义。
- 插件能力和安装页元数据属于 `.craft-plugin/plugin.json`，不应写入市场文档。

## 市场来源

| 来源类型 | 值 | 行为 |
|---|---|---|
| **`git`** | 仓库简写或 Git URL | DotCraft 检出并验证仓库 |
| **`local`** | 已存在的目录 | DotCraft 直接验证并读取该目录 |
| **`archive`** | HTTPS 归档 URL 或本地归档文件 | DotCraft 使用解压后的缓存快照 |

Desktop 的添加对话框支持 Git 来源。只有本地工作区可以选择本地来源。宿主提供或手动配置的 registry 还可以使用归档来源。

### Git 来源格式

| 输入 | 结果 |
|---|---|
| `owner/repo` | GitHub 仓库的默认分支 |
| `owner/repo@release` | GitHub 仓库的 `release` 引用 |
| `https://host/team/repo.git` | HTTPS Git 仓库 |
| `https://host/team/repo.git#release` | HTTPS Git 仓库的 `release` 引用 |
| `ssh://git@host/team/repo.git` | SSH Git 仓库 |
| `git@host:team/repo.git` | SCP 格式的 SSH 仓库 |

显式设置的 `Ref` 会覆盖来源中附带的引用。`SparsePaths` 只适用于 Git 来源，且只能包含仓库内相对路径。绝对路径和 `..` 会被拒绝。

DotCraft 不接受 `file://` 来源，也不接受带内嵌凭据的 Web URL。Git 操作以非交互方式运行，并使用宿主机已经配置好的凭据。

## 配置

市场来源默认保存在全局配置中，插件仍按工作区分别安装。

```json
{
  "Plugins": {
    "PluginRegistries": [
      {
        "Name": "example-marketplace",
        "SourceType": "git",
        "Url": "https://github.com/example/dotcraft-plugins.git",
        "Ref": "main",
        "SparsePaths": [
          ".craft/plugins",
          "plugins"
        ],
        "MarketplacePath": ".craft/plugins/marketplace.json"
      }
    ]
  }
}
```

通过 Desktop 或 AppServer 添加来源时，`Name`、`LastUpdated` 和 `LastRevision` 会根据验证后的来源维护。手动配置时，`Name` 必须与市场文档一致。完整字段和配置优先级见[配置](../configuration#plugins-mcp-与-lsp)。

## 生命周期

### 添加

DotCraft 获取或打开来源，验证市场文档，记录该来源，然后刷新插件目录。添加市场不会安装任何插件。

### 刷新

刷新会重新获取 Git 来源、重新验证本地目录，或下载新的归档快照。一个市场刷新失败不会阻止其他市场继续刷新。

插件发现只读取已经存在于磁盘上的来源。列出插件不会触发 Git 获取。

### 安装

安装会把选中的插件复制到当前工作区并验证 manifest。DotCraft 通过普通插件生命周期加载已经启用的贡献。尚未安装的目录项，以及已经安装但未启用的插件，都不会提供 skills、tools、apps、servers、hooks 或 Desktop Plugin UI。

### 移除

移除市场会删除其配置来源。已经安装到各工作区的插件会继续保留。

## AppServer 操作

客户端调用市场方法前，必须先协商 `capabilities.pluginMarketplaces`。

| 方法 | 用途 |
|---|---|
| `marketplace/add` | 添加并验证 Git 或本地市场 |
| `marketplace/refresh` | 刷新一个市场或全部已配置市场 |
| `marketplace/remove` | 移除已配置的市场来源 |
| `plugin/list` | 返回插件以及市场分组元数据 |

请求和响应载荷见 [AppServer 协议](../protocols/appserver-protocol#插件市场)。

## 安全边界

- 添加市场表示信任其目录来源，但不会执行插件代码。
- 市场中的插件路径不能越出市场根目录。
- DotCraft 不保存市场来源凭据。
- 只有安装到工作区并启用后，插件才会贡献运行时能力。
- Tools、apps 与 hooks 继续使用各自的运行时检查。启用 Desktop Plugin 会执行其可信 renderer 代码，这也是对该模块的信任决定。

## 相关文档

- [DotCraft 官方插件市场](https://github.com/DotHarness/dotcraft-plugins)
- [插件市场](../../features/agent-system/plugin-marketplaces)
- [配置](../configuration#plugins-mcp-与-lsp)
- [AppServer 协议](../protocols/appserver-protocol#插件市场)
- [DotCraft App](./app-binding)
- [Desktop Plugins](./desktop-plugins)
