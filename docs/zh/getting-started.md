# 开始使用 DotCraft

安装 DotCraft Desktop，打开一个项目，完成初始化向导，然后发起第一次对话。

![安装 DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

## 1. 安装 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载适合当前系统的安装包。完成安装后打开 DotCraft。

## 2. 打开项目

选择 **打开工作区**，然后选择项目所在的文件夹。DotCraft 会打开工作区初始化向导。

按提示选择一个 Agent Profile。如果项目中已有 `AGENTS.md` 或 `CLAUDE.md`，可以在初始化时导入其中的项目指令。

## 3. 配置模型

在初始化向导中选择模型提供商和模型。根据提供商要求填写 API Key，或选择 **使用 ChatGPT 订阅**，并在初始化完成后登录。

检查所选设置，然后选择 **创建工作区**。

## 4. 发起第一次对话

在对话输入框中输入一个简单的请求并发送。例如：

```text
请阅读这个项目的 README，并告诉我应该怎样启动它。
```

DotCraft 返回回复后，这个工作区就可以继续处理其他任务了。

## 相关文档

- [Desktop](./features/entry-points/desktop)
- [Agent Profiles](./features/agent-system/agent-profiles)
- [插件与工具](./features/agent-system/plugins-tools)
