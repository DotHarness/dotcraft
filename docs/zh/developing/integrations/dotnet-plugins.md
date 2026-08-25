# 开发 .NET 插件

.NET 插件运行在 **DotCraft 进程内部**。MCP 服务器隔着边界与 DotCraft 通信，而 .NET 插件是从内部参与内核组装：它在具名贡献点上贡献工具、提示词分节、中间件和生命周期观察者，并可以解析宿主服务，用 DotCraft 自身的部件搭建这些贡献。

本页面向插件开发者。插件的用户视角见[插件与工具](../../features/agent-system/plugins-tools)；所有插件共用的清单字段见[插件市场](./plugin-market)。

> [!CAUTION]
> .NET 插件把完全受信任的代码加载进 DotCraft 进程，并获得该进程的文件系统、网络、凭据、原生互操作与操作系统权限。这里没有托管沙箱，也没有权限模型。安全性取决于你选择构建或信任哪些代码，而不是运行时边界。需要真正的信任边界时请使用 MCP。

仓库中的示例位于 `sdk/dotnet/samples/DotNetPluginSample/`：它包含两个 bundle、覆盖全部公共贡献点，并通过宿主的预检与运行时校验构建产物。

## 使用 DotCraft 创建

让 `$plugin-creator` 在当前工作区创建 .NET 插件。它会生成持久化的源码项目与标准开发 bundle：

```text
.craft/plugin-projects/<plugin-id>/
├── src/
└── plugin/
    ├── .craft-plugin/plugin.json
    └── lib/
```

项目没有 `.csproj`，也不会还原 NuGet 包。Agent 编辑 `src/**/*.cs` 和 manifest，通过 `DotNetPlugin.Inspect` 查询准确的 API 签名与文档，再调用 `DotNetPlugin.Build`。构建过程使用当前宿主随附的公共插件 API 与 BCL 完成编译，执行 metadata preflight，原子发布 bundle 并将其激活；不需要外部 .NET SDK 或网络访问。

构建成功后，精确的插件 id 与指纹只在当前宿主进程中获得执行资格；该过程不会修改 `dotnet-plugin-trust.json`。DotCraft 重启后需要重新构建。对同一项目中已激活的相同指纹重复构建不会产生变更。

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

插件 id 以 ASCII 字母或数字开头，后续还可包含 `.`、`_`、`:` 或 `-`；只要存在 `dotnet`，`version` 就是必填。所有路径以 `./` 开头、不得越出插件根目录，并且必须指向构建好的 bundle 中已存在的文件。

### 引用 DotCraft.Core，但不要随包分发

插件 API 就是 `DotCraft.Core` 本身，外加它传递引用的 `DotCraft.Agents` 和 Microsoft.Extensions.AI。没有单独的 SDK 程序集。

```xml
<ProjectReference Include="path/to/src/DotCraft.Core/DotCraft.Core.csproj" Private="false" />
```

加载上下文按**简单名称**把每个 DotCraft 程序集及其包闭包解析到宿主中已加载的副本，忽略 bundle 自带的版本。类型标识的单一性正来自于此：中间件贡献改写的 `ChatMessage`，与内核派发时使用的是同一个类型——哪怕 bundle 自带了 `Microsoft.Extensions.AI.Abstractions.dll`。随包分发这些程序集只会让 bundle 变大。其余内容都从 bundle 的 `.deps.json` 与相邻探测解析，并限制在 bundle 目录内。

### 绑定到宿主版本

由于内核的整个公共面就是插件 API，兼容性绑定的是宿主版本，而不是一份只增不改的契约。

- `minHostVersion` 是硬性门槛。低于它的宿主会让插件停在 `blocked`，并报 `PluginHostVersionUnsatisfied`，插件代码一行都不会运行。
- 更新的宿主会尽力加载插件，并且不会就 bundle 编译时依赖的 `DotCraft.Core` 报告任何信息。这种差异即便出问题，也是在首次使用时的成员解析阶段失败，而不是在加载阶段。
- **按宿主的每个 minor 版本重新编译。** 一个宿主 minor 版本就是一个兼容目标，`minHostVersion` 就是插件声明自己面向哪个目标的方式。

