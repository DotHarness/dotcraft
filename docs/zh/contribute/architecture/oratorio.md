# Oratorio 集成

本文面向 DotCraft 贡献者，说明内置 [Oratorio](../../guide/oratorio/) 工作流的集成边界。

![Oratorio 集成边界：Desktop Renderer 与内置桌面扩展位于 typed IPC 边界一侧，始终拿不到 endpoint 与凭据。Desktop Main 在另一侧持有两者，并且只有 Main 与 Oratorio Server 交换请求和事件，而 Hub 只负责启动、从不代理](/oratorio-boundaries-topology.svg)

## 组件边界

| 组件 | 职责 |
| --- | --- |
| **Oratorio Server** | 管理任务、运行、草稿、来源同步、工作树、决策、设置与实时事件。 |
| **DotCraft Hub** | 启动并监管已注册的用户级 `oratorio` managed service，不代理 Oratorio 业务请求。 |
| **Desktop Main** | 解析本地或远程服务访问、注入 bearer、校验允许的 route，并持有实时连接。 |
| **Desktop Renderer** | 通过 typed IPC 渲染 Board、任务详情和 Settings，不获取 endpoint 或 bearer。 |
| **内置桌面扩展** | 通过公共桌面扩展激活契约注册 Oratorio 视图与 Settings page。 |

本地 Desktop 会在首次使用时请求 Hub 确保随包提供的 Server 已运行。远程 Desktop 则从当前 [DotCraft Stack](../../self-hosting/server-deployment) 解析 Oratorio 服务。两种模式下，Renderer 请求都经过同一个 Main process 边界。

App connection handoff 在 Main 中检查，并要求用户明确批准。用户为 Thread 启用 Oratorio 后，Main 会把 bind handoff 作为技术激活直接交给 managed service，并将激活失败返回发起流程。Renderer 只在 connection consent 时接收 request ID 与脱敏摘要。

## GitHub 讨论执行

仓库评论扫描与可选的 Webhook 共用 mention 接收流程。命令审计记录保存目标类型和编号。派发前会刷新对应的 issue 或 pull request，再创建 Discussion Turn，尚未进入 Board 的 issue 也会被导入。回复复用 source write 日志和隐藏回复标记，避免重试时重复发布。

独立的 issue 讨论会向 GitHub 查询默认分支，使用 App installation 凭据拉取该分支，再以取得的 SHA 准备按 item 管理的托管工作树。拉取与 SHA 解析在仓库锁内执行。空闲的独立讨论恢复时会刷新目标。正在运行的会话和实现 Thread 保持原有执行上下文。来源同步更新的是问题跟踪数据，不会切换或拉取基础 checkout。

提问包含 issue 标题、正文、URL 和讨论历史，准备独立讨论时还会提供实际分支、SHA 与执行路径。提问不创建 round、run 或实现交付。[GitHub 接入指南](../../guide/oratorio/github)说明了协作者的使用方式。

## 开发与验证

在仓库根目录构建 Server 并运行专用测试：

```bash
dotnet build src/DotCraft.Oratorio/DotCraft.Oratorio.csproj
dotnet test tests/DotCraft.Oratorio.Tests/DotCraft.Oratorio.Tests.csproj
```

在 `desktop/` 目录运行 Desktop 检查：

```bash
npm test
npm run build
```

打包会把 self-contained Server 发布到 `build/oratorio/`，再放进 Desktop resources。本机 Windows 打包运行 `build.bat`，各发行平台的产物由仓库的 `build-multiplatform` workflow 生成。

Oratorio 领域行为应保留在 Server 中。Desktop view model 可以格式化显示数据，但不得重新实现 lifecycle、retry、recovery、工作树或 decision 规则。

## 相关文档

- [Hub 协议](../../extend/protocols/hub)——Desktop 用来确保 `oratorio` managed service 已运行的接口。
- [开发桌面扩展](../../extend/desktop-extensions)——内置插件注册 Oratorio 视图所用的激活契约。
- [DotCraft App](../../extend/app-binding)——Main 转交的 connect 与 bind handoff 背后的授权模型。
