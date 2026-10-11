# Desktop

Desktop 把工作区、会话、diff、计划、模型配置和运行状态放进同一个窗口，用图形界面驱动 Agent，不用记命令。第一次接触 DotCraft，从这里开始最省事。

下载安装、选工作区、配模型这几步见[快速开始](./)。本页讲装好之后 Desktop 能帮你做什么。

## 看清 Agent 做了什么

Agent 的每一步都摊在窗口里。打开一个会话就能逐条回看它读了什么、改了什么，文件改动以 diff 呈现，写文件和执行命令前会停下来等你批准。[自动化与目标](../guide/automations)跑出的结果同样在这里等你过目。

想追得更细，就打开 Trace 或 Dashboard，看这次任务调用了哪些工具、花了多少 token。

回复里标了 `mermaid` 的代码块会直接渲染成图，渲染不出来时回落显示源码。粘进对话的图片重启后仍然保留，重新打开会话，缩略图还在。

## 在回复里直接操作

当计算器、方案对比或小测验比文字更能回答问题时，DotCraft 会在回复里给出一个[交互式回答](../guide/interactive-answers)。想看图表、模拟演示或几版设计稿，DotCraft 会画出[交互式可视化](../guide/visualizations)，不用离开对话就能探索和修改。

## 一个窗口里管多个项目

换一个工作区就换一个项目，配置、技能、记忆和自动化任务都跟着项目走，互不干扰。项目的记忆开关、梦境和删除记忆在**设置 → 个性化**里，它们各自管什么见[记忆与梦境](../guide/memory)。

## 不建项目也能聊

问题不属于任何项目时，选**对话**而不是某个项目。每个新对话在 `Documents/DotCraft/Chats` 下有自己的文件夹，以日期和第一条消息的开头几个英文单词命名，例如 `2026-10-11/write-a-release-note-for-v2`，消息里没有英文字母或数字时叫 `new-chat`。Agent 把草稿和临时文件放在对话的 `work` 文件夹，给你的成果保存在 `outputs`，不同对话的文件互不混杂。文件面板、终端和**在…中打开**都作用于这个文件夹。归档、重命名或删除对话时，它的文件夹原样保留。

想把对话文件放到别处，在**设置 → 常规 → 对话文件夹**中选择**更改**。之后新建的对话使用新位置，**重置**则改回 `Documents/DotCraft/Chats`。

## 配好模型

在**设置 → 模型提供商**里添加提供商、填凭据、挑模型。凭据和 endpoint 写进个人配置 `~/.craft/config.json`，不进工作区，把工作区配置分享给同事也不会带上密钥。保存前用**测试**确认凭据和模型列表可达。提供商不支持列出模型时，手填模型名照样能存。Desktop 目前支持 OpenAI 和 Anthropic。

这里选的模型只决定新会话的默认值。已有会话保留自己创建时的设置，也可以在它自己的输入框里单独换。[Subagents](../guide/subagents) 默认沿用主 Agent 的模型，需要时可以给它指定一个更快或更便宜的。

## 用自己的应用打开文件

在会话 **⋯** 菜单或侧边栏会话右键菜单的**在…中打开**里，可以用 VS Code、JetBrains IDE、Windows Terminal、文件资源管理器或其他已安装的应用打开本地项目。文件查看器的**打开**按钮用同样的方式打开当前文件。回复里的文件引用在 VS Code、Cursor、Zed、Sublime Text 和 JetBrains IDE 中会直接跳到对应行。

从**在…中打开**或文件查看器里选过的应用，Desktop 会记下来，作为这个项目以及其他地方的默认应用。在文件引用的菜单里选应用只打开这一次，不改默认值。所有项目的默认应用在**设置 → 常规 → 默认文件打开方式**里设置。

### 添加自己的应用

想用 Desktop 没有列出的应用或自己的脚本打开文件，就把它加到 `~/.craft/config.json` 的 `Desktop.CustomFileHandlers` 里。每一项以 id 为键，id 由字母、数字、`.`、`_` 和 `-` 组成，菜单中排在内置应用之后。

```json
{
  "Desktop": {
    "CustomFileHandlers": {
      "notepad-plus": {
        "Label": "Notepad++",
        "Command": "C:\\Program Files\\Notepad++\\notepad++.exe"
      },
      "review": {
        "Label": "Review tool",
        "Command": "review-tool",
        "Args": ["--open"],
        "Input": "jsonStdin"
      }
    }
  }
}
```

| 字段 | 说明 | 默认值 |
|---|---|---|
| `Label` | 菜单中显示的名称 | 必填 |
| `Command` | 程序的绝对路径，或能在 `PATH` 中找到的命令名 | 必填 |
| `Args` | 放在路径或 JSON 之前的参数 | `[]` |
| `Input` | `path` 把路径作为最后一个参数。`jsonArgument` 追加一个 JSON 参数，`jsonStdin` 把同样的 JSON 写到标准输入 | `path` |
| `Icon` | 图片的绝对路径，或 `data:image/…` URL | 通用应用图标 |

