# DotCraft Hooks Samples

**中文 | [English](./README.md)**

DotCraft Hooks 示例，演示如何在工作区中配置生命周期钩子，用于拦截危险操作、记录工具执行结果等。

## 示例内容

本示例提供两套可直接参考的目录：

- [windows](./windows): Windows / PowerShell 示例
- [linux](./linux): Linux / macOS Bash 示例

每套示例都包含：

- `hooks.json`
- `hooks/guard-exec.*`：在 `PreToolUse` 阶段拦截明显危险的 `Exec` 命令
- `hooks/log-post-tool.*`：在 `PostToolUse` 阶段记录 `WriteFile` / `EditFile` 操作

## 使用方式

将对应平台目录下的内容复制到你的真实工作区中：

```text
<workspace>/.craft/hooks.json
<workspace>/.craft/hooks/...
```

Linux / macOS 下如果使用脚本文件，记得为脚本增加可执行权限：

```bash
chmod +x hooks/*.sh
```

## create-hooks Skill

DotCraft 内置了 `create-hooks` skill，适合在和 AI 对话时让它帮你生成或调整 `hooks.json` 和 `hooks` 脚本。

如果你不想手写配置，可以直接描述你的需求，例如：

- 拦截某些 Shell 命令
- 在写文件后自动记录日志
- 在停止回答后发送通知

## 重要说明

创建或修改 Hooks 后，需要用户手动重启 DotCraft，变更才会生效。

## 当前示例演示的事件

| 事件 | 作用 |
|------|------|
| `PreToolUse` | 在工具执行前检查 `Exec` 命令，并阻止明显危险的命令 |
| `PostToolUse` | 在文件写入或编辑成功后追加日志 |

## 预期效果

- 当 Agent 尝试执行明显危险的 Shell 命令时，Hooks 会返回阻止原因
- 当 Agent 成功执行 `WriteFile` 或 `EditFile` 时，会在 `hooks/hooks.log` 中追加一条日志
