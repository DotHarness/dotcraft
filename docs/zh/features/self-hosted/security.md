# 安全与沙箱

DotCraft 用四层护栏约束 Agent 能碰到什么：文件黑名单、工作区边界、工具能力开关和沙箱隔离。本地个人项目用默认策略再补几条敏感路径就够了。要把 DotCraft 暴露给外部渠道或公网，照下面的严格部署清单逐项过一遍。

![DotCraft 安全护栏示意图](/security-guardrails-overview.svg)

## 默认安全基线

新建一个工作区，护栏是这样的：

- 工作区外的文件和 Shell 操作需要审批。
- 黑名单是空的，你机器上的凭据和密钥目录得自己加。
- 内置工具全部可用，除非你主动收紧。
- 沙箱隔离关闭，需要时再开。

这几项对应的字段名、默认值和 JSON 示例都在 [Tools, Security 与 Sandbox](../../developing/configuration#tools-security-与-sandbox)。

## 文件黑名单

黑名单列出 Agent 绝不能碰的路径，对 CLI、Desktop、外部渠道和自动化任务一视同仁。

读取、写入、编辑、搜索这些路径的操作会被拒绝，引用了它们的 Shell 命令同样被拒绝。黑名单的优先级高于工作区边界的审批，命中的路径不会走审批流程，直接拒绝。写绝对路径或以 `~` 开头的路径都可以，子路径一并覆盖。

## 工作区边界

执行 Shell 命令前，DotCraft 会把命令里引用的路径全部展开再判断：Unix 绝对路径、`~` 开头的主目录路径、环境变量、Windows 盘符路径，以及 `\\server\share` 这样的 UNC 路径。

只要解析结果落在工作区外，DotCraft 就按工作区策略直接拒绝，或者向当前的交互入口发起审批。文件工具用的是同一套展开规则，所以文件操作和 Shell 命令的判断结果一致。

## 工具能力开关

工具策略决定哪些内置工具对 Agent 可见、工作区外的文件和 Shell 操作是否需要审批、文件和 Web 响应最多返回多少内容，以及是否启用 LSP 和沙箱工具。需要精确的 allow-list、Web 搜索 provider、超时或输出上限时，在 [Tools, Security 与 Sandbox](../../developing/configuration#tools-security-与-sandbox) 里查字段。

## Hooks

Hooks 把安全检查变成会话生命周期上的关卡：命令执行前先检查一遍、工具调用后审阅改动，或者在高风险操作前停下来等你点头。概念说明见[生命周期 Hooks](../agent-system/hooks)，事件、matcher 规则和退出码语义见[配置参考](../../developing/configuration#automations-goals-与-hooks)。

写 Hook 时：

- 脚本保持短小，复杂逻辑放进项目自己的脚本。
- 阻塞型 Hook 一定要打印清楚的错误信息，否则你只会看到操作被挡下，不知道为什么。
- 不要把密钥写进 Hook，用环境变量或全局配置。
- 命令路径写成工作区相对路径，不同入口的 cwd 并不一致。

## 沙箱（OpenSandbox）

[OpenSandbox](https://github.com/alibaba/OpenSandbox) 把 Shell 和 File 工具的执行放进 Docker 容器。工作区要暴露给 bot、共享服务器或不可信的任务队列时，这一层最有用。

它需要一个 OpenSandbox 服务，前置条件和全部沙箱字段见 [Tools, Security 与 Sandbox](../../developing/configuration#tools-security-与-sandbox)。

## 严格部署清单

DotCraft 暴露给外部渠道或公网时，这些策略建议一起开：

| 区域 | 建议 |
|---|---|
| 工作区边界 | 工作区外文件和 Shell 操作必须审批 |
| 黑名单 | 禁止访问密钥和凭据目录 |
| 工具表面积 | 只保留部署所需工具 |
| AppServer | 远程访问使用强随机 WebSocket token |
| 沙箱 | 需要进一步隔离时启用 OpenSandbox |
| Subagents | 除非明确需要，否则限制递归委派 |

## 使用场景

| 场景 | 推荐 |
|---|---|
| 个人本地项目 | 保留工作区外审批，把 SSH、云凭据、密码管理器目录加入黑名单 |
| 团队共享工作区 | 把安全策略放进工作区 `.craft/config.json`，所有入口统一执行 |
| 外部渠道或 bot | 开启审批，收紧工具，使用强 token |
| 自动化任务 | 按任务需要开启沙箱或收紧工具表面积 |

## 相关文档

- [可观测性](./observability) — 在 Dashboard 回看审批和拦截记录
- [Subagents](../agent-system/subagents) — 用角色的工具策略约束委派出去的任务
