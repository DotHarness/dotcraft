# Remote Tool Host

Remote Tool Host 让一台设备上的 Agent 在另一台设备的工作区中运行符合条件的文件、Shell 和 LSP 工具。Agent 设备仍负责模型、对话、审批和工具历史。Tool Host 持有真实工作区，并在本机执行工具。

![Agent Runtime 保留模型循环和工具身份，Remote Tool Host 在目标工作区旁执行符合条件的 Core 文件、Shell 与 LSP 工具](/remote-tool-host-topology.svg)

## 适用场景

当 Agent 设备不应该保存项目 checkout 或本地工具链时，可以使用 Remote Tool Host。常见组合是一台负责对话和模型访问的 Agent 设备，加上一台已经准备好代码仓库、构建工具、Shell 环境和 language server 的开发工作站。

## 准备条件

你需要：

- 两台 Windows 设备都安装 DotCraft，并使用兼容的 Remote Tool Host 协议和工具契约。
- Tool Host 上有一个 Agent 设备能够访问的 HTTPS endpoint，包含明确端口。
- Tool Host 上已有一个绝对路径的工作区目录。
- 能够把一份 pairing 文件从 Tool Host 安全传到 Agent 设备。

v1 的自启动以当前 Windows 用户身份运行。该用户必须保持登录，Tool Host 才能在线。

## 配置 Tool Host 设备

在持有工作区的设备上执行一次 setup。把 endpoint 和路径替换成该设备的实际值：

```powershell
dotcraft tool-host setup https://tool-host.example:7443 --output .\tool-host.pairing.json
dotcraft tool-host workspace add sample-project C:\workspaces\sample-project
dotcraft tool-host status
```

Endpoint 是必填项，因为它决定向 Agent 设备公布的地址，以及生成的 TLS 证书身份。这里的 `sample-project` 是 Agent 使用的稳定 workspace id，不会从目录名自动推导。

要在下次登录时自动启动 Host：

```powershell
dotcraft tool-host autostart install
```

要立即测试，请在一个终端中保持下面的命令运行：

```powershell
dotcraft tool-host serve
```

用 `dotcraft tool-host policy list` 查看本地策略。Tool Host 管理员可以修改一个符合条件的工具：

```powershell
dotcraft tool-host policy set Exec needs-approval
```

策略在 Tool Host 上强制执行。Agent 不能放宽 `deny` 规则，也不能创建永久批准。

## 配对 Agent 设备

通过安全渠道传输 `tool-host.pairing.json`，然后在运行 Agent 的设备上注册：

```powershell
dotcraft tool-host register .\tool-host.pairing.json
dotcraft tool-host list
dotcraft tool-host test <host-id>
```

Setup 会输出 host id，`tool-host list` 也会再次显示。Pairing 文件包含 bearer token。注册后删除所有传输副本。Agent 会把 token 保存到操作系统凭据存储中。

如果 `setup` 或 `token rotate` 没有指定输出路径，DotCraft 会在当前目录生成以 host id 命名的 pairing 文件。Token rotate 会立即撤销所有旧注册，因此在重新依赖该 Host 前，需要分发并注册新的 pairing 文件。

## 连接一段对话

让 Agent 列出已注册的 Host，并把当前对话连接到工作区：

```text
调用 RemoteToolHost.List，然后用 RemoteToolHost.Connect 把当前对话连接到
<host-id> 上的 sample-project 工作区。
```

`Connect` 成功后，现有的文件、Shell 和 LSP 工具名会路由到该工作区。模型不会同时看到本地版和远端版工具。路由只属于当前对话，DotCraft 重启后不会恢复。

先核对 `Connect` 返回的执行目标，再让 Agent 读取一个已知文件或运行无副作用的工作区命令。要恢复本地执行，让它调用 `RemoteToolHost.Disconnect`。网络异常不会让同一次操作静默改到本机执行。

## 故障排查

### Host offline

在 Tool Host 上运行 `dotcraft tool-host status`，再启动 `dotcraft tool-host serve`。确认本机防火墙允许访问配置的 HTTPS hostname 和端口。如果安装了自启动，还要确认对应用户已经登录。

### Workspace busy

另一个 Agent Host 正在占用该工作区。请在那台 Agent Host 上断开连接，或者等待丢失的 lease 过期。Remote Tool Host 不会排队，也不会抢占其他 Agent Host 持有的工作区。

### Certificate mismatch

不要绕过警告。在 Tool Host 上用 `dotcraft tool-host status` 查看 fingerprint，并与预期的 pairing 信息核对。如果该 Host 确实重新执行过 setup，请注销旧 host id，再注册一份重新安全传输的 pairing 文件。

## 相关文档

- [插件与工具](./plugins-tools) — 了解 Agent 能力的其他来源
- [安全与沙箱](../self-hosted/security) — 查看工作区边界、审批和执行策略
