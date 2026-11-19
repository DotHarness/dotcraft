# 可观测性

DotCraft 的 Dashboard 是一个网页，用来看清 Agent 刚才到底做了什么。会话轨迹、工具调用、配置合并结果和审批记录都留在这里。出了问题不用翻日志，打开页面对着时间线一步步看就行。

![所有入口的会话都会把 trace 事件送进 Dashboard 窗口：Trace Timeline 页面按时序排列 Agent、工具与错误事件，下方是审批记录和配置合并结果](/observability-trace-overview.svg)

## 打开 Dashboard

在 Desktop 中，选择 **视图 → 打开 Dashboard**，或按 **Ctrl+Shift+D**（macOS 上是 **⌘⇧D**）。也可以从托盘打开。用页面顶部的选择器切换正在运行的本地工作区。

要在独立查看器中查看已保存的 trace，运行：

```bash
dotcraft dashboard
```

打开 `http://127.0.0.1:8080/dashboard`。要在自托管服务器上查看实时诊断，见[服务器部署](./server-deployment)。

## 每一页回答什么问题

### 模型有没有正常输出

触发一次会话，然后在 **Sessions** 里打开它。时间线按回合依次排开事件：每一次带编号的模型请求、每一次工具调用和它的返回、以及中途报的错。看一遍就知道卡在哪一步。在顶部的时间条上拖选一段，可以只看那段时间里发生的事。

如果模型完全没有输出，多半是 Provider 凭据或 Endpoint 不匹配，去 **Settings** 页面对照合并后的 Provider 配置。**Provider** 过滤器还会显示重试过程——试了几次、每次什么结果、为什么停下来。

### 工具调用为什么失败或被拦下

在 **Sessions** 里打开会话，切到 **Tools** 或 **Errors** 过滤器，选中具体一次调用，参数、返回、耗时和 stderr 会在时间线旁边展开。

如果是审批没通过，**Approvals** 页面记着每一次需要审批的调用：哪个入口发起的、批了还是拒了、依据是你的决定还是工作区策略或 Hook。策略本身怎么配见[安全](./security)。

### 配置为什么是这个值

**Settings** 页面把全局 `~/.craft/config.json` 和工作区 `.craft/config.json` 并排展开，告诉你字段在哪一层定义、合并后哪个值生效、哪些字段改完需要重启。

改了配置没生效时，先在这里确认它属于即时生效、子系统重启还是 AppServer 重启，再看[设置生效层级](../configuration/settings-lifecycle)。

### Agent 用的是哪份项目指令

**Instructions** 过滤器显示这次会话实际带上的 `AGENTS.md` 内容和它的来源文件。它是线程捕获的快照，线程重新加载项目指令时才更新，不是磁盘文件的实时预览。

### 自动化和梦境跑得怎么样

**Automations** 页面列出由 AppServer 托管的自动化，以及它们当前的活动状态。**Dreams** 页面用来逐个文件审阅每次梦境运行提出的记忆改动，决定应用还是丢弃。已应用的运行有问题时，可以把它替换掉的记忆恢复回来。

## 自己消费这些事件

想把 Trace 事件接进自己的面板，[Dashboard API](../extend/protocols/dashboard-api) 列出了 HTTP 端点和事件类型。Dashboard 渲染的就是这份数据，AppServer 协议在 Wire Protocol 上推送的也是同一份。

## 相关文档

- [安全](./security) — 哪些操作需要审批，Dashboard 里的拦截记录从哪来
- [服务器部署](./server-deployment) — 把 Dashboard 和 AppServer 一起部署到服务器上
