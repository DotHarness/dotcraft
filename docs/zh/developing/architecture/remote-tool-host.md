# 远程工具调用

远程工具调用让 Agent 使用另一台机器上的文件、Shell、LSP 工具及支持远程调用的 .NET 插件工具。远程工具执行端（Remote Tool Host）是那台机器上负责执行的组件。本页介绍不使用 DotCraft Desktop 时的命令行配置和集成方式。

![Agent 机器通过 Hub 将工具调用交给共享电脑上的执行端](/remote-tool-host-topology.svg)


## 职责划分

Agent 机器持有模型循环、审批、hook 和 Session 历史。工作区机器持有真实工作区、本地工具策略和执行审计。远端执行的工具保留原有的工具身份、schema 和 Session 投影，远端化只替换稳定注册背后的运行时路由，所以模型不会看到同一个工具的第二份远端副本。

Agent 机器上的 Hub 是双方的会合点。执行端主动连接 Hub，从不监听入站连接，因此工作区机器不需要入站防火墙规则、端口转发或 TLS 身份。Hub 只在两侧之间转发字节，不解析内容。

两台机器都以登录用户身份运行，不是系统服务，所以两边都需要保持登录。

## 用命令行完成配对

Agent 机器上运行着 DotCraft Hub（`dotcraft hub`），工作区机器需要能访问它的 47600 端口。在 Agent 机器上运行下面的命令生成邀请：

```powershell
dotcraft tool-host invite --name "Ann's workstation"
```

命令会输出邀请链接，以及对方需要执行的完整命令。邀请用主机名标识这台设备。如果对方机器无法解析该名称，生成邀请时直接指定要拨号的地址：

```powershell
dotcraft tool-host invite --host 192.168.1.20 --expires 4
```

`--expires` 以小时为单位设置有效期。邀请只能使用一次。

在持有工作区的机器上运行，把链接和文件夹替换成实际值：

```powershell
dotcraft tool-host setup --name "Ann's workstation"
dotcraft tool-host join http://ann-pc:47600/i/inv_x1y2z3 --workspace C:\workspaces\sample-project
dotcraft tool-host serve
```

`join` 会把长期凭据写入操作系统凭据存储，并输出 Agent 将要使用的 workspace id。`serve` 负责保持控制连接，Hub 重启后它会自行恢复。设置为登录时自动启动：

```powershell
dotcraft tool-host autostart install
```

对于不想碰终端的机器主人，[DotCraft 卫星](../../features/agent-system/satellite)是对应的托盘客户端，用一个安装程序和一个同意窗口取代 `setup`、`join`、`serve` 和 `autostart install`。

## 查看与路由

在 Agent 机器上，`dotcraft tool-host list` 输出与这个 Hub 配对的机器及其 id，`dotcraft tool-host test <machine-id>` 检查其中一台是否在线。在工作区机器上，`dotcraft tool-host workspace list` 输出它导出的文件夹，策略命令则用来查看和修改它允许执行的内容：

```powershell
dotcraft tool-host policy list
dotcraft tool-host policy set Exec needs-approval
```

策略取值为 `allow`、`deny` 或 `needs-approval`，按规范工具名逐个设置。

策略在执行端强制执行。Agent 不能放宽 `deny` 规则，也不能在远端机器上创建永久批准。

不用 Desktop 时，Agent 通过 `RemoteToolHost.List`、`RemoteToolHost.Connect` 和 `RemoteToolHost.Disconnect` 这几个模型工具路由对话：

```text
调用 RemoteToolHost.List，然后用 RemoteToolHost.Connect 把当前对话连接到
<machine-id> 上的 sample-project 工作区。
```

一个工作区同一时间只服务一个 Agent Host。如果它已被占用，请在那一侧断开，或等待它被释放。这里没有排队，也不会抢占。远端失败会如实报告为远端失败，DotCraft 不会把同一次调用静默改到本地绑定重试。

## 本地访问与文件传输

支持远程调用的工具接受 `target: "local"` 或 `target: "remote"`，省略时沿用当前对话的连接。例如，`ReadFile({ "path": "scripts/check.py", "target": "local" })` 可以直接读取 Agent 工作区，无须断开远端连接。`WriteStdin` 应使用创建终端的 `Exec` 所用的 target。

`RemoteToolHost.Transfer` 在两台机器之间直接复制文件或目录：

```json
{
  "direction": "upload",
  "localPath": "tools/checker",
  "remotePath": "tools/checker",
  "overwrite": false
}
```

使用 `download` 从远端复制到本地。路径是相对于各自工作区的路径或绝对路径，指向确切的目标位置。目录合并会保留多余文件，替换已有文件须设置 `overwrite: true`。中途失败时，结果会报告已完成的文件数和字节数，未完成的文件会被丢弃。Plan 模式不允许显式传输。

传输复用现有连接，并遵循两端的文件访问策略。它拒绝文件系统链接，校验 SHA-256，并逐个文件原子提交。执行端的 `Tools.File.MaxTransferBytes` 默认为 10 GiB，与文本读取限制独立。提交响应丢失时会报告结果未知，不会自动重试。

## Skill 与插件资源

支持远程调用的 .NET 插件工具使用 Agent 已接纳的插件包和生效配置。Connect 自动在远端工作区准备完整插件包及其 .NET 依赖，包括延迟工具，无需在远端另行安装插件。插件发生变化时，会在下一 Turn 使用工具快照之前完成准备。MCP 和运行时动态工具仍在本地执行。

在工作区优先授权下，远端所有者需要批准确切的插件指纹，随后才会加载插件代码。完全访问授权允许自动激活。断开连接会释放当前线程的插件状态，工作区最后一个租约结束时会等待插件运行时停止。已校验的插件文件会保留供后续复用。

Skill 保留在 Agent 机器上。`SkillView` 读取本地指令，Skill 目录提供实际生效的本地路径，包括变体路径。连接远端时，可以通过 `ReadFile` 的 `target: "local"` 读取配套文件。

Agent 按需使用 `Transfer` 复制远端需要的脚本、目录或 CLI 文件，保留相对依赖关系，并在适用时选用 Skill 的生效变体。传输的文件在断开后保留，目标位置和覆盖行为由 Agent 显式选择。可执行文件须支持远端操作系统。

Connect 协商工具和文件传输能力后才发布路由，连接建立失败时保留之前的路由。不支持文件传输的卫星需要先升级再连接。

## 解除配对

```powershell
dotcraft tool-host revoke <machine-id>
```

在 Agent 机器上执行会把对端从 Hub 移除并关闭连接。在工作区机器上执行会删除本地配对并让 `serve` 退出。任意一侧执行都足够，保存的凭据会一并删除。

## 相关文档

- [DotCraft 卫星](../../features/agent-system/satellite) —— 在 Desktop 里完成同一套配对
- [架构总览](./overview) —— Hub 和 Agent Host 在整个运行时中的位置