## 实现入口点

在一个带 public 无参构造函数的 public 类型上实现 `IDotCraftPlugin`。宿主在每个激活 generation 中构造它一次。

```csharp
using DotCraft.Contributions;
using DotCraft.Plugins;

namespace Acme.ReviewCore;

public sealed class Plugin : IDotCraftPlugin
{
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {
        var journal = new ReviewJournal(context.DataRoot);
        context.Lifetime.Own(journal);
        context.Contributions.Add<IToolSource>(new SummaryTool(journal));
        return ValueTask.CompletedTask;
    }
}
```

激活上下文承载了插件模型的两个方向：

| 成员 | 用途 |
|---|---|
| **`Contributions`** | 仅激活期可用的注册方向。每个贡献都指明自己的贡献点，并返回一个归 generation 所有的句柄。 |
| **`Services`** | 公共宿主应用服务经过过滤的只读视图。 |
| **`Exports` / `Dependencies`** | 跨插件边界的类型化服务。仅激活期可用。 |
| **`Lifetime`** | 受管资源、后台工作，以及 `Stopping` 令牌。 |
| **`ContentRoot` / `DataRoot` / `WorkspaceRoot`** | 该 generation 的只读影子拷贝、插件自己的可写数据目录，以及 Workspace。 |
| **`Settings`** | 本插件的有效设置，在 generation 激活时生成快照。 |

请把 `ContentRoot` 当作只读，可变状态放在 `DataRoot` 下。

所有 `Contributions.Add` 调用都应放在 `ActivateAsync` 内。激活提交时，宿主会封闭 registrar；之后从后台工作发起的调用会被拒绝。若要改变某个 generation 的贡献集合，请改变它的输入，并让运行时协调过程重启 generation。

### 用 Lifetime 持有资源，而不是贡献

拆卸会先撤销贡献句柄、触发 `Stopping`，再排空已进入的调用与后台工作，之后才释放原始贡献目标。共享资源应通过 `context.Lifetime.Own` 或 `OwnAsync` 注册；这些资源会比贡献目标存活更久，贡献只借用而不拥有它们。

后台工作走 `context.Lifetime.Run`。它在激活提交后启动，拆卸开始时通过 `Lifetime.Stopping` 取消。裸线程、静态事件订阅、无人跟踪的 Task 和全局缓存都可能钉住可回收的加载上下文：路由仍会立即停止，但内存要等到残留引用释放后才能回收，很多时候只能等进程重启。

## 访问宿主服务

`context.Services` 是经过过滤的 `IServiceProvider` 视图。从中解析公共应用服务，用内核的部件组装行为：

```csharp
using DotCraft.Sessions;

var sessions = (ISessionService?)context.Services.GetService(typeof(ISessionService))
    ?? throw new InvalidOperationException("ISessionService is unavailable.");
```

该提供程序由宿主拥有且只读。插件不能注册、装饰或替换容器服务。这个视图不暴露根 provider、贡献注册表、服务作用域工厂、宿主生命周期或插件运行时控制面。永远不要释放解析得到的服务；只释放插件自己创建的东西，并通过 `context.Lifetime` 释放。

消费面同样受版本绑定约束：今天能解析到的服务，由你编译时面向的宿主版本保证，而不是由只增不改的承诺保证。请在 generation 停止前解除对宿主服务的回调、事件订阅和其他引用，好让加载上下文能够卸载。

### 读取自己的设置

`context.Settings` 是本插件有效 `Plugins.Settings[id]` 设置袋的快照，在 generation 激活时捕获。它的形状由插件定义，宿主不做校验。配置改动只有在运行时协调并重启 generation 后才可见；它绝不会修改已经捕获的激活上下文。

```csharp
var limit = context.Settings.TryGetProperty("checklistLimit", out var value)
    && value.TryGetInt32(out var parsed) ? parsed : 3;
```

每个字段都要自备回退值，因为未配置的插件读到的是空对象。若反序列化到插件自定义类型，请让 serializer options 与 metadata 都归插件所有，以便 generation 能够卸载。

## 选择贡献点

