# Remote Tool Host

Remote Tool Host 让一台设备上的 Agent 在另一台设备的工作区中运行符合条件的文件、Shell 和 LSP 工具。Agent 设备仍负责模型、对话、审批和工具历史。Tool Host 持有真实工作区，并在本机执行工具。

![Agent Runtime 保留模型循环和工具身份，Remote Tool Host 在目标工作区旁执行符合条件的 Core 文件、Shell 与 LSP 工具](/remote-tool-host-topology.svg)

## 适用场景

当 Agent 设备不应该保存项目 checkout 或本地工具链时，可以使用 Remote Tool Host。常见组合是一台负责对话和模型访问的 Agent 设备，加上一台已经准备好代码仓库、构建工具、Shell 环境和 language server 的同事工作站。

工作区所在的机器只向外拨号。那台机器上不需要开放入站端口、传输证书，也不需要改配置文件。

## 准备条件

你需要：

- 两台 Windows 设备都安装 DotCraft，并使用兼容的 Remote Tool Host 协议和工具契约。
- Agent 设备上运行着 DotCraft Hub（`dotcraft hub`），它是两台机器的会合点。
- 工作区所在的机器能访问 Agent 设备的 47600 端口。
- 工作区所在的机器上已有一个准备共享的文件夹。

两台机器都需要保持登录状态：Remote Tool Host 以登录用户身份运行，不是系统服务。

## 邀请持有工作区的机器

在 Desktop 里打开**设置 → 连接 → 卫星**，点**邀请**。弹出的对话框会问你需要这台机器做什么、希望在它的哪个文件夹里工作——两项都是可选的，也都会展示给被邀请的人。生成链接后在同一个对话框里复制并发给对方，再点**完成**。想接着邀请下一台机器，点**再建一个**，不用离开对话框。链接只能使用一次，24 小时后过期。

不用 Desktop 时，在 Agent 设备上运行：

```powershell
dotcraft tool-host invite --name "Ann 的工作站"
```

命令会输出同一条链接，以及对方需要执行的完整命令。

第一次邀请会打开配对端口，Windows 会询问一次是否允许 DotCraft 通过防火墙，请在专用网络上允许。

邀请用主机名标识这台设备。如果对方机器无法解析该名称，可以在生成邀请时指定要拨号的地址：`dotcraft tool-host invite --host 192.168.1.20`。

## 加入并保持在线

在持有工作区的机器上运行，把链接和文件夹替换成实际值：

```powershell
dotcraft tool-host setup --name "Ann 的工作站"
dotcraft tool-host join http://ann-pc:47600/i/inv_x1y2z3 --workspace C:\workspaces\sample-project
dotcraft tool-host serve
```

`join` 会把长期凭据写入操作系统凭据存储，并输出 Agent 将要使用的 workspace id。`serve` 负责保持连接，让它一直运行，或者设置为登录时自动启动：

```powershell
dotcraft tool-host autostart install
```

Agent 设备上的 Hub 重启后，这台机器不需要任何操作，连接会在数秒内恢复。

用 `dotcraft tool-host policy list` 查看本地策略。Tool Host 管理员可以修改一个符合条件的工具：

```powershell
dotcraft tool-host policy set Exec needs-approval
```

策略在 Tool Host 上强制执行。Agent 不能放宽 `deny` 规则，也不能创建永久批准。

同事其实可以完全不碰这些命令：[DotCraft 卫星](./satellite)从邀请链接安装，用一个同意窗口和一个托盘图标取代 `join`、`serve` 和 `autostart install`。

## 查看可用的机器

**设置 → 连接 → 卫星**会列出所有加入过的机器，并标注**待命**、**使用中**或**离线**。点进一台可以看到它共享的文件夹，以及最近发生的事。不用 Desktop 时，`dotcraft tool-host list` 会输出同样的机器列表和它们的 id。

## 选择对话在哪里执行

输入框里的**执行位置**决定当前对话的工具在哪台机器上运行。它提供**本机**，以及每台已配对机器的每个文件夹，别人正在使用的文件夹和离线的机器都会标出来。选中之后，现有的文件、Shell 和 LSP 工具名会路由过去，模型不会同时看到本地版和远端版工具。Desktop 会记住这个对话的选择，下次打开时只要机器在线、文件夹空闲就会自动恢复。

不用 Desktop 时，让 Agent 把对话路由过去：

```text
调用 RemoteToolHost.List，然后用 RemoteToolHost.Connect 把当前对话连接到
<machine-id> 上的 sample-project 工作区。
```

一个工作区同一时间只服务一个 Agent Host。如果文件夹已被占用，说明另一个 Agent Host 正在使用它，请在那台机器上断开，或等待它的 lease 过期。这里没有排队，也不会抢占。

要恢复本地执行，选回**本机**，或者让 Agent 调用 `RemoteToolHost.Disconnect`。网络异常不会让同一次操作静默改到本机执行。

## 解除配对

任意一方都可以解除，保存的凭据会一并删除。在 Desktop 里打开**设置 → 连接 → 卫星**中的那台机器，从状态菜单里选**移除**。不用 Desktop 时：

```powershell
dotcraft tool-host revoke <machine-id>
```

在 Agent 设备上执行会把该机器从 Hub 移除并关闭连接。在工作区所在的机器上执行会删除本地配对并让 `serve` 退出。

## 相关文档

- [插件与工具](./plugins-tools) — 了解 Agent 能力的其他来源
- [安全与沙箱](../self-hosted/security) — 查看工作区边界、审批和执行策略
