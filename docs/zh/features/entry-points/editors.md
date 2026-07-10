# IDE / 编辑器（ACP）

DotCraft 可以直接在你的编辑器里当编码助手用——JetBrains、Obsidian、Unity 等等——不用云订阅、不用专有插件、不绑定任何厂商。它靠的是 [Agent Client Protocol（ACP）](https://agentclientprotocol.com/)：一个把编码代理接进编辑器的开放标准（思路类似 LSP，只是对象换成了 AI 代理）。任何兼容 ACP 的编辑器都能接任意兼容 ACP 的代理，而 DotCraft 原生就支持 ACP。

编辑器启动 DotCraft 并与它对话，DotCraft 再把这段对话桥接到自己的 [AppServer](../../developing/lifecycle/appserver)，由 AppServer 运行 Agent。因此一个 ACP 会话与 Desktop、渠道共用同一个工作区、会话、记忆和工具——编辑器只是面向同一个 Agent 的另一扇窗口。默认情况下 DotCraft 会替你启动一个本地 AppServer；需要时再让它连接远程 AppServer。

## 支持的编辑器

ACP 是开放标准，生态持续扩展。DotCraft 可运行于以下环境：

| 编辑器 | 插件 / 集成方式 |
|---|---|
| **JetBrains IDEs** | AI Assistant 内置 Agent 支持 |
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

按提示填写 provider / model / api-key 即可。完整字段可见 [配置完整参考](../../developing/configuration)；其余支持的 setup 参数可通过 `dotcraft setup --help` 查看。

DotCraft 初始化完成后，工作区即可供 ACP、Desktop 或自动化入口使用。

### 2. 在编辑器中配置 ACP

在编辑器的 Agent 配置中：

- **命令**：`dotcraft`
- **参数**：`-acp`
- **工作目录**：第 1 步中完成初始化的项目根目录

DotCraft 以 `-acp` 标志启动时会自动激活 ACP 模式，无需修改任何配置文件。

### 3. 远程工作区（可选）

如果你已有正在运行的 DotCraft AppServer（例如通过 `dotcraft app-server` 或桌面应用启动），可以让 ACP 桥接层连接到该实例，而不是重新启动一个：

```text
dotcraft -acp --remote ws://<host>:<port>/ws
```

AppServer 监听的是裸地址 `ws://host:port`，客户端连接时一律在末尾追加 `/ws` 路径（如上所示）。AppServer 需要认证时再附加 `--token <token>`。连接远程 AppServer 后，你在编辑器中创建的会话对所有已连接的客户端实时可见。

---

## JetBrains IDEs

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

连接之后，Agent 可以查询 Unity 场景、选中对象、控制台和项目信息——这些工具由 `dotcraft-unity` 插件提供并维护。

## 在编辑器里你能得到什么

在编辑器里运行，让 DotCraft 拥有普通 CLI Agent 给不了的能力：

- **读取未保存的缓冲区** — Agent 看到的是你当前的编辑内容，而不只是磁盘上的版本。
- **应用前先看内联 diff** — 在编辑器自己的 diff 视图里逐项审阅并批准每处改动。
- **编辑器托管的终端** — 命令在编辑器拥有的终端里运行，沿用它的工作目录和环境。
- **原生审批** — 写文件或执行 Shell 命令前，编辑器会弹出批准/拒绝提示。
- **斜杠命令与模型切换** — 你的 `.craft/commands/` 会出现在编辑器的命令选择器里，也可以直接在编辑器里切换模型。

由于 Agent 在 AppServer 中运行，你的工作不会随编辑器关闭而消失：会话持久保存，关闭编辑器后另一个客户端仍能接管同一个线程。

DotCraft 实现的完整 ACP 方法清单，以及桥接层如何把它们映射到 AppServer，见 [AppServer 协议](../../developing/protocols/appserver-protocol)。

## 会话在多个客户端间共享

一个 ACP 会话就是一个完整的工作区会话——它和你的 Desktop、Bot 会话存在同一套存储里，并共享同一份长期记忆。在 ACP 会话中获取的知识，在同一工作区的 Desktop 或 QQ 机器人会话中同样可以访问，反之亦然。

使用 `--remote` 时，多个客户端可同时连接同一个 AppServer。你在 Obsidian 中开启的会话，可以在桌面应用中实时查看或继续。背后的模型见 [统一会话核心](../../developing/architecture/session-core)。

## 使用示例

| 场景 | 推荐方式 |
|---|---|
| 本地 IDE 直接使用 | 配置编辑器启动 `dotcraft -acp` |
| 远程工作区 | 先启动 AppServer WebSocket，再在 ACP 参数中使用 `--remote` |
| 与 Desktop 共享会话 | 连接同一个 workspace / AppServer |
| 让编辑器负责文件和终端能力 | 使用能原生处理文件与终端请求的 ACP 客户端 |

## 相关文档

- [Desktop](./desktop) — 图形界面 + ACP 同后台
- [AppServer 模式](../../developing/lifecycle/appserver) — 远程或多客户端
- [统一会话核心](../../developing/architecture/session-core) — Thread / Turn / Item 模型与 ACP 桥接关系
