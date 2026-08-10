# DotCraft Telegram 适配器

DotCraft 外部渠道适配器的 Telegram 参考实现。

该适配器将每个 Telegram 会话映射为一个 DotCraft 线程。用户通过发送消息与 Agent 交互；Agent 的回复作为 Telegram 消息发回。当 Agent 需要用户审批时（例如执行 Shell 命令前），适配器会展示原生的 Telegram 内联键盘。

## 功能特性

- **长轮询** — 不需要公网 IP 或 Webhook。
- **会话-线程映射** — 每个 Telegram 会话（私聊或群聊）映射为一个 DotCraft 线程。
- **流式回复** — 累积 Agent 的文本增量，作为一条完整消息发送。
- **内联键盘审批** — `item/approval/request` 以 Telegram 按钮形式呈现。
- **`/new` 命令** — 归档当前线程，开启新对话。
- **`/help` 命令** — 显示可用命令。
- **消息投递支持** — `ext/channel/send` 映射到 `bot.send_message()`。
- **输入状态指示** — 在 Agent 处理期间显示"正在输入…"。
- **Markdown → HTML 转换** — 将 Agent 的 Markdown 输出转换为 Telegram 兼容的 HTML。

## 前置条件

- Python 3.11+
- 来自 [@BotFather](https://t.me/BotFather) 的 Telegram Bot Token
- 正在运行且已配置 `ExternalChannels` 的 AppServer

## 安装

```bash
# 在 DotCraft 仓库根目录执行：
pip install -r sdk/python/examples/telegram/requirements.txt
```

这会安装 `dotcraft`（SDK）和 `python-telegram-bot`。

## 配置

### 1. 创建 Telegram Bot

在 Telegram 上找到 [@BotFather](https://t.me/BotFather)：

```
/newbot
```

复制它给你的 Bot Token（格式类似 `123456789:AABBccdd...`）。

### 2. 配置 DotCraft

在你的 DotCraft `config.json` 中添加以下内容，将 `your-bot-token-here` 替换为实际 Token：

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "subprocess",
      "command": "python",
      "args": ["-m", "dotcraft_telegram"],
      "workingDirectory": "sdk/python/examples/telegram",
      "env": {
        "TELEGRAM_BOT_TOKEN": "your-bot-token-here"
      }
    }
  }
}
```

完整模板见 [config.example.json](https://github.com/DotHarness/dotcraft/blob/master/sdk/python/examples/telegram/config.example.json)。

### 3. 启动 DotCraft

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
```

DotCraft 会自动以子进程方式启动 Telegram 适配器。你可以在 stderr 日志中看到 Bot 成功连接的信息。

## 手动运行（开发调试）

你也可以在 DotCraft 之外单独运行适配器（单独运行时需要使用 WebSocket 模式）：

```bash
export TELEGRAM_BOT_TOKEN="your-token-here"
cd sdk/python/examples/telegram
python -m dotcraft_telegram
```

> [!NOTE]
> 在子进程模式下，适配器通过 stdin/stdout 与 DotCraft 通信。在终端中直接运行不会生效，因为 stdin 是终端而非 DotCraft 进程。开发调试时请使用 WebSocket 模式。

## 环境变量

| 变量 | 必填 | 说明 |
|------|------|------|
| `TELEGRAM_BOT_TOKEN` | 是 | 来自 @BotFather 的 Bot Token。 |
| `HTTPS_PROXY` | 否 | Telegram API 的 HTTPS 代理 URL（在网络受限环境中使用）。 |

## 命令

| 命令 | 说明 |
|------|------|
| `/new` | 归档当前对话线程，开启新对话。 |
| `/help` | 显示可用命令。 |
| _（任意文本）_ | 向 Agent 发送消息。 |

## 审批流程

当 Agent 需要审批时（例如执行 Shell 命令或写入文件），适配器会发送带有内联键盘按钮的 Telegram 消息：

```
⚠️ Agent 需要审批
类型: shell
操作: npm test
原因: Agent 想要执行一个 Shell 命令

[✅ 批准]  [✅ 批准（本次会话）]
[❌ 拒绝]  [🛑 取消本次对话]
```

点击按钮发送你的决定。Agent 会根据你的选择继续（或停止）执行。

## 架构

```
Telegram 用户
     │
     │ 消息 / /new / /help
     ▼
TelegramAdapter (python-telegram-bot, 长轮询)
     │
     │ handle_message()      → ChannelAdapter
     │ on_approval_request() → 内联键盘 → 用户 → 决策
     │ on_deliver()          → bot.send_message()
     │
     ▼
dotcraft.ChannelAdapter
     │
     │ thread/start, turn/start, 流式事件
     │ item/approval/request ↔ 响应
     │ ext/channel/send
     │
     ▼
DotCraft AppServer (stdio JSON-RPC)
     │
     ▼
Agent 执行（SessionService、工具、记忆）
```

## 文件结构

```
examples/telegram/
  dotcraft_telegram/
    __init__.py         包入口
    __main__.py         启动入口 (python -m dotcraft_telegram)
    bot.py              TelegramAdapter 实现
    formatting.py       Markdown → Telegram HTML 转换、消息分割
  requirements.txt      Python 依赖
  config.example.json   DotCraft config.json 配置示例
  README.md             英文说明
  README_ZH.md          本文件
```

## 相关文档

- [Python SDK](../sdks/python)——`dotcraft` SDK 概览与 API 参考。
- [Channels & Bots](../../features/entry-points/channels)——所有聊天渠道的总览。
- [Telegram 渠道适配器](./telegram)——同一平台的 TypeScript 适配器。

