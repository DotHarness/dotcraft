# DotCraft Workspace 示例

**中文 | [English](./README.md)**

这个示例提供了一个基于源码仓库的 DotCraft workspace，你可以直接在当前仓库副本中运行它，适合开发机或云服务器上的长期工作区场景。

它包含：

- `start.sh`：从当前仓库构建并启动 DotCraft
- `start-sandbox.sh` 和 `start-sandbox.ps1`：启动可选的 OpenSandbox 服务
- `config.template.json`：安全的 workspace 配置示例

## 开始前先说明

对大多数第一次使用 DotCraft 的用户，更安全的默认路径仍然是：

```bash
cd /path/to/your-project
dotcraft
```

当 DotCraft 在一个全新的 workspace 中启动时，它可以帮你初始化 `.craft/`，并在配置缺失时自动打开 setup-only Dashboard。

只有当你明确想要一个可复用的源码工作区，或者希望在服务器上保留一套长期运行的 workspace 时，才更适合使用 `samples/workspace`。

如果你的目标是生产式服务器机器人部署，优先使用新的 [Docker 服务器部署](../../docs/zh/developing/server-deployment.md)。它会把 AppServer、TypeScript 渠道模块和可选 OpenSandbox 打包到 Compose 流程里，不需要手动分别启动 sandbox helper 和渠道适配器。

## 示例内容

| 文件 | 用途 |
|------|------|
| `start.sh` | 从当前仓库构建 DotCraft，并在 `samples/workspace` 中启动 CLI |
| `start-sandbox.sh` | 在 Linux / macOS 上启动 OpenSandbox |
| `start-sandbox.ps1` | 在 Windows PowerShell 上启动 OpenSandbox |
| `config.template.json` | 可作为起点复制到你自己 workspace 中的安全示例配置 |
| `.craft/config.json` | 你本地创建并使用的真实工作区配置；由于可能包含私密数据，不会随仓库一起分发 |
| `.craft/dashboard/*` | 本地运行后产生的 Dashboard 运行产物，不会随仓库一起分发 |
| `.craft/sessions/*` | 本地运行后产生的会话产物，不会随仓库一起分发 |

## 前置条件

### 推荐默认路径

- 你的机器上已经安装或构建好 DotCraft
- 你有一个实际要使用的项目目录作为 workspace

### 如果你要使用 `start.sh`

- Bash
- .NET 10 SDK
- 当前仓库已经 clone 到本地
- 如果想在 Linux 服务器上后台运行，还需要 `screen`

### 如果你要使用可选的 sandbox 脚本

- Docker 已启动，并且当前 shell 可以访问
- OpenSandbox Server 已安装，且可通过 `PATH` 找到
- 默认存在 `~/.sandbox.toml` 基础配置，或通过 `SANDBOX_CONFIG_PATH` 指向该文件
- Windows：PowerShell 和 `python`
- Linux / macOS：`bash` 和 `python3`

## 快速开始

### 方式一：推荐给新用户

在你的真实项目目录里运行 DotCraft，并让初始化流程自动创建 `.craft/`：

```bash
cd /path/to/your-project
dotcraft
```

### 方式二：直接运行本示例 workspace

```bash
cd samples/workspace
mkdir -p .craft
cp config.template.json .craft/config.json
bash start.sh
```

如果你不想先复制模板，也可以直接运行 `bash start.sh`，让 DotCraft 首次启动时自行初始化 `.craft/`。但如果你想复用这个 sample 里推荐的 Dashboard / Sandbox 示例字段，还是应该先从 `config.template.json` 复制。

如果你在 Linux 服务器上运行，也可以配合 `screen` 后台启动：

```bash
cd samples/workspace
screen -dmS dotcraft bash -c "bash start.sh"
```

## 配置说明

DotCraft 会先读取全局配置 `~/.craft/config.json`，再叠加 `<workspace>/.craft/config.json` 中的 workspace 覆盖项。

对于这个示例：

- 密钥类信息尽量放在 `~/.craft/config.json`
- 用 `samples/workspace/config.template.json` 作为示例参考
- 只把需要的字段复制到你自己的 `<workspace>/.craft/config.json`
- 不要假设仓库分发时会带上 `.craft/config.json`，因为它通常包含私密数据并被忽略
- 不要因为这个 sample 本地存在 `.craft/config.json`，就直接覆盖已经在使用中的真实配置

建议使用方式：

1. 先查看 `config.template.json`
2. 把你需要的字段复制到自己的 `.craft/config.json`
3. 不要把机器相关路径、密钥、私有服务地址直接保留在共享 sample 中