贡献点目录就是全部贡献面。每个贡献点声明自己支持哪些能力层级：

| 层级 | 作用 |
|---|---|
| **A —— 追加** | 在已有条目旁增加一项，按 `Order` 升序排列，同值时以注册顺序决胜。 |
| **B —— 替换** | 用 `ReplaceTarget` 遮蔽一个*具名*默认项。替换句柄一旦释放，默认项立即回归。 |
| **C —— 接管** | 对某个贡献点的组装结果拥有最终决定权。它通过 Tier-B 机制实现，由接收已组装结果的契约承担。 |

| 贡献点 | 契约 | 层级 |
|---|---|---|
| **工具** | `IToolSource` | A |
| **系统提示词分节** | `ISystemPromptSection`，Tier C 为 `ISystemPromptAssembler` | A、B、C |
| **会话上下文条目** | `IChatContextProvider` | A |
| **Thread 提示词上下文** | `IThreadSystemPromptContextProvider`（仅 `BaseInstructions`） | A |
| **发送前上下文变换** | `IAgentContextSource` → `AIContextProvider` | A、B |
| **Chat 中间件** | `IChatMiddleware` | A、B |
| **派发阶段** | policy、approval、recorder、normalizer 各阶段接口 | A、B |
| **Thread 生命周期** | `IThreadLifecycleContributor` | A |
| **Turn 生命周期** | `ITurnLifecycleContributor` | A |
| **Thread 运行时信号** | `IThreadRuntimeSignalContributor` | A |
| **斜杠命令** | `ICodeCommand` | A |
| **工具约束** | `IToolRestriction` | A |
| **压缩摘要** | `ICompactionSummarizer` | B |
| **可压缩工具** | `ICompactableToolPolicy` | A、B |
| **SubAgent 运行时** | `ISubAgentRuntimeSource` | A |
| **Trace 汇聚** | `ITraceSink` | A |
| **辅助生成器** | `ICommitMessageSuggester`、`IWelcomeSuggester` | B |

失败处理由各贡献点决定。观察与扇出类贡献通常会记录并跳过失败项；result normalization、compaction 等权威变换可以让所属操作失败。预期失败应通过契约的结果类型返回，而不是抛异常。

### Tier A —— 增加一个工具

插件通过 `IToolSource` 贡献工具——内核自己的工具源用的就是这个贡献点。一个源把每个工具声明为一份**定义**（标识、规范名、描述、JSON schema、策略提示）与一份**运行时绑定**（真正执行它的代码）的组合。

宿主在贡献边界上把自己插在中间：它把定义复制成自己的对象，把标识重新编成带 `PluginNative` 来源的 `(pluginId, toolId)`，并把绑定换成只持有 `(pluginId, generationId, toolId)` 的代理，每次调用都重新解析你的源。因此冻结的工具快照里不含任何插件分配的对象：撤销贡献立即生效，而从旧快照派发的调用会以 `tool_unavailable` 失败。插件工具与内置工具一样获得 schema 校验、策略、审批、Hooks 和记录，`PolicyHints` 只是给宿主策略的提示，永远不会覆盖它。

```csharp
internal sealed class SummaryTool(ReviewJournal journal) : IToolSource, IToolRuntime
{
    private const string ToolId = "review-summary";

    public string SourceId => "acme.review-core.summary";

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.PluginNative,
            SourceId,
            new SourceToolId(ToolId));
        var registration = new ToolRegistration(
            new ToolDefinition(
                definitionId,
                new ToolName("review", "summary"),
                "Normalizes review text.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new { text = new { type = "string" } },
                    required = new[] { "text" },
                    additionalProperties = false
                }),
                policyHints: new ToolPolicyHints(RequiresApproval: false, ReadOnly: true)),
            new ToolRuntimeBinding(
                new RuntimeBindingId($"{SourceId}:{ToolId}:{context.Revision}"),
                definitionId,
                this,
                ToolBindingLeases.AlwaysAvailable,
                SourceId,
                context.Revision),
            ToolProjectionShape.StandardPair);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([registration]);
    }

    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        journal.Write("echo tool invoked");
        return ValueTask.FromResult(ToolExecutionResult.Succeeded(
            arguments["text"]?.GetValue<string>() ?? string.Empty));
    }
}
```

