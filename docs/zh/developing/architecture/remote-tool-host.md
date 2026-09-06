# Remote Tool Host

Remote Tool Host 在同一内网的另一台机器上执行 Agent 的文件、Shell 和 LSP 工具。本页面向不使用 DotCraft Desktop、直接配置配对的集成方和运维人员。

![Agent Runtime 保留模型循环和工具身份，Remote Tool Host 在目标工作区旁执行符合条件的 Core 文件、Shell 和 LSP 工具](/remote-tool-host-topology.svg)


## 职责划分

Agent 机器持有模型循环、审批、hook 和 Session 历史。工作区机器持有真实工作区、本地工具策略和执行审计。远端执行的工具保留原有的工具身份、schema 和 Session 投影，远端化只替换稳定注册背后的运行时路由，所以模型不会看到同一个工具的第二份远端副本。

Agent 机器上的 Hub 是双方的会合点。Remote Tool Host 向它拨号，从不监听入站连接，因此工作区机器不需要入站防火墙规则、端口转发或 TLS 身份。Hub 只在两侧之间转发字节，不解析内容。

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

策略在 Tool Host 上强制执行。Agent 不能放宽 `deny` 规则，也不能在远端机器上创建永久批准。

不用 Desktop 时，Agent 通过 `RemoteToolHost.List`、`RemoteToolHost.Connect` 和 `RemoteToolHost.Disconnect` 这几个模型工具路由对话：

```text
调用 RemoteToolHost.List，然后用 RemoteToolHost.Connect 把当前对话连接到
<machine-id> 上的 sample-project 工作区。
```

一个工作区同一时间只服务一个 Agent Host。如果它已被占用，请在那一侧断开，或等待它被释放。这里没有排队，也不会抢占。远端失败会如实报告为远端失败，DotCraft 不会把同一次调用静默改到本地绑定重试。

## 解除配对

```powershell
dotcraft tool-host revoke <machine-id>
```

在 Agent 机器上执行会把对端从 Hub 移除并关闭连接。在工作区机器上执行会删除本地配对并让 `serve` 退出。任意一侧执行都足够，保存的凭据会一并删除。

## 相关文档

- [DotCraft 卫星](../../features/agent-system/satellite) —— 在 Desktop 里完成同一套配对
- [架构总览](./overview) —— Hub 和 Agent Host 在整个运行时中的位置
