# 调试 DotCraft Desktop

Chrome DevTools Protocol（CDP）让 Agent 可以检查和操作 DotCraft Desktop renderer。Desktop 以 CDP 模式启动后，调试技能会管理附加的会话，并在调查结束后断开连接，不影响应用继续运行。

![开发者启动启用了 CDP 的 DotCraft Desktop，Agent 随后加载 Desktop 调试技能，通过一段可复用的 Playwright 会话操作正在运行的应用](/desktop-debugging-flow.svg)

## 安装调试工作流

`$dotcraft-desktop-debugging` 技能随官方 `dotcraft` 插件提供。开始调试任务前，先把这个插件安装到当前工作区：

1. 在 DotCraft Desktop 中打开**插件**页面。
2. 找到由 DotHarness 发布的 **DotCraft**，点击**安装**。
3. 检查确认信息后，点击**添加到 DotCraft**。

完整的插件安装流程见[插件与工具](../../features/agent-system/plugins-tools)。

## 启动启用了 CDP 的 Desktop

### 开发环境

在 `desktop/` 目录运行：

```powershell
npm run dev:debug
```

需要打开指定工作区时，把 workspace 参数继续传给 Electron：

```powershell
npm run dev:debug -- -- --workspace <workspace-path>
```

### 正式环境

调试 Windows 打包版本时，使用调试端口启动可执行文件：

```powershell
DotCraft.exe --remote-debugging-port=9222
```

这两种方式都会开放固定的 loopback endpoint `http://127.0.0.1:9222`。CDP 会在进程启动时开启。

## 确认 CDP 已启用

![DotCraft Desktop 首页右下角显示 CDP 状态标识及启用提示](https://github.com/DotHarness/resources/raw/master/dotcraft/developing/desktop-cdp-debugging.png)

<p class="caption">右下角的状态标识表示当前 Desktop 进程可以接受 CDP 连接。</p>

悬停或聚焦右下角的蓝色状态标识，它会提示 **CDP 调试已启用。**

## 把会话交给 Agent

说明需要检查的 Desktop 行为，并明确指定调试技能：

```text
$dotcraft-desktop-debugging 检查当前 Desktop 窗口并为这个问题截取证据。
```

Agent 会连接现有的 loopback endpoint，选中 DotCraft 窗口，等待工作台恢复完成，并在整个任务中复用同一段调试会话。连接命令和就绪检查由技能维护，本页不重复这些细节。
