# 部署 DotCraft Stack

使用官方 Docker Compose stack，在 Linux 服务器上一起运行 DotCraft AppServer 和 Oratorio。两个服务共享同一个 workspace，因此 Oratorio 可以派发任务，DotCraft 也能打开对应的会话与 worktree。

## 初始化 stack

安装 DotCraft CLI，然后创建部署目录：

```bash
dotcraft stack init --dir /opt/dotcraft-stack --no-start
```

该命令会创建 Compose 文件、相互独立的 AppServer 与 Oratorio service token、可写的 Oratorio 配置，以及本地 `workspace`、`state` 和 `secrets` 目录。生成的 secret 只显示一次，并保存在 `/opt/dotcraft-stack/.env`。DotCraft Marketplace 配置和缓存数据保存在 `state/dotcraft` 下。

编辑 `.env`，设置模型 Provider：

```dotenv
DOTCRAFT_PROVIDER=openai
DOTCRAFT_MODEL=your-model-id
DOTCRAFT_API_KEY=your-api-key
```

> [!CAUTION]
> `.env` 包含 API key 和服务凭据。请保持私密，绝不要提交到仓库。

启动部署：

```bash
cd /opt/dotcraft-stack
docker compose up -d
dotcraft stack doctor --dir /opt/dotcraft-stack
```

## 管理插件

从 Desktop 连接后打开 **Plugins**。服务端镜像会把所有 bundled plugin 作为可安装的 catalog 条目公开，并默认启用官方 Plugin Marketplace。安装插件时，只会把选中的插件复制到共享 workspace 的 `workspace/.craft/plugins` 下。

用户添加的 Marketplace 配置和缓存快照保存在 `state/dotcraft` 下。替换或迁移部署时，请同时保留 `state/dotcraft` 和 `workspace/.craft`。如需使用其他 registry archive，请先在 `.env` 中设置 `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`，再重启 DotCraft 服务。

## 添加项目

把每个仓库克隆到生成的 workspace 目录下，然后把 source 身份绑定到准确的容器路径：

```bash
git clone https://github.com/acme/example.git /opt/dotcraft-stack/workspace/example
dotcraft stack add-project \
  --dir /opt/dotcraft-stack \
  --provider github \
  --project acme/example \
  --workspace /workspace/example
dotcraft stack restart --dir /opt/dotcraft-stack
```

GitLab 项目使用 `--provider gitlab`。每个可派发项目都需要显式的 `/workspace/...` 映射；运行时不会猜测 fallback workspace。

## 从 Desktop 连接

![Desktop 服务器设置](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

Desktop 通过系统 SSH 客户端连接，并把 AppServer、Oratorio 和 Dashboard 的凭据保留在主进程中。

1. 确认免交互 SSH 可用：`ssh -o BatchMode=yes user@host "echo ok"`。
2. 打开 **Settings -> Servers -> Add server**。
3. 填写 SSH target，并把 `/opt/dotcraft-stack` 设为 deployment folder。
4. 保留默认端口：AppServer `9100`、Oratorio `5087`、Dashboard `8080`。
5. 选择 **Open in Desktop**。

Desktop 会为 AppServer 和 Oratorio 分别打开 loopback SSH tunnel。Oratorio endpoint 与 bearer 不会进入 renderer。

## 管理 stack

```bash
dotcraft stack status --dir /opt/dotcraft-stack
dotcraft stack logs --dir /opt/dotcraft-stack --service oratorio
dotcraft stack restart --dir /opt/dotcraft-stack
dotcraft stack upgrade --dir /opt/dotcraft-stack
```

对会修改状态的命令添加 `--dry-run`，可以先查看操作效果，而不写入文件或启动进程。

## 启用 GitHub webhook 入口

通过可选 Caddy gateway 只公开 GitHub webhook endpoint：

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

Gateway 只接受 `POST /api/v1/sources/github/webhook`；AppServer、Dashboard 和 Oratorio 的其余 API 仍只绑定 loopback。请把命令显示的 secret 配置到 GitHub App 中。

关闭 gateway 不会删除 stack 状态或 secret：

```bash
dotcraft stack webhook disable --dir /opt/dotcraft-stack
```

## 相关文档

- [Oratorio](../oratorio)
- [配置 Oratorio](../oratorio/settings)
- [将 GitHub 接入 Oratorio](../oratorio/github)
- [安全与沙箱](./security)
- [可观测性](./observability)
