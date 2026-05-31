# DotCraft TUI

**中文 | [English](./README.md)**

DotCraft 的 Rust 原生终端界面，基于 [Ratatui](https://ratatui.rs/) 构建，通过 Wire Protocol（JSON-RPC）连接 DotCraft AppServer，在终端中提供完整的 AI Agent 交互体验。

## 功能特性

| 功能 | 说明 |
|------|------|
| **流式输出** | Agent 消息逐字流式渲染，支持 Markdown（代码高亮、表格、标题等） |
| **工具调用展示** | `• Called ReadFile("src/main.rs") (0.3s)` 格式，含耗时和结果摘要 |
| **StatusIndicator** | 任务运行时显示 `⠋ Working (Ns · esc to interrupt)`，带文字闪烁动画 |
| **顶部状态卡** | 紧凑展示模型、工作区、线程和连接状态 |
| **内容流输入区** | 短对话时输入区跟随最近内容，长对话时自然靠近视口底部 |
| **FooterLine** | 按需显示的输入区提示行，用于斜杠导航、发送提示、连接错误和运行中 Token |
| **内联 SubAgent 进度** | SubAgent 运行状态内联展示，全部完成后折叠为摘要行 |
| **内联 Plan 视图** | Agent 任务计划（待办清单）内联展示 |
| **会话管理** | `/sessions` 打开会话选择器，支持恢复/归档/删除 |
| **审批流** | 工具调用需要审批时弹出 `ApprovalOverlay`，支持多种决策选项 |
| **主题定制** | 通过 TOML 文件自定义配色 |
| **剪贴板** | `y` 键复制最后一条 Agent 消息（需 `clipboard` feature） |
| **WebSocket 模式** | 可连接远程 AppServer（需 `websocket` feature） |

## 构建

**前提条件**：Rust 工具链（stable channel），推荐通过 [rustup](https://rustup.rs/) 安装。

```bash
# 进入 tui 目录
cd tui

# 标准构建（包含 WebSocket 支持）
cargo build --release

# 不含 WebSocket（不支持本地 Hub 或远程模式）
cargo build --release --no-default-features

# 包含系统剪贴板支持
cargo build --release --features clipboard
```

构建产物位于 `target/release/dotcraft-tui`（Windows 下为 `dotcraft-tui.exe`）。

## 启动方式

### 方式一：Hub 托管本地模式（默认）

TUI 会启动或发现 DotCraft Hub，让 Hub 为当前工作区确保 AppServer 已运行，然后连接 Hub 返回的 AppServer WebSocket 端点。`--server-bin` 指定用于启动 Hub 的 `dotcraft` 二进制；省略时会优先查找 `dotcraft-tui` 同目录下的 `dotcraft`，再回退到 PATH 中的 `dotcraft`。

终端 UI 会立即出现，并同时显示顶部状态卡和输入区。Hub/AppServer 仍在连接时也可以编辑草稿；连接完成前按 `Enter` 不会清空或提交草稿，只会在输入区附近显示连接状态。

```bash
# 在项目目录下直接启动
dotcraft-tui

# 指定工作区路径
dotcraft-tui --workspace /path/to/project

# 指定用于启动 Hub 的 dotcraft 二进制路径
dotcraft-tui --server-bin /usr/local/bin/dotcraft

# 通过环境变量指定二进制路径
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

### 方式二：远程 WebSocket 模式

连接到已在运行的 AppServer（需 `websocket` feature）。

```bash
# 连接本地 AppServer
dotcraft-tui --remote ws://localhost:3000/ws

# 连接带认证的远程 AppServer
dotcraft-tui --remote "ws://host:3000/ws?token=your-secret"

# 配合 --workspace 指定工作区
dotcraft-tui --remote ws://host:3000/ws --workspace /path/to/project
```

AppServer 启动方式参考：

```bash
# 启动 AppServer（WebSocket 模式）
dotcraft app-server --listen ws://0.0.0.0:3000
```

### 主题

```bash
# 使用自定义主题
dotcraft-tui --theme /path/to/theme.toml
```

### 命令行参数速查

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--remote <URL>` | 连接远程 AppServer（WebSocket URL） | — |
| `--server-bin <PATH>` | 用于启动 Hub 的 `dotcraft` 二进制 | 同目录 `dotcraft`，再 PATH |
| `--workspace <PATH>` | 工作区路径 | 当前目录 |
| `--theme <PATH>` | 自定义主题 TOML 路径 | 内置深色主题 |

## 快捷键

| 按键 | 作用 |
|------|------|
| `Enter` | 已连接时发送消息；连接中保留草稿 |
| `Shift+Enter` | 在输入框内插入换行 |
| `Tab` | 任务运行中：将消息加入队列；空闲时：斜杠命令或 `$技能` 补全 |
| `Ctrl+C` | 任务运行中：中断当前 Agent；空闲时：第一次标记退出意图，再次按下退出 |
| `Shift+Tab` | 切换 Agent / Plan 模式 |
| `↑` / `↓` | 输入框内容为空时：历史消息导航；弹窗打开时：切换候选项 |
| `PageUp` / `PageDown` | 对话区域翻页 |
| `Home` / `End` | 对话区域跳到顶部 / 底部 |
| `y` | 复制最后一条 Agent 消息到剪贴板（需 `clipboard` feature） |
| `s` | SubAgent 全部完成后：展开 / 折叠详情 |
| `Ctrl+L` | 强制刷新终端 |

## 斜杠命令

| 命令 | 说明 |
|------|------|
| `/sessions` | 打开会话管理器 |
| `/new` | 开启新会话 |
| `/clear` | 清空当前对话历史 |
| `/load <thread-id>` | 加载指定会话 |
| `/agent` | 切换到 Agent 模式 |
| `/plan` | 切换到 Plan 模式 |
| `/model [name\|default]` | 打开模型选择器或设置模型 |
| `/skills` | 启用、禁用或查看技能 |
| `/permissions` | 设置当前线程或下一条线程的权限预设 |
| `/cron` | 列出 Cron 任务 |
| `/quit` | 退出 TUI |

## 技能引用

在输入框中输入 `$` 会在输入框下方打开技能选择列表。`↑/↓` 切换候选项，`Tab` 或 `Enter` 插入选中的技能，`Esc` 关闭列表。提交时，已识别且启用的 `$技能` 会以原生 `skillRef` 输入片段发送给 AppServer，同时对话历史仍保留用户看到的 `$技能` 文本。

## 主题配置

在 `--theme` 指定的 TOML 文件中自定义颜色（颜色支持 Ratatui 颜色名或 `#RRGGBB`）：

```toml
[colors]
brand = "#7C3AED"           # 品牌色（Logo、模式指示器）
user_message = "white"
agent_message = "white"
reasoning = "cyan"
tool_active = "yellow"
tool_completed = "gray"
error = "red"
success = "green"
dim = "dark_gray"
mode_agent = "green"
mode_plan = "blue"
status_indicator = "yellow" # "Working" 状态文字颜色

[footer]
foreground = "dark_gray"    # 按需输入区提示文字
context_color = "dark_gray" # 运行中 Token 等上下文

[code]
syntect_theme = "base16-ocean.dark"  # 代码高亮主题
```

## 日志

设置 `DOTCRAFT_TUI_LOG` 环境变量启用日志输出（日志写入 stderr）：

```bash
DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log
```
