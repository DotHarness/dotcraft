# 将 GitHub 接入 Oratorio

使用 GitHub App 同步 issue 与 pull request、发布审阅反馈并交付实现工作。

## 创建 GitHub App

在仓库所属的用户或组织下创建或复用 GitHub App。只把它安装到 Oratorio 需要访问的仓库。

根据启用的操作授予最小权限：

| 操作 | GitHub 权限 |
| --- | --- |
| **导入 issue 与 pull request** | Issues 与 pull requests：read |
| **读取文件与讨论** | Pull requests：read；Contents：read |
| **发布评论与审阅** | Issues 与 pull requests：write |
| **发布审阅检查** | Checks：write |
| **交付 pull request** | Contents 与 pull requests：write |

为 App 生成 private key。配置 DotCraft 时需要 App ID 与该密钥。

## 配置 Provider

1. 打开 Oratorio Board，选择 **Oratorio settings**，然后选择 **GitHub**。
2. GitHub.com 保持默认 endpoint；GitHub Enterprise 则填写对应的 API endpoint。
3. 输入 App ID，并添加 private key 或 private-key path。
4. 为每个 GitHub owner 添加 installation profile。保存项目路由后可让 Oratorio 检测 Installation ID，也可以手动填写。
5. 返回 Oratorio 设置，添加各仓库并选择对应的 DotCraft workspace。
6. 只有需要 Oratorio 发布评论、审阅、检查、分支或 pull request 时，才启用 **Source writes**。
7. 选择 **Sync now**，确认仓库具备读取能力。

## 启用 Webhook

同步本身不强制使用 Webhook，但 GitHub 评论命令需要它。本地 Desktop 通常无法直接接收 GitHub 云端 Webhook；手动同步与定时同步不受影响。

对于远程 DotCraft Stack，只公开受限的 Webhook endpoint：

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

把命令输出的 endpoint 设置为 GitHub App 的 Webhook URL，把生成的 secret 填入 App，保持 SSL verification 开启，并订阅工作流需要的 issue comment、issue、pull request、review 与 review comment 事件。

具备仓库协作权限的用户可以在已配置且仍开放的 pull request 下发送以下独立评论来请求审阅：

```text
@dotcraft-ai review
```

需要一次性审阅重点时，可在命令后追加说明，例如 `@dotcraft-ai review for security regressions`。

## 相关文档

- [Oratorio](../oratorio)
- [使用 Oratorio 工作流](./workflow)
- [配置 Oratorio](./settings)
- [部署 DotCraft Stack](../self-hosted/server-deployment)

