# 开发 .NET 插件

.NET 插件运行在 **DotCraft 进程内部**。MCP 服务器隔着边界与 DotCraft 通信，而 .NET 插件是从内部参与内核组装：它在具名贡献点上贡献工具、提示词分节、中间件和生命周期观察者，并可以解析宿主服务，用 DotCraft 自身的部件搭建这些贡献。

本页面向插件开发者。插件的用户视角见[插件与工具](../../features/agent-system/plugins-tools)。所有插件共用的清单字段见[插件市场](./plugin-market)。

> [!CAUTION]
> .NET 插件把完全受信任的代码加载进 DotCraft 进程，并获得该进程的文件系统、网络、凭据、原生互操作与操作系统权限。这里没有托管沙箱，也没有权限模型。安全性取决于你选择构建或信任哪些代码，而不是运行时边界。需要真正的信任边界时请使用 MCP。

![.NET 插件开发与运行时拓扑](/dotnet-plugin-topology.svg)

可运行的 [DotNetPluginSample](https://github.com/DotHarness/dotcraft/tree/main/sdk/dotnet/samples/DotNetPluginSample) 包含两个 bundle、覆盖全部公共 .NET 贡献点、通过宿主的预检与运行时校验构建产物，并为其中一个工具提供 Desktop 呈现。

## 使用 DotCraft 创建

让 `$plugin-creator` 在当前工作区创建 .NET 插件。它会生成持久化的源码项目与标准开发 bundle：

```text
.craft/plugin-projects/<plugin-id>/
├── src/
└── plugin/
    ├── .craft-plugin/plugin.json
    └── lib/
```

项目没有 `.csproj`，也不会还原 NuGet 包。Agent 编辑 `src/**/*.cs` 和 manifest，通过 `DotNetPlugin.Inspect` 查询准确的 API 签名与文档，再调用 `DotNetPlugin.Build`。构建过程使用当前宿主随附的公共插件 API 与 BCL 完成编译，执行 metadata preflight，原子发布 bundle 并将其激活。整个过程不需要外部 .NET SDK，也不访问网络。

构建成功后，精确的插件 id 与指纹只在当前宿主进程中获得执行资格，`dotnet-plugin-trust.json` 不受影响。DotCraft 重启后需要重新构建。对同一项目中已激活的相同指纹重复构建不会产生变更。

`DotNetPlugin.Build` 在编译前运行宿主固定的工具生成器，因此源码项目也支持 `[Tool]`、`[GeneratedTool]` 与 `[ToolDeclaration]`。生成文件只存在于内存中。源码项目不能提供 analyzer 或 generator。生成错误带有 `phase: generate`，不会替换此前已发布的 bundle 或活动 generation。

自定义 Harness 宿主需要支持源码开发时，在宿主项目中设置 `<PreserveCompilationContext>true</PreserveCompilationContext>`。Harness 会把必需的 Core 与 Agents XML 文档复制到构建和发布输出。发布单文件宿主时，还需设置 `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>`，让编译器能够读取提取后的程序集、文档与引用包。

执行构建的 Turn 继续使用它已冻结的工具快照。新的插件工具从下一个 Turn 开始可用，构建本身不会调用它们。源码改动只在 Agent 调用 `DotNetPlugin.Build` 时生效。

## 准备预构建 bundle

.NET 插件就是一个带 `dotnet` 贡献的普通 DotCraft 插件目录。对于外部构建的插件，请在安装前放入全部托管与原生依赖。发现、安装和激活过程不会还原 NuGet 包、运行 MSBuild 或编译源码。

```text
acme.review-core/
├── .craft-plugin/
│   └── plugin.json
└── lib/
    ├── Acme.ReviewCore.Plugin.dll
    ├── Acme.ReviewCore.Plugin.deps.json
    ├── Acme.ReviewCore.Api.dll
    └── 私有依赖
```

入口程序集的 `.deps.json` 必须与它放在一起——加载上下文正是靠它解析 bundle 自带的一切。

```json
{
  "schemaVersion": 1,
  "id": "acme.review-core",
  "version": "1.0.0",
  "displayName": "Review Core",
  "description": "In-process review services.",
  "capabilities": ["dotnet"],
  "dotnet": {
    "minHostVersion": "0.5.0",
    "entryAssembly": "./lib/Acme.ReviewCore.Plugin.dll",
    "entryType": "Acme.ReviewCore.Plugin",
    "exportedApiAssemblies": ["./lib/Acme.ReviewCore.Api.dll"]
  },
  "dependencies": { "acme.review-base": "1.0.0" }
}
```

| 字段 | 必填 | 含义 |
|---|---|---|
| **`minHostVersion`** | 是 | 插件可运行的最低 DotCraft 宿主版本，格式为 `MAJOR.MINOR.PATCH`。 |
| **`entryAssembly`** | 是 | 承载入口类型的托管程序集。 |
| **`entryType`** | 是 | 入口类型的完整 CLR 名称：public、具体、非泛型，且有 public 无参构造函数。 |
| **`exportedApiAssemblies`** | 否 | 供已声明依赖方绑定的契约程序集。入口程序集不能被导出。 |
| **`dependencies`** | 否 | Provider 的最低兼容版本。只能与 `dotnet` 同时出现。 |

插件 id 以 ASCII 字母或数字开头，后续还可包含 `.`、`_`、`:` 或 `-`。只要存在 `dotnet`，`version` 就是必填。所有路径以 `./` 开头、不得越出插件根目录，并且必须指向构建好的 bundle 中已存在的文件。

### 引用 DotCraft.Core，但不要随包分发

插件 API 就是 `DotCraft.Core` 本身，外加它传递引用的 `DotCraft.Agents` 和 Microsoft.Extensions.AI。没有单独的 SDK 程序集。

```xml
<ProjectReference Include="path/to/src/DotCraft.Core/DotCraft.Core.csproj" Private="false" />
```

在 DotCraft checkout 之外建立独立项目时，引用已发布的 `DotCraft.Harness` 包即可获得这些 API 与工具 analyzer。使用 checkout 项目引用构建时，还需引用 `DotCraft.Generators.csproj`，并设置 `OutputItemType="Analyzer"`、`ReferenceOutputAssembly="false"`。单独引用 Core 不会运行生成器。请面向同一宿主版本构建，bundle 中只保留插件及其私有依赖闭包。

加载上下文按**简单名称**把每个 DotCraft 程序集及其包闭包解析到宿主中已加载的副本，忽略 bundle 自带的版本。类型标识的单一性正来自于此：中间件贡献改写的 `ChatMessage`，与内核派发时使用的是同一个类型——哪怕 bundle 自带了 `Microsoft.Extensions.AI.Abstractions.dll`。随包分发这些程序集只会让 bundle 变大。其余内容都从 bundle 的 `.deps.json` 与相邻探测解析，并限制在 bundle 目录内。

### 绑定到宿主版本

由于内核的整个公共面就是插件 API，兼容性绑定的是宿主版本，而不是一份只增不改的契约。

- `minHostVersion` 是硬性门槛。低于它的宿主会让插件停在 `blocked`，并报 `PluginHostVersionUnsatisfied`，插件代码一行都不会运行。
- 更新的宿主会尽力加载插件，并且不会就 bundle 编译时依赖的 `DotCraft.Core` 报告任何信息。这种差异即便出问题，也是在首次使用时的成员解析阶段失败，而不是在加载阶段。
- **按宿主的每个 minor 版本重新编译。** 一个宿主 minor 版本就是一个兼容目标，`minHostVersion` 就是插件声明自己面向哪个目标的方式。

## 实现入口点

在一个带 public 无参构造函数的 public 类型上实现 `IDotCraftPlugin`。宿主在每个激活 generation 中构造它一次。

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugin.AcmeReview;

public sealed class Plugin : IDotCraftPlugin
{
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {
        context.Contributions.Add<IToolSource>(new PluginTool());
        return ValueTask.CompletedTask;
    }
}

internal sealed class PluginTool : AIFunctionToolSource
{
    public override string SourceId => "acme-review";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        yield return DotCraft.GeneratedTools.Acme.ReviewCore.Plugin
            .GeneratedToolFunctions.PluginTool_Status(this);
    }

    [GeneratedTool(Name = "acme_review")]
    [Description("Reports whether Acme Review is active.")]
    public string Status() => "Acme Review is active.";

    protected override ToolPolicyHints GetPolicyHints(
        AIFunction function,
        ToolPlanningContext context) => new(ReadOnly: true);

    protected override ToolPresentationDescriptor? GetPresentation(
        AIFunction function,
        ToolPlanningContext context) => null;
}
```

上面的生成命名空间假设入口程序集为 manifest 中的 `Acme.ReviewCore.Plugin.dll`。源码编译器同样使用 manifest 入口程序集的文件名作为程序集名。插件显式返回空的 Core 呈现描述符。如需 Desktop 呈现，请贡献插件自有的呈现组件。

激活上下文承载了插件模型的两个方向：

| 成员 | 用途 |
|---|---|
| **`Contributions`** | 仅激活期可用的注册方向。每个贡献都指明自己的贡献点，并返回一个归 generation 所有的句柄。 |
| **`Services`** | 公共宿主应用服务经过过滤的只读视图。 |
| **`Exports` / `Dependencies`** | 跨插件边界的类型化服务。仅激活期可用。 |
| **`Lifetime`** | 受管资源、后台工作，以及 `Stopping` 令牌。 |
| **`ContentRoot` / `DataRoot` / `WorkspaceRoot`** | 该 generation 的只读影子拷贝、插件自己的可写数据目录，以及 Workspace。 |
| **`Settings`** | 本插件的有效设置，在 generation 激活时生成快照。 |

请把 `ContentRoot` 当作只读，可变状态放在 `DataRoot` 下。配置了 `UserDataPath` 时，宿主把 `DataRoot` 解析为 `<UserDataPath>/plugins/<id>/data`，否则解析为 `<DataPath>/plugin-data/<id>`。同一插件的 Hooks 与 LSP 进程也会通过 `DOTCRAFT_PLUGIN_DATA` 获得同一个目录。

把所有 `Contributions.Add` 调用放在 `ActivateAsync` 内。激活提交时宿主会封闭 registrar，之后从后台工作发起的调用会被拒绝。若要改变某个 generation 的贡献集合，请改变它的输入，并让运行时协调过程重启 generation。

### 用 Lifetime 持有资源，而不是贡献

拆卸会先撤销贡献句柄、触发 `Stopping`，再排空已进入的调用与后台工作，之后才释放原始贡献目标。共享资源用 `context.Lifetime.Own` 或 `OwnAsync` 注册。它们比贡献目标存活更久，贡献只借用而不拥有。

后台工作走 `context.Lifetime.Run`。它在激活提交后启动，拆卸开始时通过 `Lifetime.Stopping` 取消。裸线程、静态事件订阅、无人跟踪的 Task 和全局缓存都可能钉住可回收的加载上下文：路由仍会立即停止，但内存要等到残留引用释放后才能回收，很多时候只能等进程重启。

## 编写强类型工具 {#write-typed-tools}

插件工具与内置工具使用同一套生成方法包装器。工作区配置与服务放在构造函数里，JSON 参数与默认值交给生成器处理。`AIFunctionToolSource` 提供注册，宿主负责赋予插件的 `PluginNative` 身份与 generation 生命周期。普通强类型工具不需要实现 `IToolRuntime`，也不需要手工解析 `JsonObject`。

只有需要真实 Thread、Turn 或调用身份的工具才请求上下文：

```csharp
[GeneratedTool(Name = "calling_thread")]
[Description("Report the calling thread's identity.")]
public ToolExecutionResult CallingThread(
    ToolInvocationContext context,
    [Description("Include the call identifier.")] bool includeDetails = false,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    return ToolExecutionResult.Succeeded(includeDetails
        ? $"Invocation belongs to thread {context.ThreadId}, call {context.CallId}."
        : context.ThreadId);
}
```

像入口点示例一样，通过 `CreateFunctions` 暴露该方法的生成工厂。上下文与取消令牌由运行时注入，不是模型参数。最多声明一个非空上下文，不得添加默认值、`ref`/`in`/`out` 或 schema 属性。缺少宿主上下文时直接调用生成函数，会在业务方法执行前失败。运行时不会从规划状态补造身份。

`ToolExecutionResult` 是可选的：普通结果使用字符串或 DTO 即可。声明为 `ToolExecutionResult`、`Task<ToolExecutionResult>` 或 `ValueTask<ToolExecutionResult>` 时保留成功/失败语义，不会把业务失败包装成成功的 JSON 响应，也不会推断 DTO 输出 schema。工具捕获取消后显式返回的结果仍会保留，因此可以表达“执行可能已经开始，不宜重放”。

这不会扩大插件边界。结果复制出插件时，宿主只保留成功的文本与结构化 JSON，或失败的文本与错误。不会转发插件自有对象、富 `AIContent`、私有结果元数据或执行指令。调用身份支持业务层的归属检查，不是针对完全受信任插件代码的沙箱。动态 schema 与协议桥接仍可使用底层 `IToolSource`/`IToolRuntime`。

## 访问宿主服务

`context.Services` 是经过过滤的 `IServiceProvider` 视图。从中解析公共应用服务，用内核的部件组装行为：

```csharp
using DotCraft.Sessions;

