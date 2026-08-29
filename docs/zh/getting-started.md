# 快速开始

安装 DotCraft Desktop、打开项目、走完初始化向导，然后发起第一次对话。四步做完，DotCraft 就能在你的项目里开始工作。

![安装 DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

## 1. 安装 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载适合当前系统的安装包，装好后打开 DotCraft。

## 2. 打开项目

选择 **打开工作区**，选中项目所在的文件夹。DotCraft 会打开工作区初始化向导。

如果本机已有 Claude Code 的配置，向导会多出一步，可以直接导入。不需要就跳过。

## 3. 配置模型

在向导中选择模型提供商和模型。按提供商要求填写 API Key，或者选择 **使用 ChatGPT 订阅**，创建完成后再登录。

核对最后一页的摘要，选择 **创建工作区**。

## 4. 发起第一次对话

在对话输入框里输入一个简单的请求并发送。例如：

```text
请阅读这个项目的 README，并告诉我应该怎样启动它。
```

DotCraft 回复之后，这个工作区就可以接着处理真正的任务了。

## 相关文档

- [Desktop](./features/entry-points/desktop) — 认识主界面：会话、审批和工作区切换
- [插件与工具](./features/agent-system/plugins-tools) — 给 Agent 接上完成任务所需的能力
- [长期记忆与梦境](./features/agent-system/memory) — 让下一次会话记得这次的结论
