# IDE / 编辑器（ACP）

[Agent Client Protocol（ACP）](https://agentclientprotocol.com/) 是一个开放协议，专门用于标准化编码代理与编辑器/IDE 之间的通信方式——与 LSP 解决编辑器与语言服务器耦合问题的思路如出一辙，但作用对象是 AI 代理。任何实现了 ACP 的编辑器都可以接入任意 ACP 兼容代理。DotCraft 原生支持 ACP，因此可以作为编辑器内的一等公民编码助手运行，无需云订阅、无需专有插件、无需任何厂商特定的配置。

从编辑器角度来看，通信方式仍基于 **stdio（标准输入/输出）与 JSON-RPC 2.0**：编辑器将 DotCraft 作为子进程启动，通过标准流双向交换消息。在内部，DotCraft ACP 进程充当**协议桥接层**，将编辑器（ACP 协议）与 AppServer 实例（Wire Protocol）连接起来。所有会话状态、Agent 执行和工具调用均由 AppServer 处理，并与 TUI、Desktop、外部渠道等客户端共用同一套后端。桥接层会自动启动本地 AppServer 子进程，也可连接到你指定的远程 AppServer。

## 支持的编辑器

ACP 是开放标准，生态持续扩展。DotCraft 可运行于以下环境：

| 编辑器 | 插件 / 集成方式 |
|---|---|
| **JetBrains Rider**（及其他 JetBrains IDE） | AI Assistant 内置 Agent 支持 |
| **Obsidian** | [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) |
| **Unity Editor** | [dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) |

任何其他支持 ACP 的编辑器或工具，均可使用相同的配置模式接入 DotCraft。

## 快速开始

### 1. 初始化 DotCraft 工作区

在接入编辑器之前，先在终端进入项目目录完成一次非交互式初始化：

```bash
cd <你的项目目录>
dotcraft setup
```

按提示填写 provider / model / api-key 即可。完整字段可见 [配置完整参考](../../developing/configuration.md)；其余支持的 setup 参数可通过 `dotcraft setup --help` 查看。

DotCraft 初始化完成后，工作区即可供 ACP、TUI、Desktop 或自动化入口使用。

### 2. 在编辑器中配置 ACP

在编辑器的 Agent 配置中：

- **命令**：`dotcraft`
- **参数**：`-acp`
- **工作目录**：第 1 步中完成初始化的项目根目录

DotCraft 以 `-acp` 标志启动时会自动激活 ACP 模式，无需修改任何配置文件。

### 3. 远程工作区（可选）

如果你已有正在运行的 DotCraft AppServer（例如通过 `dotcraft app-server` 或桌面应用启动），可以让 ACP 桥接层连接到该实例，而不是重新启动子进程：

```text
dotcraft -acp --remote ws://<host>:<port>/ws
```

如果 AppServer 需要认证，可附加 `--token <token>`。连接远程 AppServer 后，通过编辑器创建的会话对所有已连接的客户端实时可见。

---

## JetBrains Rider（及其他 JetBrains IDE）

安装了 AI Assistant 插件的 JetBrains IDE 可以直接添加 ACP 代理。打开 **AI Chat - Add Custom Agents**，创建一条代理配置：

```json
{
    "agent_servers": {
        "DotCraft": {
            "command": "dotcraft",
            "args": ["-acp"]
        }
    }
}
```

保存后，在 AI 聊天面板的代理选择器中选中 DotCraft 即可。IDE 负责进程的生命周期管理——打开会话时 DotCraft 自动启动，关闭时自动退出。

## Obsidian

安装 [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) 插件（通过 BRAT 或手动安装），然后在插件设置中添加 Custom agents：

| 字段 | 值 |
|------|----|
| **AgentID** | DotCraft |
| **Display name** | DotCraft |
| **Path** | `dotcraft.exe` |
| **Arguments** | `-acp` |

配置完成后，DotCraft 会显示在插件的聊天界面中。它既能回答问题，也能直接读写笔记——同一个代理，既是编码助手，也是知识库助理。

## Unity 编辑器

Unity 编辑器客户端独立维护在另一个仓库：[DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity)。

DotCraft 本体仍然是 Unity 客户端通过 ACP 启动的 agent harness。请先从本仓库安装并配置 DotCraft，然后在 Unity 项目中添加 `dotcraft-unity`：

```text
https://github.com/DotHarness/dotcraft-unity.git
```

Unity 场景、选中对象、控制台和项目信息工具由 `dotcraft-unity` 在 ACP 初始化时声明为运行时 dynamic tools。`_unity/*` 是 Unity 客户端内部的 ACP 扩展方法，由客户端插件负责实现和维护。

## 工作原理

编辑器以 ACP 模式启动 DotCraft 后，交互流程如下：

1. **初始化** — 编辑器与 ACP 桥接层交换协议版本和能力声明（`initialize`）；桥接层随后连接 AppServer（若未指定 `--remote` 则自动启动本地子进程），并将握手信息通过 Wire Protocol 转发给 AppServer。支持 DotCraft 扩展的客户端还可以在 `_meta.dotcraft.runtimeTools` 中声明由客户端实现的运行时工具。
2. **创建会话** — 编辑器发送创建会话请求（`session/new`）；桥接层将请求转发给 AppServer，由 AppServer 创建会话，再将响应（可用斜杠命令、配置选项等）中继回编辑器 UI。客户端声明的运行时工具会作为 AppServer `dynamicTools` 绑定到当前会话连接。
3. **提示交互** — 编辑器发送用户消息（`session/prompt`）；AppServer 运行 Agent 并流式返回可见回复、思考内容、工具调用状态和执行结果，桥接层将可见回复作为 `agent_message_chunk`、思考内容作为 `agent_thought_chunk`，并通过 `session/update` 通知转发给编辑器。
4. **配置切换** — DotCraft 通过 ACP `configOptions` 暴露模式和模型选择器。支持该能力的客户端调用 `session/set_config_option` 切换模型，桥接层会同步更新当前线程和工作区默认模型。
5. **权限请求** — 执行文件写入或 Shell 命令前，AppServer 通过 Wire Protocol 下发审批请求；桥接层将其转换为 `requestPermission` ACP 消息，由编辑器向用户展示审批/拒绝提示。
6. **文件与终端访问** — 当 AppServer 需要编辑器原生的文件或终端访问能力时，请求通过桥接层转发回编辑器（`fs/readTextFile`、`fs/writeTextFile`、`terminal/*`），所有操作均通过编辑器自身的 API 路由。
7. **客户端运行时工具** — Unity 等客户端可以声明场景查询、选中对象读取、控制台日志读取等客户端拥有的工具。DotCraft 只负责校验和桥接工具描述符，具体实现仍留在客户端插件内。

这意味着 DotCraft 能够读取编辑器缓冲区中尚未保存的内容、在应用变更前展示内联 diff、并在编辑器管理的终端中执行命令——这些能力超出了普通 CLI 代理所能提供的范畴。与此同时，所有 Agent 状态均由 AppServer 统一管理，会话持久存储并可在其他客户端中访问，即使编辑器关闭后依然保留。

## 支持的协议功能

| 功能 | 说明 |
|---|---|
| `initialize` | 协议版本协商和能力交换 |
| `session/new` | 创建新会话 |
| `session/load` | 加载已有会话并回放历史 |
| `session/list` | 列出所有 ACP 会话 |
| `session/prompt` | 发送提示并流式接收回复 |
| `session/update` | DotCraft 向编辑器推送可见消息块、思考消息块和工具调用状态 |
| `session/set_config_option` | 切换会话配置选项，例如模式和模型 |
| `session/cancel` | 取消正在进行的操作 |
| `requestPermission` | DotCraft 就敏感操作向编辑器请求执行权限 |
| `fs/readTextFile` | 通过编辑器读取文件（含未保存内容） |
| `fs/writeTextFile` | 通过编辑器写入文件（可预览 diff） |
| `terminal/*` | 通过编辑器创建和管理终端 |
| Runtime Dynamic Tools | 客户端通过 `_meta.dotcraft.runtimeTools` 声明线程级工具，桥接层通过 AppServer `item/tool/call` 调回客户端 |
| Slash Commands | `.craft/commands/` 中的自定义命令自动广播到编辑器命令选择器 |
| Config Options | 将可选配置（模式、模型等）暴露到编辑器 UI，模型选择器使用 ACP `category: "model"` |

## 会话与工作区行为

ACP 作为完整的 AppServer 客户端工作。通过编辑器创建的会话会写入同一套会话存储：

- **会话 ID 格式**：`acp_{sessionId}`（会话 ID 由编辑器分配后转发给 AppServer）
- **会话存储**：存储于 `<workspace>/.craft/sessions/`，与 TUI、Desktop、Bot 等渠道的会话并列存放
- **共享记忆**：`memory/MEMORY.md` 和 `memory/HISTORY.md` 在同一工作区的所有 Channel 之间共享——在 ACP 会话中获取的知识，在 TUI、Desktop 或 QQ 机器人会话中同样可以访问，反之亦然
- **多客户端并发**：使用 `--remote` 时，多个客户端可同时连接同一个 AppServer。在 Obsidian 中开启的 ACP 会话，可以在桌面应用中实时查看或继续

## 使用示例

| 场景 | 推荐方式 |
|---|---|
| 本地 IDE 直接使用 | 配置编辑器启动 `dotcraft -acp` |
| 远程工作区 | 先启动 AppServer WebSocket，再在 ACP 参数中使用 `--remote` |
| 与 Desktop 共享会话 | 连接同一个 workspace / AppServer |
| 让编辑器负责文件和终端能力 | 使用支持 `fs/*` 和 `terminal/*` 的 ACP 客户端 |

## 故障排查

### 编辑器里没有出现 DotCraft

确认命令路径指向 `dotcraft`，参数为 `-acp`，并且编辑器插件支持 Agent Client Protocol。

### 无法读取未保存文件

只有通过编辑器 ACP 客户端路由的文件访问才能看到未保存缓冲区。其他入口通常读取磁盘文件。

### 远程模式连接失败

确认 AppServer 使用 WebSocket 启动，URL 包含 `/ws`，并且 token 与服务端一致。

## 相关入口

- [Desktop](./desktop.md) — 图形界面 + ACP 同后台
- [AppServer 模式](../../developing/appserver.md) — 远程或多客户端
- [统一会话核心](../session-core.md) — Thread / Turn / Item 模型与 ACP 桥接关系
