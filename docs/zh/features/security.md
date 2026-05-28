# 安全与沙箱

DotCraft 的安全模型有四层：**文件黑名单**、**工作区边界**、**工具能力开关**和**沙箱隔离**。大多数本地项目只需要默认策略加少量敏感路径。公开部署和外部渠道接入应按严格部署清单逐项检查。

## 默认安全 baseline

创建工作区后：

- 工作区外的文件和 Shell 操作需要审批。
- 黑名单初始为空，需要按你的机器补充凭据和密钥目录。
- 内置工具默认可用，除非你主动收紧工具表面积。
- 沙箱隔离默认关闭，需要时再显式开启。

准确字段名、默认值和 JSON 示例见 [Tools, Security 与 Sandbox](../developing/configuration.md#tools-security-与-sandbox)。

## 文件访问黑名单

黑名单列出 Agent 绝不能访问的路径。它对 CLI、Desktop、外部渠道和自动化入口都生效。

黑名单行为：

- 访问黑名单路径的文件读取、写入、编辑、搜索会被拒绝。
- 引用黑名单路径的 Shell 命令会被拒绝。
- 黑名单优先于工作区边界审批。
- 支持绝对路径和用户主目录展开，也会检查子路径。

## 工作区边界

DotCraft 会在执行 Shell 前解析命令中的路径，覆盖 Unix 绝对路径、用户主目录、环境变量、Windows 盘符路径、UNC 路径和常见安全设备路径。

如果命令解析到工作区外，DotCraft 会按工作区策略直接拒绝或向当前交互源发起审批。文件工具使用同样的展开规则，保证文件和 Shell 行为一致。

## 工具能力开关

工具策略决定哪些内置工具可见、工作区外文件和 Shell 是否需要审批、文件/Web 响应上限是多少，以及是否启用 LSP 或沙箱。

需要精确 allow-list、Web 搜索 provider、超时、输出上限或沙箱设置时，请查看 [Tools, Security 与 Sandbox](../developing/configuration.md#tools-security-与-sandbox)。

## Hooks

Hooks 允许 DotCraft 在生命周期事件中运行外部命令，适合格式化、审计、通知、环境检查和安全守卫。建议先用观察型 Hook 看清行为，再加入阻塞型 Hook。

Hook 配置、生命周期事件、stdin payload、matcher 规则、退出码语义和示例都放在 [Automations、Goals 与 Hooks](../developing/configuration.md#automations-goals-与-hooks)。

最佳实践：

- Hook 脚本保持短小，复杂逻辑放到项目脚本里。
- 阻塞型 Hook 要输出清晰错误信息。
- 不要提交密钥，优先使用环境变量或全局配置。
- 使用工作区相对命令路径，避免不同入口 cwd 不一致。

## 沙箱（OpenSandbox）

[OpenSandbox](https://github.com/alibaba/OpenSandbox) 可以把 Shell 和 File 工具执行隔离在 Docker 容器中。工作区暴露给 bot、共享服务器或不可信任务队列时尤其有用。

OpenSandbox 服务前置条件和所有沙箱字段见 [Tools, Security 与 Sandbox](../developing/configuration.md#tools-security-与-sandbox)。

## 严格部署清单

当 DotCraft 暴露给外部渠道或公网时，建议同时启用这些策略：

| 区域 | 建议 |
|---|---|
| 工作区边界 | 工作区外文件和 Shell 操作必须审批 |
| 黑名单 | 禁止访问密钥和凭据目录 |
| 工具表面积 | 只保留部署所需工具 |
| AppServer | 远程访问使用强随机 WebSocket token |
| 沙箱 | 需要进一步隔离时启用 OpenSandbox |
| SubAgents | 除非明确需要，否则限制递归委派 |

## 使用场景

| 场景 | 推荐 |
|---|---|
| 个人本地项目 | 保留工作区外审批；把 SSH、云凭据、密码管理器目录加入黑名单 |
| 团队共享工作区 | 把安全策略放进工作区 `.craft/config.json`，所有入口统一执行 |
| 外部渠道或 bot | 开启审批，收紧工具，使用强 token |
| 自动化任务 | 按任务需要开启沙箱或收紧工具表面积 |

## 故障排查

### 命令明明在工作区内仍被拒绝

检查命令字符串是否还引用了工作区外路径，例如用户主目录、`/etc`、`C:\Users`，或会展开到工作区外的环境变量。

### 沙箱无法启动

确认 Docker 和 `opensandbox-server` 正在运行，再到配置参考里检查沙箱域名、API key 和网络策略。

### Web 搜索或抓取失败

检查搜索 provider、超时、响应大小上限和网络环境。

### Hook 没有执行

确认 Hooks 已启用、事件名正确、matcher 能匹配当前工具名，并且命令路径能在当前工作区执行。

### Hook 超时

增加 Hook 超时，或把慢任务放到后台队列。Hooks 适合短检查，不适合长任务。

### Hook 修改不了工具参数

这是设计限制。Hooks 只负责观察或阻塞。

## 相关入口

- [可观测性](./observability.md) — 在 Dashboard 查看审批和拦截记录
- [SubAgents](./subagents.md) — 用 role 工具策略限制委派任务
- [示例与模板](../resources/samples.md) — Hooks 模板
- [配置完整参考](../developing/configuration.md) — 完整安全和工具字段