JSON 的内容是 `{ "target": "custom:review", "path": "<绝对路径>", "location": { "line": 12, "column": 1 } }`，没有对应行时 `location` 为 `null`。Desktop 每次打开菜单都会重新读取这份列表，保存后无需重启就能看到。缺少字段、id 不合法或命令不存在的条目不会出现。

## 运行项目命令

把项目里反复输入的命令存成操作，例如启动开发服务器、跑测试或在 Unity 中打开项目，之后在对话里直接运行。它们放在本地会话旁**摘要**的**环境信息**一栏：带操作图标的按钮运行最近一次运行过的操作，**操作**菜单列出全部操作，**打开终端**在项目文件夹中打开一个终端。每个操作在以它命名的终端标签页中运行，再次运行会重启这个标签页。

添加第一个操作时，选择**设置本地环境**，填好名称、图标和命令后保存。Desktop 会在项目中写入 `.craft/environments/environment.json` 并运行这个操作。之后从**操作 → 添加操作**继续添加。

### 在文件中编辑操作

`.craft/environments/` 中的每个 JSON 文件是一个环境，也就是一组有名字的操作。Desktop 还会读取各级上层文件夹直到仓库根目录的 `.craft/environments/`，monorepo 里的项目因此可以共用操作。修改文件后无需重启就能看到。

```json
{
  "version": 1,
  "name": "Web",
  "actions": [
    { "name": "Dev server", "icon": "run", "command": "npm run dev" },
    { "name": "Test", "icon": "test", "command": "npm test" },
    { "name": "Reveal", "icon": "tool", "command": "explorer .", "platform": "win32" }
  ]
}
```

| 字段 | 说明 |
|---|---|
| `name` | 显示在菜单和终端标签页上 |
| `icon` | `run`、`debug`、`test` 或 `tool`。其他值按 `tool` 显示 |
| `command` | 在终端中输入的命令，运行目录是包含该环境 `.craft` 文件夹的那个文件夹 |
| `platform` | `win32`、`darwin` 或 `linux`。设置后该操作只在对应系统上出现 |

默认使用项目自己的 `environment.json`。有多个环境时，从**操作 → 更换环境**中选择，Desktop 会为这个项目记住选择。无法读取的文件会在环境信息一栏显示问题，点击即可打开该文件。

## 看 token 花在哪

**设置 → 个人资料**里有一张 Token 活动图，像 GitHub 贡献图那样按天铺开当前工作区所有会话的用量，旁边是累计 Token、单日峰值和连续使用天数。需要先为这个工作区开启 tracing，这张图才有数据。

## 养一只桌宠

输入框里的伙伴就是你的桌宠。它会在你工作时找东西穿：Agent 干了足够多的活之后，隔一阵子就有一件新物品进背包，通知会告诉你来的是什么。在通知上点**穿上**，或者打开**设置 → 桌宠**换颜色、从背包里给它装扮，也可以拿十件同稀有度的物品合成一件高一档的。在那里关掉桌宠自定义会换回经典的蓝色伙伴并暂停掉落，背包里的东西都会留着。

## 连本地，也能连远程

默认情况下 Desktop 在本机启动或接管当前工作区的 AppServer，其他入口共用同一个进程，不用你操心。在这里开的会话也不会被锁在 Desktop 里，换个[入口](./entry-points)可以接着聊。

要连服务器上的 DotCraft，在**设置 → 连接 → 工作区**里填远程 AppServer 地址。保存前 Desktop 会先试连一次，连不上就不保存，下次启动也不会卡在一个坏地址上。整套服务端部署见[服务器部署](../self-hosting/server-deployment)。

远程工具调用让 Agent 在另一台电脑上工作，对话仍留在这里。在**设置 → 连接 → 卫星**中完成配对，再到输入框的**执行位置**中选中它。连接步骤和屏幕查看方式见 [DotCraft 卫星](../guide/satellite)。

想在手机上跟进和回复聊天，在**设置 → 连接 → 手机**中配对手机。下载和设置步骤见[手机 App](./mobile)。

## 跟上新版本

启动时 DotCraft 会检查 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 有没有更新的版本。当前平台有安装包时，标题栏会出现下载按钮，点开可以看版本说明、下载并看到进度，下完 DotCraft 退出并打开安装包。

升级后第一次进入工作区会弹出一次**最新功能**，用动图介绍这个版本新增的能力。之后从**帮助 → 最新功能**或侧边栏底部的版本号可以随时再打开。

## 相关文档

- [入口总览](./entry-points) — 什么时候改用 CLI、编辑器或群聊入口
- [应用连接](../guide/connected-apps) — 让会话直接用上你已经在用的产品和服务
- [可观测性](../self-hosting/observability) — 打开 Dashboard 回看会话轨迹、diff 和 token 用量
