# 将 GitHub 接入 Oratorio

接入之后，GitHub 的 issue 和 pull request 会同步进 Oratorio 看板，Agent 的审阅意见、检查结果和实现分支也能写回 GitHub。连接靠一个 GitHub App 完成。

## 创建 GitHub App

在拥有这些仓库的用户或组织下创建一个 GitHub App，也可以复用已有的。只把它安装到 Oratorio 需要访问的仓库上。

按你打算启用的操作授予权限，用不到的就不给：

| 操作 | GitHub 权限 |
| --- | --- |
| **导入 issue 与 pull request** | Issues 与 pull requests：read |
| **读取文件与讨论** | Pull requests：read，Contents：read |
| **发布评论与审阅** | Issues 与 pull requests：write |
| **发布审阅检查** | Checks：write |
| **交付 pull request** | Contents 与 pull requests：write |

再为这个 App 生成一个 private key。接下来配置 DotCraft 时要用到 App ID 和这把密钥。

## 配置 GitHub 连接

1. 打开 Oratorio Board，选择 **Oratorio settings**，然后选择 **GitHub**。
2. GitHub.com 保持默认 endpoint。GitHub Enterprise 填写自己的 API endpoint。
3. 填入 App ID，再添加 private key 或它的路径。
4. 为每个 GitHub owner 添加一个 installation profile。保存项目路由后，Oratorio 可以自动检测 Installation ID，你也可以手动填。
5. 回到 Oratorio 设置，逐个添加仓库，并为每个仓库选择对应的 DotCraft workspace。
6. 需要 Oratorio 发布评论、审阅、检查、分支或 pull request 时，才启用 **来源写入**。
7. 选择 **立即同步**，确认仓库已经可读。

私有仓库不需要额外设置。Oratorio 用 App installation 凭据把审阅目标取回映射的 checkout，这个 checkout 本身不必保存 Git 凭据。

## 启用 Webhook

同步不依赖 Webhook，但 GitHub 上的评论命令需要它。本地 Desktop 通常收不到 GitHub 云端的 Webhook，手动同步和定时同步照常可用。

如果你运行的是远程 [DotCraft Stack](../self-hosted/server-deployment)，只公开受限的 Webhook endpoint：

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

把命令输出的 endpoint 填成 GitHub App 的 Webhook URL，把生成的 secret 填进 App，保持 SSL verification 开启，然后订阅工作流用到的 issue comment、issue、pull request、review 和 review comment 事件。

配置好之后，有仓库协作权限的人可以在已接入且仍然开放的 pull request 下单独发一条评论来请求审阅：

```text
@dotcraft-ai review
```

想让这次审阅盯住某个方面，就在命令后面接一句说明，例如 `@dotcraft-ai review for security regressions`。

## 相关文档

- [使用 Oratorio 工作流](./workflow) — 同步进来的任务在 Board 上怎么一步步推进
- [配置 Oratorio](./settings) — 调整审阅自动化、Worktree 和交付方式