var sessions = (ISessionService?)context.Services.GetService(typeof(ISessionService))
    ?? throw new InvalidOperationException("ISessionService is unavailable.");
```

该提供程序由宿主拥有且只读。插件不能注册、装饰或替换容器服务。这个视图不暴露根 provider、贡献注册表、服务作用域工厂、宿主生命周期或插件运行时控制面。永远不要释放解析得到的服务。只有插件自己创建的东西才需要释放，并且要交给 `context.Lifetime`。

消费面同样受版本绑定约束：今天能解析到的服务，由你编译时面向的宿主版本保证，而不是由只增不改的承诺保证。请在 generation 停止前解除对宿主服务的回调、事件订阅和其他引用，好让加载上下文能够卸载。

### 读取自己的设置

`context.Settings` 是本插件由 schema 约束的有效设置快照，在 generation 激活时捕获。在 manifest 中声明 `"settings": "./settings.schema.json"`。宿主会校验 schema 默认值与两个存储层，然后依次解析默认值、个人设置和工作区设置。对象递归合并，数组和标量替换下层值。

```csharp
var limit = context.Settings.TryGetProperty("checklistLimit", out var value)
    && value.TryGetInt32(out var parsed) ? parsed : 3;
```

插件预期使用的每个字段都应声明 schema 默认值。配置 mutation 会先 quiesce 当前 generation，写入后自动 reconcile 本插件及其必要依赖闭包。quiesce 失败不会写文件，写入失败会恢复原 generation。已捕获的激活上下文绝不会被原地修改。若反序列化到插件自定义类型，请让 serializer options 与 metadata 都归插件所有，以便 generation 能够卸载。

完整的贡献点目录、排序规则、类型化导出、信任与 generation 生命周期见 [.NET Plugin API 与生命周期](./dotnet-plugin-reference)。

## 相关文档

- [Desktop Plugins](./desktop-plugins)——给同一个 bundle 加上 Desktop 界面。
- [.NET Plugin 架构规范](https://github.com/DotHarness/dotcraft/blob/main/specs/architecture/dotnet-plugins.md)——本页行为背后的规范原文。
