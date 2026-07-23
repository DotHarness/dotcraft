# 插件市场

插件市场会添加来自你所选来源的插件目录，方便你直接在 Plugins 页面中浏览和安装。

![一个插件市场可以向不同工作区提供可安装插件](/plugin-marketplace-flow.svg)

市场只需添加到 DotCraft 一次，其中的插件则按工作区分别安装。这样，每个项目只保留自己需要的能力。

## 添加插件市场

1. 在 DotCraft Desktop 中打开 **Plugins / 插件**。
2. 打开 **Create / 创建** 旁的菜单，然后选择 **Add marketplace / 添加市场**。
3. 输入市场来源。
4. 点击 **Add marketplace / 添加市场**。

DotCraft 会读取目录，并把其中的插件显示在 Plugins 页面。

### 选择来源

| 来源 | 示例 | 适用场景 |
|---|---|---|
| **GitHub 仓库** | `owner/repo` | 添加 GitHub 仓库的最简方式 |
| **Git URL** | `https://host/team/plugins.git` | 公共、私有或自托管 Git 仓库 |
| **本地文件夹** | 点击 **Browse / 浏览** | 正在这台电脑上开发或维护的市场 |

只有 Desktop 使用本地工作区时，才会显示 **Browse / 浏览**。

使用 Git 来源时，让 **Git ref / Git 引用** 保持为空即可跟随默认分支。只有需要固定版本时，才填写分支、标签或 commit。

只有当仓库维护者要求仅下载特定目录时，才使用 **Sparse paths / 稀疏路径**。每行填写一个仓库内路径。大多数市场不需要这项设置。

## 安装插件

1. 打开 **Plugins / 插件**。
2. 搜索插件，或在发布者筛选器中选择 **Marketplaces / 插件市场**。
3. 打开插件详情。
4. 点击 **Install / 安装**。
5. 检查确认信息，然后点击 **Add to DotCraft / 添加到 DotCraft**。
6. 按安装对话框提示完成所需的 App 配置。
7. 点击 **Try in chat / 在对话中试用**，或新建对话并描述你要完成的任务。

插件只会安装到当前工作区。通过 **Manage / 管理** 可以启用或禁用已安装插件，而不必卸载。

## 刷新插件市场

当发布者新增或更新插件时，刷新对应市场：

1. 在发布者筛选器中选择 **Marketplaces / 插件市场**。
2. 找到市场标题。
3. 打开 **Marketplace actions / 市场操作**。
4. 选择 **Refresh / 刷新**。

刷新会更新插件市场目录。

## 移除插件市场

打开市场旁的 **Marketplace actions / 市场操作**，然后选择 **Remove / 移除**。

移除后，DotCraft 不再显示这个市场的目录。已经安装到工作区的插件会继续保留，直到你主动卸载。

> [!CAUTION]
> 只添加你信任的市场来源。安装插件前，请检查发布者、权限和相关链接。

## 相关文档

- [插件与工具](./plugins-tools)
- [Connected Apps](./connected-apps)
- [安全与沙箱](../self-hosted/security)
- [插件市场](../../developing/integrations/plugin-market)
