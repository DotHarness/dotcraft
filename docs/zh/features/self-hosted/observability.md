# 可观测性

DotCraft 的 Dashboard 是一个网页，用来看清 Agent 刚才到底做了什么。会话轨迹、工具调用、配置合并结果和审批记录都留在这里。出了问题不用翻日志，打开页面对着时间线一步步看就行。

![所有入口的会话都会把 trace 事件送进 Dashboard 窗口：Trace Timeline 页面按时序排列 Agent、工具与错误事件，下方是审批记录和配置合并结果](/observability-trace-overview.svg)

## 打开 Dashboard

先在工作区配置中启用 Dashboard，字段名和 JSON 示例见[入口与服务](../../developing/configuration#entry-points-and-services)。然后启动它：

```bash
dotcraft dashboard
```

默认地址是 `http://127.0.0.1:8080/dashboard`。首页给出运行摘要、各入口状态和近期活动。从 CLI、Desktop 或任何一个入口发起一次对话，数据随即出现。

`dotcraft dashboard` 读的是已经落盘的记录。想看正在运行的自动化和外部渠道状态，就让 AppServer 托管 Dashboard。

Dashboard 默认只监听本机。改成监听外部地址后，同一网络里的人都能打开它。

> [!CAUTION]
> Dashboard 会展示 prompt、项目指令及其来源路径、工具参数和工具结果。暴露到公网前，请先确认网络边界与认证策略。

## 每一页回答什么问题

### 模型有没有正常输出

触发一次会话，打开 **Trace Timeline**。事件按时间排开：模型的输出、每一次工具调用和它的返回、以及中途报的错。看一遍就知道卡在哪一步。

如果模型完全没有输出，多半是 Provider 凭据或 Endpoint 不匹配，去 **Settings** 页面对照合并后的 Provider 配置。**Provider** 过滤器还会显示重试过程——试了几次、每次什么结果、为什么停下来。

### 工具调用为什么失败或被拦下

在 **Sessions** 里打开会话详情，切到 **Tools** 或 **Errors** 过滤器，点开具体一次调用，参数、返回、耗时和 stderr 都在里面。

如果是审批没通过，**Approvals** 页面记着每一次需要审批的调用：哪个入口发起的、批了还是拒了、依据是你的决定还是工作区策略或 Hook。策略本身怎么配见[安全与沙箱](./security)。

### 配置为什么是这个值

**Settings** 页面把全局 `~/.craft/config.json` 和工作区 `.craft/config.json` 并排展开，告诉你字段在哪一层定义、合并后哪个值生效、哪些字段改完需要重启。

改了配置没生效时，先在这里确认它属于即时生效、子系统重启还是 AppServer 重启，再看[设置生效层级](../../developing/lifecycle/settings-lifecycle)。

### Agent 用的是哪份项目指令

**Instructions** 过滤器显示这次会话实际带上的 `AGENTS.md` 内容和它的来源文件。它是线程捕获的快照，线程重新加载项目指令时才更新，不是磁盘文件的实时预览。

### 自动化和梦境跑得怎么样

**Automations** 页面列出由 AppServer 托管的本地任务和 Cron，以及它们当前的活动状态。**Dreams** 页面用来审阅后台生成的梦境，决定应用还是丢弃。

## 自己消费这些事件

想把 Trace 事件接进自己的面板，[Dashboard API](../../developing/protocols/dashboard-api) 列出了 HTTP 端点和事件类型。Dashboard 渲染的就是这份数据，AppServer 协议在 Wire Protocol 上推送的也是同一份。

## 相关文档

- [安全与沙箱](./security) — 哪些操作需要审批，Dashboard 里的拦截记录从哪来
- [服务器部署](./server-deployment) — 把 Dashboard 和 AppServer 一起部署到服务器上
