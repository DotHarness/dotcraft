# 配置 Harness 路径

应用持有配置来源与存储位置。Harness 使用一份最终生效的 `AppConfig`，并把三个路径选项解析成一个经过验证的 `DotCraftPaths` 上下文。

## 在 Harness 外部准备配置

`AddDotCraftHarness` 不读取配置文件、环境变量或用户目录。应用在注册前加载并合并这些配置来源。

```csharp
AppConfig appConfig = configurationStore.Load();

builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
});
```

这个边界让桌面应用、服务和测试使用各自的配置系统，而 Runtime 行为保持一致。

## 选择路径根目录

| 选项 | 是否必填 | 默认值 | 用途 |
| --- | --- | --- | --- |
| `WorkspacePath` | 是 | 无 | 会话与工具使用的应用 workspace。 |
| `DataPath` | 否 | `.craft` | Workspace 内的会话、Trace、工具结果与 Runtime 状态。 |
| `UserDataPath` | 否 | `null` | 用户级 Skill、Command、Hook、插件、插件市场与 Provider 身份验证。 |

设置一个直属子目录名即可使用不同的 workspace 数据目录：

```csharp
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
    options.DataPath = ".agents";
});
```

`DataPath` 也接受这个直属子目录的绝对路径。Harness 会拒绝嵌套相对路径、越过 workspace 的路径，以及指向 workspace 外部的现有文件系统链接。

> [!TIP]
> 把选定的数据目录当作 Harness 持有的状态目录。在版本控制操作中排除它，也不要在其中存放应用文档。

## 显式启用用户级状态

`UserDataPath` 默认禁用。只有当应用确实要持有用户级发现与持久化状态时才设置它。

```csharp
builder.Services.AddDotCraftHarness(appConfig, options =>
{
    options.WorkspacePath = workspacePath;
    options.DataPath = ".agents";
    options.UserDataPath = applicationDataPath;
});
```

当 `UserDataPath` 为 `null` 时，用户级发现返回空结果。必须持久化用户状态的操作会抛出明确错误，而不会隐式选择某个用户目录。

## 在应用服务中解析路径

Harness 会注册一个不可变的 `DotCraftPaths`。应用组件通过依赖注入获取它，不要在各处重新实现路径规则。

```csharp
using DotCraft.Workspaces;

public sealed class ExportService(DotCraftPaths paths)
{
    public string GetSessionExportPath(string fileName) =>
        paths.Data.Resolve("exports", fileName);

    public string? GetOptionalUserTemplatePath(string fileName) =>
        paths.UserData.ResolveOrNull("templates", fileName);
}
```

如果某项操作缺少用户级持久化就无法继续，请使用 `Require`：

```csharp
var authFile = paths.UserData
    .Require("Provider authentication")
    .Resolve("auth.json");
```

`Resolve`、`ResolveOrNull` 与 `Require` 把路径可用性和边界检查集中在一处。

## 测试隔离的 Host

测试显式提供临时 workspace 与用户数据目录。验证不访问用户目录的嵌入式运行方式时，省略 `UserDataPath`。

```csharp
builder.Services.AddDotCraftHarness(testConfig, options =>
{
    options.WorkspacePath = temporaryWorkspace;
    options.DataPath = ".agents";
    options.UserDataPath = null;
});
```

## 相关文档

- [托管与生命周期](./hosting-lifecycle)——这些路径选项在 Host 生命周期中何时被校验与使用。
- [模型 Provider](./model-providers)——`AppConfig` 中的 Provider 与模型字段，和这里的路径选项共同构成注册参数。
