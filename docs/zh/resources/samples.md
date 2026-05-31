# 示例与模板

仓库 [`samples/`](https://github.com/DotHarness/dotcraft/tree/master/samples) 提供可直接复制到工作区或参考的示例。每个示例聚焦一个能力，按顺序复制到自己的项目即可。

> [!TIP]
> 第一次验证建议先跑 [Workspace](#workspace)，然后按需要选 Automations / Hooks / Skills。

## 配置示例

| 名称 | 用途 | 仓库链接 |
|---|---|---|
| Automations | 本地任务模板和启用 Automations 的最小工作区配置 | [samples/automations](https://github.com/DotHarness/dotcraft/tree/master/samples/automations) |
| Hooks | Linux 和 Windows 生命周期 Hook 示例 | [samples/hooks](https://github.com/DotHarness/dotcraft/tree/master/samples/hooks) |
| Plugins | 用于开发和集成测试的参考插件包 | [samples/plugins](https://github.com/DotHarness/dotcraft/tree/master/samples/plugins) |

## 工作区行为示例

| 名称 | 用途 | 仓库链接 |
|---|---|---|
| Automations · example-local-task | 本地任务模板（`task.md` + `workflow.md`） | [samples/automations](https://github.com/DotHarness/dotcraft/tree/master/samples/automations) |
| Hooks · windows | PowerShell hooks（拦截危险 Exec、记录文件写入） | [samples/hooks/windows](https://github.com/DotHarness/dotcraft/tree/master/samples/hooks/windows) |
| Hooks · linux | Bash hooks（拦截危险 Exec、记录文件写入） | [samples/hooks/linux](https://github.com/DotHarness/dotcraft/tree/master/samples/hooks/linux) |

## Skills 模板

| 名称 | 用途 | 仓库链接 |
|---|---|---|
| `dev-guide` | 项目开发规范示例，包含模块开发参考文档 | [samples/skills/dev-guide](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/dev-guide) |
| `feature-workflow` | 大型功能开发工作流，用于拆解、实现、验证复杂需求 | [samples/skills/feature-workflow](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/feature-workflow) |
| `dotcraft-llm-error-diagnosis` | LLM / agent / 工具调用 / session 失败时的只读诊断流程 | [samples/skills/llm-error-diagnosis](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/llm-error-diagnosis) |

## Plugins 模板

仓库 [`samples/plugins/`](https://github.com/DotHarness/dotcraft/tree/master/samples/plugins) 收录可参考的 DotCraft 插件骨架。开发自定义插件时，建议先用内置 `$plugin-creator` 生成结构，再参考这些示例完善 manifest、tool 进程和说明。

## 常见使用流程

### Workspace

```bash
cd /path/to/your-project
dotcraft setup --provider-mode create \
  --provider-protocol anthropic --provider-id anthropic \
  --model claude-sonnet-4-5 --api-key <anthropic-api-key> \
  --profile developer
```

这是第一次使用 DotCraft 最稳的路径。完成后，`.craft/` 包含工作区配置和 bootstrap 文件。如果你想长期运行服务器上的社交渠道机器人，请使用 [服务器部署](../developing/server-deployment.md) 中的 Docker 方式：

```bash
cd deploy/docker
cp .env.example .env
docker compose up -d
```

模板里最常需要修改的字段：

| 字段 | 建议 |
|---|---|
| `DashBoard.Host` / `DashBoard.Port` | 按本机绑定地址或端口调整 |
| `Tools.Sandbox.Enabled` | 仅在实际运行 OpenSandbox 时开启 |
| `Tools.Sandbox.Domain` | 改成你自己的 OpenSandbox 地址，不要和 Dashboard 端口冲突 |
| `Tools.Sandbox.Image` | 替换成你希望 sandbox 使用的容器镜像 |

### Hooks

把 `samples/hooks/<platform>/` 内容复制到工作区：

```text
<workspace>/.craft/hooks.json
<workspace>/.craft/hooks/...
```

Linux / macOS 下记得给脚本加可执行权限：

```bash
chmod +x hooks/*.sh
```

> [!WARNING]
> 创建或修改 Hooks 后，需要重启 DotCraft 才能生效。

不想手写时，可在对话中描述需求，让内置 `create-hooks` skill 帮你生成。

### Skills

```text
.craft/skills/dev-guide/
.craft/skills/feature-workflow/
```

复制后保留结构，逐步用项目自己的术语、路径和验收标准替换。

## 故障排查

### 示例配置复制后不生效

确认配置在当前工作区 `.craft/config.json`，并重启相关 Host。启动级字段不会自动热更新，详见 [设置生效层级](../developing/settings-lifecycle.md)。

### 找不到示例源码文件

确认你在 DotCraft 仓库根目录。示例源码位于 `samples/`，文档站页面位于 `docs/`。

### 不知道先跑哪个示例

先按照 [快速开始](../getting-started.md) 完成 Desktop + 模型配置，再按需要选择本页对应的示例。

## 相关入口

- [快速开始](../getting-started.md)
- [Plugins 与工具](../features/plugins-tools.md)
- [配置完整参考](../developing/configuration.md)
