# 服务器部署

用官方 Docker Compose stack，在一台 Linux 服务器上同时运行 DotCraft AppServer 和 [Oratorio](../oratorio)。两个服务共用同一个工作区目录。Oratorio 派发的任务，你在 Desktop 里可以直接打开对应的会话和 worktree。

## 初始化 stack

安装 DotCraft CLI，然后创建部署目录：

```bash
dotcraft stack init --dir /opt/dotcraft-stack --no-start
```

命令会生成 Compose 文件、两个服务各自的凭据，以及 `workspace`、`state`、`secrets` 三个目录。生成的 secret 只显示这一次，之后都保存在 `/opt/dotcraft-stack/.env` 里。

编辑 `.env`，填入模型 Provider：

```dotenv
DOTCRAFT_PROVIDER=openai
DOTCRAFT_MODEL=your-model-id
DOTCRAFT_API_KEY=your-api-key
```

> [!CAUTION]
> `.env` 里有 API Key 和服务凭据。请保持私密，绝不要提交到仓库。

启动部署，再做一次自检：

```bash
cd /opt/dotcraft-stack
docker compose up -d
dotcraft stack doctor --dir /opt/dotcraft-stack
```

## 添加项目

把每个仓库克隆到 `workspace` 目录下，再把它绑定到容器里的准确路径：

```bash
git clone https://github.com/acme/example.git /opt/dotcraft-stack/workspace/example
dotcraft stack add-project \
  --dir /opt/dotcraft-stack \
  --provider github \
  --project acme/example \
  --workspace /workspace/example
dotcraft stack restart --dir /opt/dotcraft-stack
```

GitLab 项目用 `--provider gitlab`。每个要接受派发的项目都需要一条显式的 `/workspace/...` 映射，DotCraft 不会自行推断。

## 从 Desktop 连接

![Desktop 服务器设置](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

这些服务只监听服务器本机。Desktop 通过系统 SSH 客户端建立隧道访问，不需要把端口开放到公网。

先确认免交互 SSH 可用：

```bash
ssh -o BatchMode=yes user@host "echo ok"
```

然后打开 **设置 → 服务器 → 添加服务器**，填写 SSH 目标，把部署目录设为 `/opt/dotcraft-stack`，端口保持默认（AppServer `9100`、Oratorio `5087`、Dashboard `8080`）。最后选择 **在 Desktop 中打开**。

## 管理插件

连上服务器后打开 **插件**。服务器自带官方插件市场和全部随附插件，装上的插件只对这台服务器的共享工作区生效，不影响你本机的工作区。

替换或迁移这套部署时，把 `state/dotcraft` 和 `workspace/.craft` 一起带走，插件、市场来源和缓存都在这两个目录里。想改用自建的插件 registry，见[配置参考](../../developing/configuration#plugins-mcp-与-lsp)。

## 日常运维

```bash
dotcraft stack status --dir /opt/dotcraft-stack
dotcraft stack logs --dir /opt/dotcraft-stack --service oratorio
dotcraft stack restart --dir /opt/dotcraft-stack
dotcraft stack upgrade --dir /opt/dotcraft-stack
```

给会改动状态的命令加上 `--dry-run`，可以先看清它要做什么，再真正执行。

## 开放 GitHub webhook 入口

需要接收 GitHub 事件时，用可选的 Caddy gateway 只对外暴露 webhook 这一个入口：

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

Gateway 只接受 `POST /api/v1/sources/github/webhook`，其余接口仍然只监听本机。把命令输出的 secret 填进你的 GitHub App，接入步骤见[将 GitHub 接入 Oratorio](../oratorio/github)。

关闭 gateway 不会删除 stack 状态和 secret：

```bash
dotcraft stack webhook disable --dir /opt/dotcraft-stack
```

## 相关文档

- [Oratorio](../oratorio) — 在这套部署上派发任务、跟踪进度
- [安全与沙箱](./security) — 收紧服务器上 Agent 能碰的范围
- [可观测性](./observability) — 在 Dashboard 上回看运行状况和会话轨迹
