# IDE / 编辑器（ACP）

DotCraft 可以直接在编辑器里当编码助手用——JetBrains、Obsidian、Unity 都可以，不需要云订阅，也不需要专有插件。它讲的是 [Agent Client Protocol（ACP）](https://agentclientprotocol.com/)，一个把 coding agent 接进编辑器的开放标准，作用类似 LSP 之于语言服务。任何支持 ACP 的编辑器都能接上 DotCraft。

编辑器负责启动 DotCraft，DotCraft 再把这段对话桥接给自己的 [AppServer](../../developing/lifecycle/appserver)，由它运行 Agent。所以编辑器里的会话和 Desktop、渠道用的是同一个工作区、同一份会话记录和同一份记忆。编辑器只是面向同一个 Agent 的另一扇窗口。

![DotCraft 在编辑器窗口里运行，JetBrains、Obsidian、Unity 通过 ACP 连到同一个 AppServer，Desktop 与聊天渠道也接在这个 AppServer 上，共用同一套会话与记忆](/editor-acp-overview.svg)

## 支持的编辑器

| 编辑器 | 插件 / 集成方式 |
|---|---|
| **JetBrains IDEs** | AI Assistant 内置的 Agent 支持 |
| **Obsidian** | [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) |
| **Unity Editor** | [dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) |

ACP 是开放标准，生态还在扩展。其他支持 ACP 的编辑器用同样的配置方式接入。

## 接入编辑器

### 1. 初始化 DotCraft 工作区

先在项目目录里完成一次初始化：

```bash
cd <你的项目目录>
dotcraft setup
```

按提示填写 provider、model 和 api-key。可用参数见 `dotcraft setup --help`，完整字段见[配置完整参考](../../developing/configuration)。初始化完成后，这个工作区就能供 ACP、Desktop 和自动化入口共同使用。

### 2. 在编辑器中配置 ACP

在编辑器的 Agent 配置里填三项：

- **命令**：`dotcraft`
- **参数**：`acp`
- **工作目录**：第 1 步初始化的项目根目录

带 `acp` 启动时 DotCraft 会自动进入 ACP 模式，不用改任何配置文件。

### 3. 连接远程 AppServer（可选）

已经有一个 AppServer 在跑（用 `dotcraft app-server` 或桌面应用启动的），可以让编辑器连过去，而不是再起一个：

```text
dotcraft acp --remote ws://<host>:<port>/ws
```

AppServer 监听的是裸地址 `ws://host:port`，客户端连接时一律在末尾加上 `/ws`。AppServer 开了认证就再补一个 `--token <token>`。连上之后，你在编辑器里创建的会话，其他已连接的客户端实时可见。

## JetBrains IDEs

装了 AI Assistant 插件的 JetBrains IDE 可以直接注册 ACP Agent。打开 **AI Chat → Add Custom Agents**，填入：

```json
{
    "agent_servers": {
        "DotCraft": {
            "command": "dotcraft",
            "args": ["acp"]
        }
    }
}
```

保存后，在 AI 聊天面板的 Agent 选择器里选中 DotCraft。进程由 IDE 管理：打开会话时 DotCraft 启动，关闭会话时退出。

## Obsidian

先装 [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client)（用 BRAT 或手动安装都行），再到插件设置里添加一个 Custom agent：

| 字段 | 值 |
|---|---|
| **AgentID** | DotCraft |
| **Display name** | DotCraft |
| **Path** | `dotcraft.exe` |
| **Arguments** | `acp` |

配置完成后，DotCraft 会出现在插件的聊天界面里。它既能回答问题，也能直接读写笔记——同一个 Agent，既是编码助手，也是知识库助理。

## Unity Editor

Unity 客户端维护在单独的仓库：[DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity)。Unity 通过 ACP 启动的仍然是 DotCraft 本体，所以先按上面的步骤装好并初始化 DotCraft，再把 `dotcraft-unity` 加进 Unity 项目：

```text
https://github.com/DotHarness/dotcraft-unity.git
```

连上之后，Agent 可以查询场景、当前选中的对象、Console 和项目信息。这些工具由 `dotcraft-unity` 插件提供并维护。

## 在编辑器里多出来的能力

- **读到未保存的内容** — Agent 看的是你正在编辑的缓冲区，不只是磁盘上的版本。
- **应用前先看 diff** — 在编辑器自己的 diff 视图里逐项审阅、逐项批准。
- **编辑器托管的终端** — 命令跑在编辑器的终端里，沿用它的工作目录和环境。
- **原生审批** — 写文件或执行 Shell 命令前，编辑器弹出批准或拒绝。
- **斜杠命令与模型切换** — `.craft/commands/` 出现在编辑器的命令选择器里，模型也能就地切换。

Agent 跑在 AppServer 里，所以工作不会随编辑器关闭而消失。DotCraft 实现的完整 ACP 方法清单，以及桥接层如何映射到 AppServer，见 [AppServer 协议](../../developing/protocols/appserver-protocol)。

## 会话在多个客户端间共享

ACP 会话就是一个完整的工作区会话，和你的 Desktop、Bot 会话存在同一套存储里，共享同一份长期记忆。在编辑器里聊出来的结论，同一工作区的 Desktop 会话和 QQ 机器人会话都能用上，反过来也一样。

用 `--remote` 连同一个 AppServer 时，多个客户端可以同时在线。你在 Obsidian 里开的会话，能在桌面应用里实时查看并接着聊。背后的模型见[统一会话核心](../../developing/architecture/session-core)。

## 相关文档

- [Desktop](./desktop) — 同一个 Agent 的图形界面入口，适合看 diff、处理审批和查历史
- [社交渠道](../channels/) — 把同一个工作区接到 QQ、飞书这类聊天工具里
