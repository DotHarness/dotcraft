# DotCraft Telegram Adapter

Reference implementation of a DotCraft external channel adapter for Telegram.

This adapter maps each Telegram chat to a DotCraft thread. Users interact with the agent by sending messages; the agent's replies are sent back as Telegram messages. When the agent requires user approval (e.g. before running a shell command), the adapter presents a native Telegram inline keyboard.

## Features

- **Long polling** — no public IP or webhook required.
- **Thread-per-chat** — each Telegram chat (private or group) maps to one DotCraft thread.
- **Streaming replies** — agent text deltas are accumulated and sent as a single composed message.
- **Inline keyboard approvals** — `item/approval/request` is presented as Telegram buttons.
- **`/new` command** — archives the current thread and starts fresh.
- **`/help` command** — shows available commands.
- **Delivery support** — `ext/channel/deliver` is mapped to `bot.send_message()`.
- **Typing indicator** — shows "typing…" while the agent is processing.
- **Markdown → HTML** — converts agent Markdown output to Telegram-compatible HTML.

## Prerequisites

- Python 3.11+
- A Telegram bot token from [@BotFather](https://t.me/BotFather)
- A running DotCraft instance (GatewayHost mode with ExternalChannels configured)

## Installation

```bash
# From the DotCraft repo root:
pip install -r sdk/python/examples/telegram/requirements.txt
```

This installs `dotcraft-wire` (the SDK) and `python-telegram-bot`.

## Configuration

### 1. Create a Telegram bot

Talk to [@BotFather](https://t.me/BotFather) on Telegram:

```
/newbot
```

Copy the bot token it gives you (looks like `123456789:AABBccdd...`).

### 2. Configure DotCraft

Add the following to your DotCraft `config.json`. Replace `your-bot-token-here` with your actual token:

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

See [config.example.json](https://github.com/DotHarness/dotcraft/blob/master/sdk/python/examples/telegram/config.example.json) for a complete template.

### 3. Start DotCraft

```bash
dotcraft gateway
```

DotCraft will automatically spawn the Telegram adapter as a subprocess. You should see log output from the adapter on stderr indicating the bot connected.

## Running Manually (for development)

You can also run the adapter outside DotCraft to test it in isolation (WebSocket mode required when running standalone):

```bash
export TELEGRAM_BOT_TOKEN="your-token-here"
cd sdk/python/examples/telegram
python -m dotcraft_telegram
```

> [!NOTE]
> In subprocess mode, the adapter communicates with DotCraft over stdin/stdout. Running it manually in a terminal won't work because stdin will be the terminal, not the DotCraft process. Use WebSocket mode for standalone development.

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `TELEGRAM_BOT_TOKEN` | Yes | Bot token from @BotFather. |
| `HTTPS_PROXY` | No | HTTPS proxy URL for the Telegram API (useful in restricted networks). |

## Commands

| Command | Description |
|---------|-------------|
| `/new` | Archive the current conversation thread and start a fresh one. |
| `/help` | Show available commands. |
| _(any text)_ | Send a message to the agent. |

## Approval Flow

When the agent requires approval (e.g. to run a shell command or write a file), the adapter sends a Telegram message with inline keyboard buttons:

```
⚠️ Agent approval required
Type: shell
Operation: npm test
Reason: Agent wants to execute a shell command

[✅ Approve]  [✅ Approve (this session)]
[❌ Decline]  [🛑 Cancel turn]
```

Tap a button to send your decision. The agent continues (or stops) based on your choice.

## Architecture

```
Telegram User
     │
     │ message / /new / /help
     ▼
TelegramAdapter (python-telegram-bot, long polling)
     │
     │ handle_message()      → ChannelAdapter
     │ on_approval_request() → inline keyboard → user → decision
     │ on_deliver()          → bot.send_message()
     │
     ▼
dotcraft_wire.ChannelAdapter
     │
     │ thread/start, turn/start, stream events
     │ item/approval/request ↔ response
     │ ext/channel/deliver
     │
     ▼
DotCraft AppServer (stdio JSON-RPC)
     │
     ▼
Agent Execution (SessionService, Tools, Memory)
```

## File Structure

```
examples/telegram/
  dotcraft_telegram/
    __init__.py         Package root
    __main__.py         Entry point (python -m dotcraft_telegram)
    bot.py              TelegramAdapter implementation
    formatting.py       Markdown → Telegram HTML conversion, message splitting
  requirements.txt      Python dependencies
  config.example.json   Sample DotCraft config.json snippet
  README.md             This file
  README_ZH.md          Chinese version
```

## Further Reading

- [Python SDK](../sdk-python.md) — `dotcraft_wire` SDK overview and API reference.
- [SDK Architecture](https://github.com/DotHarness/dotcraft/blob/master/sdk/python/ARCHITECTURE.md) — How the SDK works internally.
