# 快速开始

安装 DotCraft Desktop、打开项目、走完初始化向导，然后发起第一次对话。四步做完，DotCraft 就能在你的项目里开始工作。

![安装 DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/setup.webp)

## 1. 安装 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载适合当前系统的安装包，装好后打开 DotCraft。

Releases 页面也提供 Android [手机 App](./features/entry-points/mobile)。走完本指南后配对，就能在手机上跟进聊天。

## 2. 打开项目

选择 **打开工作区**，选中项目所在的文件夹。DotCraft 会打开工作区初始化向导。

如果文件夹附近已有 Claude Code 的项目说明，向导会多出一步，可以直接导入。不需要就选择 **不带说明开始**。

## 3. 配置模型

在 **模型接入** 一步，选择已经保存过的提供商，或者连接新的：**使用 ChatGPT 登录**、OpenAI 或 Anthropic 的 API 密钥，或者其他兼容 OpenAI 的服务。然后选择模型。之后想换模型，可以回到设置里改，也可以在对话里让 `$dotcraft-guide` 替你切换。

左侧的大纲会列出你目前的选择，点任意已完成的步骤就能回去修改。确认无误后，选择 **创建工作区**。

## 4. 发起第一次对话

在对话输入框里输入一个简单的请求并发送。例如：

```text
请阅读这个项目的 README，并告诉我应该怎样启动它。
```

DotCraft 回复之后，这个工作区就可以接着处理真正的任务了。

## 相关文档

- [Desktop](./features/entry-points/desktop) — 认识主界面：会话、审批和工作区切换
- [插件与工具](./features/agent-system/plugins-tools) — 给 Agent 接上完成任务所需的能力
- [记忆与梦境](./features/agent-system/memory) — 让下一次会话记得这次的结论
