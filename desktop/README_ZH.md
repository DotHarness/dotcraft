# DotCraft Desktop

**中文 | [English](./README.md)**

基于 Electron 的 [DotCraft](../README_ZH.md) 桌面客户端：打开工作区、与 Agent 对话、查看文件变更，并在服务端启用时编排自动化任务。

---

## 前提条件

- **Node.js 18+** 与 **npm**
- **DotCraft AppServer**（`dotcraft` / `dotcraft.exe`）需在 `PATH` 中或通过应用设置指定 —— 见 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 或[从源码构建](../README_ZH.md#从源码构建)。

---

## 快速开始

```bash
cd desktop
npm install
npm run dev
```

窗口会使用当前工作区目录（或通过 `--workspace` 传入的路径）。

开发模式下，本地 Hub/AppServer 会从仓库构建产物 `../build/release/dotcraft(.exe)` 启动。修改后端代码后，请重新构建 DotCraft 并重启应用开发服务。

---

## npm 命令

| 命令 | 说明 |
|------|------|
| `npm run dev` | 开发模式（热重载） |
| `npm run build` | 生产构建 |
| `npm run preview` | 浏览器预览已构建的 renderer |
| `npm test` | 单元测试（Vitest） |
| `npm run e2e` | 冒烟 E2E |
| `npm run pack` / `npm run dist` | 打包 / 安装包（见下文） |

---

## 安装包

`npm run dist` 在 `desktop/dist/` 下生成各平台产物（Windows 为 NSIS 安装器，macOS DMG，Linux AppImage/deb）。

在 Windows 上，`DotCraft-Setup-<version>.exe` 是带向导的安装器，可选择仅当前用户或所有用户安装，也可修改安装目录。

```bash
npx electron-builder --win   # 或 --mac / --linux
```

打包后的应用会在启动时检查 GitHub Releases。发现新版本且存在当前平台安装包时，标题栏会显示高亮下载按钮，点击后可查看 release 信息和下载进度。

---

## 界面使用说明

**工作区** — 通过菜单或欢迎流程选择/切换文件夹；一个窗口对应一个工作区。

**对话** — 侧栏为会话列表；**新建会话**（`Ctrl+N`）。在输入框发送消息，主区域显示流式回复与工具调用。

**Markdown 图表** — 标记为 `mermaid` / `mmd` 的 fenced code block 会在对话、Markdown 文件预览、技能说明和自动化摘要中渲染为 Mermaid 图。渲染失败时，Desktop 会显示原始源码块。

**图片附件** — 粘贴/拖拽图片会落盘到 `.craft/attachments/images/`；用户消息会持久化附件路径与 MIME/文件名，因此切换会话或重启后仍可从磁盘恢复缩略图。

**文件浏览器** — 对话中的文件标签和本地文件链接可用内置 viewer 打开，包括明确引用的工作区外文件。外部文件按单个文件授权，不会放开整个目录。

**详情面板**（`Ctrl+Shift+B`）— **变更**：文件差异；在支持处可撤销/重新应用。**计划** / **终端** 等视服务端能力而定。

**Git** — 应用支持在「变更」流程中**将所选已修改文件暂存并填写说明后提交**（底层为 `git add` + `git commit`）。**不包含**完整 Git 客户端能力（无克隆、拉取、分支等界面）。

**自动化**（侧栏 **Automations**，仅当服务端声明支持该能力时可用）：

1. **新建任务** — 填写标题与说明；**Agent workspace** 选「项目目录」或「隔离工作区」；**工具策略** 选工作区内工具或全自动。提交后由服务端编排器执行。
2. 任务列表显示本地任务、可复用模板、调度状态、手动运行和删除操作。
3. 选中任务后打开活动侧栏，查看实时或历史 Agent 输出、线程绑定和完成摘要。

**快捷键** — `Ctrl+B` 侧栏、`Ctrl+Shift+B` 详情面板（具体以当前平台为准）。

---

## 配置

AppServer 二进制路径保存在应用用户数据目录下的 `settings.json`；首次启动会在 `PATH` 中查找 `dotcraft`。

```bash
DotCraft --app-server /path/to/dotcraft
DotCraft --workspace /path/to/project
```