`GetRegistrationsAsync` 每次工具规划都会被调用一次，所以要保持轻量、无副作用，并只返回对收到的 `ToolPlanningContext` 有效的工具。一个源可以声明多个工具；同一插件内重复的工具 id 会被记为诊断并跳过，而不会让激活失败。

预期内的失败请用 `ToolExecutionResult.Failed` 配一个 `ToolError` 返回，这样它的稳定错误码得以保留。抛出异常的正文会被丢弃，模型只会看到未指明的工具失败。边界两个方向都只走 JSON：宿主把参数复制进去，把文本、结构化内容和错误复制出来。

### Tier A —— 增加一个斜杠命令

插件通过 `ICodeCommand` 贡献斜杠命令：一个名字、可选的别名、一段描述，以及把一次调用展开成本轮输入文本的 `Expand`。

```csharp
internal sealed class TriageCommand(ReviewService service) : ICodeCommand
{
    public string Name => "triage";

    public string Description => "汇总待评审队列";

    public IReadOnlyList<string> Aliases => ["tri"];

    public string? Expand(CommandInvocation invocation) =>
        service.BuildTriagePrompt(invocation.Arguments);
}
```

贡献的命令就是一条“正文是代码”的 Markdown 自定义命令，宿主也在同样的地方伺服它：命令面板、`command/list`、`command/execute`、ACP 斜杠命令列表都会自动收录，客户端无需任何改动。它只产出模型输入——直接回复用户仍由宿主自己的命令负责。

它不会遮蔽任何东西：内置命令、工作区 Markdown 命令、工作流命令在重名时都保留自己的名字。贡献之间由 `Order` 最小者取得名字，返回 `null` 表示放弃本次调用、交给下一个贡献回答。释放句柄即刻把命令从所有列举与解析中移除——贡献点是按调用读取的，不会有任何残留注册。

### Tier B —— 替换具名默认项

内置提示词分节和中间件都以稳定的目标名注册。把 `ReplaceTarget` 设为其中之一，你的贡献就会在句柄存活期间遮蔽它：

```csharp
context.Contributions.Add<ISystemPromptSection>(
    new ReviewResponseStyleSection(),
    new ContributionOptions(ReplaceTarget: SystemPromptSectionNames.ResponseStyle));
```

Agent 内置的记忆 Provider 也以同样的方式注册，目标名为 `AgentContextSourceNames.Memory`；替换它就等于接管整份系统提示词，而 `AgentContextRequest.PromptInputs` 会带上内置项本会用到的构建期取值——工具名、延迟加载的 MCP Server、SubAgent 档案分节、Skill 变体目标。这也是唯一一个返回 `null` 表示**放弃而非抑制**的目标：内置项会顶上，于是没有哪个 Agent 会在没有提示词的情况下运行。

“抑制”就是一个不产出内容的替换：返回 `null` 的分节会把内置项整体移除，不存在单独的移除动作。当两个替换指向同一个名字时，Thread 作用域胜过 Workspace 作用域，同一作用域内后注册者胜出；`Order` 只负责排列解析列表，从不参与替换的裁定。落败的一方记为 `ReplaceConflict` 诊断，而不会让贡献点失败。

### Tier C —— 接管贡献点的输出

接管就是一个普通贡献点，只不过它的契约接收组装好的默认结果并返回最终结果——系统提示词对应的是 `ISystemPromptAssembler`。消费方采用解析列表中的**最后一个**贡献，因此“每个贡献点至多一个生效接管”是排序的结果，而不是注册表强制的规则。既没有排在更后、也没有声明替换的接管，只是单纯不生效。

### 顺序与作用域

顺序只在单个贡献点内有意义。相对内置项定位时，请使用该贡献点公开的 target 名称与 order 常量；除非契约另有说明，较小的值先执行。`ContributionOptions.Scope` 默认为 `Workspace`；`ContributionOptions.ForThread(threadId)` 把贡献限定到单个 Thread。Fork 出的 Thread 不继承 Thread 作用域贡献；若希望贡献跟随 Fork，请使用 Workspace 作用域，或在其 `started` 生命周期事件中重新注册。

