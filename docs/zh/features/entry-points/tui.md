# TUI

TUI 是 DotCraft 的 Rust 原生终端界面，适合希望在终端中获得完整交互体验的用户。它通过 AppServer Wire Protocol 连接 DotCraft，并复用同一套工作区、会话和审批能力。

## 构建

```bash
cd tui
cargo build --release
```

构建产物位于 `target/release/`，Windows 下文件名为 `dotcraft-tui.exe`。

## 启动方式

### Hub 托管本地模式（默认）

在项目目录中：

```bash
dotcraft-tui
```

TUI 会启动或发现 DotCraft Hub，让 Hub 为当前工作区确保 AppServer 已运行，然后连接 Hub 返回的 AppServer WebSocket 端点。

### 指定工作区或二进制

```bash
dotcraft-tui --workspace /path/to/project
dotcraft-tui --server-bin /usr/local/bin/dotcraft
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

### 远程模式

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
dotcraft-tui --remote ws://127.0.0.1:9100/ws
```

带认证的服务需要同时传入 token：

```bash
dotcraft-tui --remote ws://server:9100/ws --token my-secret
```

## 命令行参数

| 参数 | 说明 |
|---|---|
| `--workspace` | 指定工作区目录 |
| `--server-bin` | 指定用于启动 Hub 的 `dotcraft` 二进制 |
| `--remote` | 连接已有 WebSocket AppServer |
| `--token` | 远程 AppServer 认证 token |
| `--lang zh|en` | 指定界面语言 |
| `--theme` | 加载自定义主题 TOML |

## 常用斜杠命令

`/new`、`/model`、`/provider`、`/clear`、`/quit`。

- `/provider` 列出已配置的个人 provider
- `/provider <id>` 选择工作区 provider
- Provider 的创建、编辑、测试和删除由 Desktop 或其他支持 AppServer provider 管理的客户端完成

完整快捷键和主题说明见仓库中的 [`tui/README.md`](https://github.com/DotHarness/dotcraft/blob/master/tui/README.md)。

## 进阶

- 默认模式是 Hub 托管本地模式；远程模式适合显式托管的 AppServer。
- 打开调试日志：`DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log`
- 启用系统剪贴板：`cargo build --release --features clipboard`

## 故障排查

### TUI 找不到 `dotcraft`

把 `dotcraft` 放在 `dotcraft-tui` 同目录或加入 `PATH`，也可以使用 `--server-bin` / `DOTCRAFT_BIN` 指定二进制路径。

### 远程连接失败

确认 AppServer 使用 WebSocket 模式启动，并且客户端 URL 包含 `/ws` 路径。带认证服务需要同时传入 token。

### 终端显示异常

使用支持 Unicode 和颜色的现代终端，并确认终端尺寸足够。必要时先使用默认主题排查。

## 相关入口

- [Desktop](./desktop.md) — 图形界面入口
- [AppServer 模式](../../developing/appserver.md) — 远程 / 多客户端
- [设置生效层级](../../developing/settings-lifecycle.md)