模板里最常需要修改的字段：

| 字段 | 建议修改内容 |
|------|--------------|
| `DashBoard.Host` / `DashBoard.Port` | 如果本机绑定地址或端口不同，需要按实际环境调整 |
| `Tools.Sandbox.Enabled` | 只有实际运行 OpenSandbox 时才开启 |
| `Tools.Sandbox.Domain` | 改成你自己的 OpenSandbox 地址，并且不要和 Dashboard 端口冲突 |
| `Tools.Sandbox.Image` | 替换成你希望 sandbox 使用的容器镜像 |

## 推荐启动顺序

### 不使用 sandbox

1. 用 `dotcraft` 或 `bash start.sh` 启动 DotCraft
2. 如果 DotCraft 提示进入 Dashboard，先完成初始化配置
3. 保存配置后，重新正常启动 DotCraft

### 使用 sandbox

1. 先确认 Docker 和 OpenSandbox 前置条件满足
2. 启动对应平台的 sandbox 脚本
3. 从 `config.template.json` 中复制需要的 sandbox 字段到你自己的 `.craft/config.json`
4. 再启动 DotCraft

### 使用 QQ / NapCat

1. 先启动 NapCat
2. 再启动 DotCraft
3. 确认 QQ 外部渠道配置已启用，并且服务地址正确

## 可选：QQ / NapCat

这个示例本身不依赖 QQ 集成。只有在你要测试 QQ 外部渠道场景时，才需要这一步。

Linux 示例：

```bash
screen -dmS napcat bash -c "napcat"
```

另外仍然需要在 DotCraft 配置中单独启用 QQ 外部渠道。

## 可选：Tool Sandbox

sandbox 启动脚本是可选辅助工具。只有当你希望 Shell / File 工具在 OpenSandbox 容器中执行，而不是直接在宿主机执行时，才需要它们。

### 运行前检查

Windows PowerShell：

```powershell
docker info
python --version
opensandbox-server --help
```

Linux / macOS：

```bash
docker info
python3 --version
opensandbox-server --help
```

### 基础配置文件

sandbox 脚本默认会读取 `~/.sandbox.toml` 作为基础配置。如果你的文件放在别的位置，请先设置 `SANDBOX_CONFIG_PATH`。

可以用下面的命令生成默认基础配置：

```bash
opensandbox-server init-config ~/.sandbox.toml --example docker
```

### 端口注意事项

不要让 Dashboard 和 OpenSandbox 使用同一个端口。提供的 `config.template.json` 会特意把两者分开，避免冲突。

### 在 Windows 上启动

请在 PowerShell 中运行：

```powershell
.\start-sandbox.ps1
```

如果你的机器默认禁止执行本地脚本，需要先调整 PowerShell 执行策略，或者在允许本地脚本的 PowerShell 环境里运行。

### 在 Linux / macOS 上启动

```bash
bash ./start-sandbox.sh
```

## 如何确认运行正常

启动后，你应该能确认：

- DotCraft 能正常启动，没有配置错误
- Dashboard 能通过配置的 host 和 port 访问
- 开启 sandbox 时，辅助脚本会输出使用的配置路径和监听端口
- 如果启用了 QQ / NapCat，对应服务也处于在线状态

## 常见问题

### 缺少 `dotnet` 或构建工具

先安装 .NET 10 SDK，并执行 `dotnet --info` 确认环境正常，再运行 `start.sh`。

### `screen: command not found`

安装 `screen`，或者直接在前台运行 `bash start.sh`。

### 找不到 `opensandbox-server`

请用能把可执行文件放到 `PATH` 的方式安装 OpenSandbox Server，或者通过 `OPENSANDBOX_SERVER_EXE` 指向完整可执行文件路径。

### Docker 已安装，但脚本还是失败

先在同一个 shell 里运行 `docker info`。在 Linux 上，还要确认当前 shell 会话已经具备访问 Docker 的权限。

### 缺少 sandbox 基础配置

用下面的命令创建 `~/.sandbox.toml`：

```bash
opensandbox-server init-config ~/.sandbox.toml --example docker
```

或者把 `SANDBOX_CONFIG_PATH` 指向一个已存在的配置文件。

### sandbox 和 Dashboard 抢同一个端口

修改 `DashBoard.Port` 或 `Tools.Sandbox.Domain`，确保两者不冲突。

## 相关文档

- [项目 README](../../README_ZH.md)
- [配置参考](../../docs/developing/configuration.md)
- [可观测性](../../docs/features/observability.md)
- [QQ 渠道适配器](../../docs/developing/channels/qq.md)