按 Turn 或按调用求值的贡献点会在下一次求值时看到变更。被固化进每 Thread 状态的那些——工具快照、Agent 的管线与指令——则通过宿主的失效链重建。进行中的 Turn 会用它开始时的那套贡献跑完。

## 导出与消费类型化服务

把公共服务接口放进独立程序集，并在 `exportedApiAssemblies` 中列出。Provider 在激活期导出实现：

```csharp
context.Exports.Add<IReviewService>(new ReviewService());
```

直接消费方声明它所需的 Provider 最低兼容版本，并在自己的激活期解析该接口：

```json
{ "dependencies": { "acme.review-core": "1.0.0" } }
```

```csharp
var review = context.Dependencies.GetRequired<IReviewService>("acme.review-core");
```

依赖用于协调 generation 生命周期与 API 共享，不负责解析私有包。服务仅能在激活期解析；Provider 先于消费方激活、后于消费方停止，因此请在 `ActivateAsync` 中捕获服务，并让所有工作受消费方 lifetime 管理。

导出签名应只使用插件自己导出的类型，以及双方共享的宿主程序集类型。在同一消费方的 Provider 集合内，导出 API 程序集的简单名称必须唯一；冲突会报 `PluginApiAssemblyConflict`。

依赖版本表示同一兼容线内的最低版本。`"acme.review-core": "1.2.0"` 接受 `1.2.0` 及更高的 `1.x` 版本，但不接受 `2.0.0`。对于 `0.x`，major 和 minor 都必须相同：`0.2.1` 接受之后的 `0.2.x`，不接受 `0.3.0`。低于最低版本或位于其他兼容线的 Provider 会让消费方停在 `blocked` 并报 `PluginDependencyUnsatisfied`。

在同一兼容线内升级时，Provider 必须保持每个导出 API 程序集的 identity 不变：简单名称、`AssemblyVersion`、culture 与 public-key token。破坏性 API 变更需要开启新的兼容线，并且通常应使用新的插件 id 与 API 程序集 identity。请声明能提供所用 API 的最低兼容版本。

## 信任

安装或启用 `dotnet` 插件都不会授予信任。已安装的插件只有在启用后，且当前 bundle 指纹已在机器本地权限文件中获得显式授权时才会运行；否则仍会被阻断。

- **授权绑定精确的 id 与指纹。** 客户端只按插件 id 请求信任；服务端把授权绑定到它实际接受的那份字节。同一个插件 id 可以同时保留多个已授权指纹。只有变更后指纹没有匹配授权时，插件才会变成 `modified`。
- **路径也是指纹的一部分。** DotCraft 对带版本且以长度分隔的 bundle 文件树做哈希，因此即使字节不变，仅把它们移动到其他文件也会改变身份。仅用于部署的 `.builtin` 标记既不计入指纹，也不会进入运行时快照。
- **权限文件独立于配置。** 授权保存在全局配置旁的 `dotnet-plugin-trust.json`，但该文件不参与配置合并，Workspace 配置也不能授予信任。
- **已安装插件不存在隐式信任层级。** 每个已安装的 `dotnet` 插件都需要显式授权，宿主自带的 bundle 也不例外。
- **撤销按指纹生效。** 撤销只移除当前插件 id 与指纹这一对授权。如果活跃闭包依赖该授权，它会停止；同一 id 的其他指纹授权保持不变。

没有匹配授权时，已安装插件停在 `blocked` 并报 `PluginUntrusted` 或 `PluginTrustModified`，而且**不会创建任何加载上下文**，因此它的代码一行都没有运行过。`DotNetPlugin.Build` 开发构建会改为让开发 bundle 的精确指纹获得进程内执行资格；该资格不会持久化，也不适用于已安装插件。

## 生命周期与更新

每次激活都会获得自己的可回收加载上下文和一个不透明的 generation id。激活从每 generation 的影子拷贝加载 bundle，因此已安装目录可以在某个 generation 仍存活时被替换。

