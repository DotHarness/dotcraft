# TUI

TUI 是 DotCraft 用 Rust 写的原生终端界面。如果你常待在终端里——或者要走 SSH 远程——它能让你在那儿获得完整的 DotCraft 体验：和 Desktop 一样的工作区、会话和审批，全程不用离开命令行。

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

## 相关文档

- [Desktop](./desktop) — 图形界面入口
- [AppServer 模式](../../developing/lifecycle/appserver) — 远程 / 多客户端
- [设置生效层级](../../developing/lifecycle/settings-lifecycle)
