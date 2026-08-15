# 将 GitLab 接入 Oratorio

接入 GitLab 项目后，可以同步 issue 与 merge request、发布反馈并交付实现工作。

## 创建项目 Token

优先使用 Project Access Token，把权限限制在单个项目。根据启用的操作只授予必要权限：

| 操作 | GitLab 权限 |
| --- | --- |
| **导入 issue 与 merge request** | Read API access |
| **读取仓库内容** | Repository read access |
| **发布 note 与审阅状态** | API access |
| **交付 merge request** | Repository write 与 API access |

每个项目使用自己的 profile 与 Token。

## 配置 Provider

1. 打开 Oratorio Board，选择 **Oratorio settings**，然后选择 **GitLab**。
2. 启用 source read。GitLab.com 保持默认 endpoint。自托管 GitLab 则填写实例根地址。
3. 添加 project profile，填写实例与完整的 `group/project` 路径。支持 subgroup。
4. 把项目 Token 添加到该 profile。
5. 返回 Oratorio 设置，添加项目并选择对应的 DotCraft workspace。
6. 只有需要 Oratorio 发布 note、状态、分支或 merge request 时，才启用 **Source writes**。
7. 选择 **Sync now**，确认项目具备读取能力。

## 启用 Webhook

Webhook 不是必需项，但可以让来源变更更快显示。把项目 Webhook URL 设置为：

```text
https://your-oratorio-host/api/v1/sources/gitlab/webhook
```

在 GitLab project profile 中保存相同的 webhook secret 或 signing token，然后启用 issue、merge request 和 note 事件。除非部署提供了经过认证的 ingress 边界，否则应保持该 endpoint 私有。

本地 Desktop 通常无法直接接收 GitLab 云端 Webhook。没有可访问 endpoint 时，使用手动同步或定时同步。

## 相关文档

- [Oratorio](../oratorio)
- [使用 Oratorio 工作流](./workflow)
- [配置 Oratorio](./settings)
- [部署 DotCraft Stack](../self-hosted/server-deployment)