| 状态 | 含义 |
|---|---|
| **`stopped`** | 插件已禁用，没有存活的 generation。 |
| **`blocked`** | 尚未尝试，且原因明确：预检失败、`minHostVersion` 不满足、依赖不可用，或信任缺失/失效。不存在加载上下文。 |
| **`activating`** | 正在构建候选 generation，其中任何内容都尚未发布。 |
| **`active`** | 一个 generation 已提交并正在接收调用。 |
| **`deactivating`** | 已关闭准入，generation 正在排空。 |
| **`faulted`** | 已尝试，且失败方式明确：构造、激活或已注册的后台工作失败。 |
| **`reclaiming`** | 功能上已停止、不再路由任何调用，但内存尚未归还。 |

`blocked` 不是终态，只要其成因可能已经改变——宿主升级、依赖激活、授予信任、重新安装——就会被重新评估。已安装的 `faulted` 插件通过停用再启用来重试；对于开发项目，请修正源码后再次运行 `DotNetPlugin.Build`。

### 撤销是确定的，回收不是

停用会先撤销每一个贡献句柄，因此不会再有新调用到达该 generation；较旧的工具快照会收到 `tool_unavailable`。普通变更最多等待到运行时的 cleanup timeout。若插件工作忽略取消，功能拆卸会保持待完成状态：在它真正结束之前，该插件不能重新激活，它依赖的 Provider 也不能停止。

宿主关机时会等待功能拆卸实际完成，再释放 Provider 与宿主根容器；即使这超过 cleanup timeout 也一样。硬性的关机截止时间由服务管理器或其他外层进程负责。功能拆卸完成后，程序集内存回收才进入尽力而为阶段，此时不会阻塞替换、依赖拆卸或关机。`leakedGenerations` 与 `restartRecommended` 会暴露加载上下文仍被钉住的 generation；其内存只能通过重启进程释放。

### 替换已安装 bundle

通过插件安装流程管理的 bundle 按以下方式更新：停用插件、替换文件、再次启用。对于 `.craft/plugin-projects` 下的项目，`DotNetPlugin.Build` 会完成发布和 generation 替换，不走这组客户端操作。

停用会先按消费方优先的顺序撤销 .NET generation 及其消费方。文件系统变更还会要求以根目录为依托的声明式贡献停止；若这一步失败，变更会返回 `notApplied` 与 `PluginContributionQuiesceFailed`，并保持 bundle 目录不变。重新启用会重新准入当前字节。

新字节属于新的 bundle，指纹也是新的。只有该精确指纹已获授权时它才会激活；否则信任会变成 `modified`，直到用户确认。版本号不变的内容变更同样会产生新的 generation。若新 bundle 随后停在 `blocked` 或 `faulted`，DotCraft 不会回滚到上一份 bundle。

### 客户端操作

| 方法 | 对 `dotnet` 插件的作用 |
|---|---|
| `plugin/install`、`plugin/installLocal` | 复制 bundle，但从不授予信任。激活仍要求插件已启用，且指纹授权匹配。 |
| `plugin/setEnabled` | 只改变意图。其他前置条件都满足时启用即激活；停用总会生效。 |
| `plugin/setTrusted` | 授予或撤销当前精确的插件 id/指纹对，两种情况都会重新规划闭包。 |
| `plugin/remove` | 先 quiesce .NET 与根目录贡献，再删除目录。quiesce 失败时目录保持不变。 |

每次变更都返回 `PluginOperationResult`——`applied`、`noChange` 或 `notApplied`——并附带这一批次影响到的其他插件的 id 与状态。操作结果与它产生的运行时状态相互独立：一次 `applied` 的安装完全可能落在 `blocked`。阻断项携带稳定代码与结构化参数；客户端应为信任、宿主版本、清单、依赖和激活失败提供对应处理办法。协议报文形状与权威代码列表见 [AppServer 协议](../protocols/appserver-protocol)。

## 相关文档

- [插件与工具](../../features/agent-system/plugins-tools)
- [插件市场](./plugin-market)
- [Desktop 扩展](./desktop-extensions)
- [AppServer 协议](../protocols/appserver-protocol)
- [安全与沙箱](../../features/self-hosted/security)
