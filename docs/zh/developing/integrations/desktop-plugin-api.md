# Desktop Plugin API

本页是受信任 Desktop Plugin 的 API 参考。要创建并构建第一个插件，请先读[开发 Desktop Plugin](./desktop-plugins)。

## 使用四个内核原语

Runtime 只有四个内核原语。六类 contribution 在此之上，为常见的产品集成提供简洁写法。

### 管理副作用

如果一项工作需要 setup 与 cleanup，却没有天然的 React owner，请使用 `host.effect`：

```ts
host.effect(() => {
  const interval = window.setInterval(refreshBoard, 30_000);
  window.addEventListener("online", refreshBoard);

  return () => {
    window.clearInterval(interval);
    window.removeEventListener("online", refreshBoard);
  };
});
```

Effect、Host 持有的订阅，以及通过其他原语创建的注册，都属于当前 revision 的 generation。禁用、卸载、替换 revision 或关闭 Desktop 时，它们会被一起清理。

### 组合 UI surface

按需要做的改动选择 `host.ui` 的三种操作：

| 操作 | 组合规则 |
|---|---|
| **`add`** | 保留全部 active registration，由 surface 按 `order` 一起渲染。 |
| **`replace`** | 使用最后一个 active registration。Dispose 后恢复前一个 replacement 或 Core default。 |
| **`wrap`** | 包装当前 surface。后注册的 wrapper 位于早期 wrapper 外层。 |

每次调用都会返回一个可 dispose 的 registration，它同样由 generation 持有。Dispose 一个 `add` 只移除该项。Dispose 一个 `replace` 会显露下一个 active replacement。Dispose 一个 `wrap` 会用剩下的 wrapper 重新组成链条。

Replacement 生效时，被替换的 default component tree 不会挂载。Dispose replacement 会重新挂载当前 fallback，而不是显露一个一直在后台运行的隐藏实现。

“最后”和“后注册”都按实际注册顺序判断，包括来自不同插件的 registration。

当排列顺序有意义时，给 `add` 传一个 `order`。Addition 按 `order` 升序渲染，`order` 相同的（包括所有省略它、默认为 100 的）则在彼此之间保持注册顺序：

```ts
host.ui.add("composer.status", ReviewStatus, { order: 50 });
```

`replace` 和 `wrap` 仍然只按注册顺序。它们的堆叠是一份 dispose 契约而非排列，引入 `order` 会让先注册的项永久压过后注册的项。

如果需要保留当前实现，只在外层添加行为或布局，请使用 `wrap`：

```tsx
import type { DesktopPluginSurfaceWrapperProps } from "@dotcraft/plugin";

function ReviewFrame({
  children,
}: DesktopPluginSurfaceWrapperProps<"composer">) {
  return <section className="acme-board-review-frame">{children}</section>;
}

host.ui.wrap("composer", ReviewFrame);
```

### 共享 service

当另一个插件需要可调用的契约，而不是视觉 surface 时，使用 renderer-local service：

```ts
interface BoardService {
  openCard(id: string): void;
}

host.services.provide<BoardService>("acme-board.board", {
  openCard: (id) => openBoardCard(id),
});

const board = host.services.use<BoardService>("acme-board.board");
board?.openCard("DC-42");
```

`use` 返回最后一个 active provider 的同步快照。Dispose 该 provider 之后，`use` 会回到上一个 provider。Desktop 模块可能并行激活，因此要在交互真正需要 service 时再解析，并处理 `undefined`。Manifest 里的依赖只排定 .NET generation 的顺序，不会让某个 Desktop provider 先激活。Renderer service 不会自动跨到 .NET、CLI、远程客户端或 AppServer。

### 发布 event

如果只是发布发生过的事件，不需要共享 service reference，请使用 event：

```ts
host.events.on<{ cardId: string }>("acme-board.card-opened", ({ cardId }) => {
  console.log("Opened", cardId);
});

host.events.emit("acme-board.card-opened", { cardId: "DC-42" });
```

Event listener 会随 generation 一起移除。Event 只存在于 renderer，不会写入 Session 数据，也不会变成 AppServer notification。

## 使用 Core surface

DotCraft 的正式 surface 覆盖 application 与 Composer。Composer surface 采用层级结构，既可以定位完整区域，也可以定位单个 Core 控件：

| Surface | 位置 |
|---|---|
| **`app`** | 完整渲染出的 Desktop application。 |
| **`app.background`** | application shell 后方由 Host 持有的装饰位。背景媒体在这里渲染；shell 如何叠加在其上由 `host.appearance` 控制。 |
| **`app.overlay`** | application shell 前方的空位，默认穿透点击。 |
| **`app.status`** | 由 Host 持有的右下角状态轨道，用于紧凑、持续的诊断信息。它与 Core 指示器的位置和间距由 Host 管理。 |
| **`composer`** | 完整的已挂载 Composer，包括新聊天 welcome、创建 thread 前的 embedded Composer 与 active thread 状态。 |
| **`composer.mascot`** | Composer mascot 的 58×58 逻辑像素 visual stage。 |
| **`composer.before`** | Composer body 之前的内容。 |
| **`composer.after`** | Composer shell 之后的内容。 |
| **`composer.input`** | 完整的 attachment 与 rich-input 区域。 |
| **`composer.toolbar`** | Composer card 内完整的 control row。 |
| **`composer.toolbar.leading`** | command、permission、mode 与 goal 所在的 leading group。 |
| **`composer.toolbar.trailing`** | context、model、voice 与 submit 所在的 trailing group。 |
| **`composer.status`** | Composer card 下方的 workspace 与 subscription 状态行。 |

当区域范围过大时，可以直接定位这些 Core 控件：

| 区域 | 控件 surface |
|---|---|
| **Input** | `composer.input.attachments`、`composer.input.editor` |
| **Leading toolbar** | `composer.toolbar.commands`、`composer.toolbar.permissions`、`composer.toolbar.mode`、`composer.toolbar.goal` |
| **Trailing toolbar** | `composer.toolbar.context-usage`、`composer.toolbar.model`、`composer.toolbar.voice`、`composer.toolbar.submit` |
| **Status** | `composer.status.workspace`、`composer.status.subscription` |

![Composer 公共 surface 层级](/desktop-plugin-composer-surfaces.svg)

Core 会把正常组件作为每个 surface 的 default content。`add` 在这份内容之后渲染。想在它之前插入内容又保留原行为，用 `wrap`。想移除 Core 组件并自行接管这块行为，用 `replace`：

```tsx
import { Button } from "@dotcraft/plugin";
import type {
  DesktopPluginSurfaceProps,
  DesktopPluginSurfaceWrapperProps,
} from "@dotcraft/plugin";

function BeforeModel({ children }: DesktopPluginSurfaceWrapperProps<"composer.toolbar.model">) {
  return (
    <>
      <Button size="sm">Review model</Button>
      {children}
    </>
  );
}

function SubscriptionStatus(_: DesktopPluginSurfaceProps<"composer.status.subscription">) {
  return <span>Review ready</span>;
}

host.ui.wrap("composer.toolbar.model", BeforeModel);
host.ui.add("composer.status.subscription", SubscriptionStatus);
```

需要让只读状态与 DotCraft 的窗口指示器共存时，请使用 `app.status`。`app.status` 贡献不要自行相对 viewport 定位。装饰内容或需要独立定位的内容继续使用 `app.overlay`。

同一组名称会挂载在 thread、Welcome、approval 与 user-input Composer 中。即使当前 provider、compact mode、minimal chrome 或 decision state 隐藏了 Core default，surface 仍然可用。渲染插件内容前应检查共享的 Composer context。Surface 名称与它的 typed context 属于公共契约，surface 生成的 DOM 不属于。

上面列出的 Core 名称就是全部。如果在 `app` 或 `composer` 下注册了 Core 并未定义的名称，Desktop 会保留这次注册，同时在控制台写一条点名该 surface 的警告——出现在这两个根下面时，它基本上就是拼写错误。这两个根之外的名称属于插件，因此不做检查：把内容注册到另一个插件尚未挂载的 surface 上很正常，那个 surface 一出现，你的组件就会渲染。

只要 Composer 尚未创建或挂接到真实 Session thread，surface context 的 `threadId` 就是 `null`，welcome 与 detached embedded Composer 都是如此。挂接之后它是真实 thread id。

| 字段 | 含义 |
|---|---|
| `workspacePath` | 当前 workspace 路径，不可用时为 `null`。 |
| `threadId` | 已挂接的 Session thread，挂接前为 `null`。 |
| `mode` | 当前 `agent` 或 `plan` 模式。 |
| `busy` | Composer 正在运行、等待或执行维护。 |
| `awaitingApproval` | Host 正在等待审批决定。 |
| `variant` | `default` 或 `agentBuilder` 等嵌入式 Composer 变体。 |
| `minimalChrome` | Core 为嵌入式 Composer 隐藏了非必要控件。 |

在新聊天 Welcome 页，`composer` 覆盖创建 thread 之前的完整撰写体验：app 选择、hero、输入框、workspace footer 与 quick starts。这些元素共享同一份 draft 与 voice lifecycle，替换 `composer` 会把它们作为一个整体换掉。

### 替换 Composer mascot

替换 `composer.mascot` 后，可以使用图片、SVG、canvas、Lottie player 或 React character。下面的 inline SVG 不依赖额外资源，可直接构建：

```tsx
import type { DesktopPluginActivate, DesktopPluginSurfaceProps } from "@dotcraft/plugin";

function Mascot({ context }: DesktopPluginSurfaceProps<"composer.mascot">) {
  return (
    <svg
      viewBox="0 0 58 58"
      width={context.size}
      height={context.size}
      data-activity={context.activity}
      role="img"
      aria-label="Acme mascot"
    >
      <circle cx="29" cy="29" r="24" fill="var(--accent)" />
      <circle cx="21" cy="26" r="3" fill="currentColor" />
      <circle cx="37" cy="26" r="3" fill="currentColor" />
      <path d="M20 38 Q29 44 38 38" fill="none" stroke="currentColor" strokeWidth="3" />
    </svg>
  );
}

export const activate: DesktopPluginActivate = (host) => {
  host.ui.replace("composer.mascot", Mascot);
};
```

Core 继续管理 mascot 的位置、bubble、menu、click handling、sleep timer、Composer handoff 与 outer motion。Context 继承普通 Composer 字段，并增加 `activity`、`expression`、`light`、`size`、`submitRevision`、`reasoningEffort`、`speed`、`contextMax` 与 `reducedMotion`。直接响应这些 snapshot 即可。同一状态下连续提交、需要一次性动画时，监听 `submitRevision`。插件自定义的 occurrence 继续用 `host.events`。

`ui.add("composer.mascot", ...)` 会在同一 stage 上叠加 accessory 或 effect。如果插件还要控制位置与交互行为，请直接替换 `composer`。`composer.mascot` 不会改变 Error Screen mascot 或 Agent Profile avatar。

## 暴露插件 surface

在组件中渲染 `PluginSurface`，即可暴露 plugin-owned extension point：

```tsx
import { PluginSurface } from "@dotcraft/plugin";

declare module "@dotcraft/plugin" {
  interface DesktopPluginSurfaceContextMap {
    readonly "acme-board.card.footer": {
      readonly issueId: string;
    };
  }
}

function BoardCard() {
  return (
    <article className="acme-board-card">
      <h2>DC-42</h2>
      <PluginSurface name="acme-board.card.footer" context={{ issueId: "DC-42" }} />
    </article>
  );
}
```

把自定义名称加入 `DesktopPluginSurfaceContextMap`，owner 与所有 consumer 才会获得同一个 context 类型。Provider 与 consumer package 应当导入同一份 declaration module。没有 declaration merging 时，未知 surface 的 context 类型是 `unknown`。另一个已启用插件可以通过 `ui.add`、`ui.replace` 或 `ui.wrap` 定位 `acme-board.card.footer`，activation 顺序不影响注册。建议使用带插件前缀的名称，但不强制。

Surface 只在它所在的组件 mounted 时存在。Registration 仍由注册它的 revision 持有，并在 surface 出现时渲染。

## 使用便捷 contribution

如果某种标准产品集成已经符合需求，可以返回 `DesktopPluginActivation`：

| 字段 | 便捷行为 |
|---|---|
| **`mainViews`** | 添加 navigation、routing 与完整 view。 |
| **`settingsPages`** | 向 Desktop Settings 添加 page。 |
| **`conversationViews`** | 在 Chat 旁添加 thread-scoped tab。 |
| **`commands`** | 添加带 availability 与 execution 的 searchable command。 |
| **`toolRenderers`** | 渲染精确的 `presentationId`，并保留 Core 与 generic fallback。 |
| **`messageActions`** | 向标准 assistant-message action area 添加 action。 |

这六个字段是 convenience API，不是 allowlist，也不是能力上限。Composer UI 用 `host.ui.add("composer.toolbar.leading", ...)` 这类调用添加。功能不适合这些便捷字段时，直接使用 surface、service、event 与 effect。

返回的 activation 还可以提供 `dispose()`。Contribution id 在同一个 activation 内必须唯一。本地化标签放进 `label.translations`，按应用 locale 作键：Desktop 会对查找的两侧都做归一化，所以 `zh-CN` 这个键同样能被 `zh-Hans` 的读者命中，而七个 locale 之外的键会回退到 `label.default`。`order` 只在 convenience API 定义了排序位置时才需要设置。

### 给 contribution 配图标

`mainViews`、`settingsPages`、`conversationViews`、`commands` 与 `messageActions` 都接受可选的 `icon`。插件特有的图形请传组件，它会收到 `size`、`strokeWidth`、`aria-hidden` 与 `style`，并通过 `currentColor` 继承周围的文字颜色：

```tsx
import type { DesktopPluginIconProps } from "@dotcraft/plugin";

function ReviewIcon({ size = 16, ...rest }: DesktopPluginIconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      {...rest}
    >
      <path d="M4 6h16M4 12h10M4 18h7" />
    </svg>
  );
}
```

`icon` 只接受组件这一种形式。如果并不在意具体图形，就不要写 `icon`，Desktop 会画上自带的回落图形，那一行不会空着。

## 使用 Host API

除四个原语外，`DesktopPluginHost` 按 owner 对稳定的产品操作分组：

| 成员 | 用途 |
|---|---|
| `plugin`、`environment` | 插件的 id、版本与显示名，以及当前 locale、theme、theme 种子与变更订阅。 |
| `appearance` | 由 generation 持有的 theme seed 与 backdrop presentation contribution。 |
| `session` | 前台 workspace、当前 thread、mode 与忙碌状态，以及变更订阅。 |
| `navigation` | 打开插件 view、Settings 页与 thread，并接管自定义 scheme 的链接。 |
| `ui` | 除三个 surface 操作外，还提供 toast、确认与颜色选择等 Host-owned 对话框。 |
| `appServer` | 受支持的 JSON-RPC request 与 subscription。 |
| `settings` | 读取、修改并跟随本插件由 schema 约束的设置。 |
| `appBindings`、`appSurfaces` | Connected App binding 与 app 提供的 UI surface。 |
| `workspaces` | 读取本地 workspace 信息。 |
| `oratorio` | Team run、handoff 与 event。 |

Host API 是兼容性契约，不是 access-control boundary。主进程的 URL、route、bearer、size 与 timeout checks 是 service invariants，而不是插件权限。

### 读取与修改插件设置

在 manifest 旁声明 schema，并通过 `settings` 指向它：

```json
{
  "schemaVersion": 1,
  "id": "acme.wallpaper",
  "settings": "./settings.schema.json"
}
```

schema 使用 `fields` 数组。支持的类型是 `text`、`textarea`、`number`、`bool`、`select`、`stringList`、`keyValueMap` 和 `json`。字段 key 不区分大小写且必须唯一，`defaultValue` 必须通过与存储值相同的校验。

```json
{
  "fields": [
    { "key": "fit", "type": "select", "defaultValue": "cover", "options": ["cover", "contain"] },
    { "key": "dim", "type": "number", "defaultValue": 20, "min": 0, "max": 80 }
  ]
}
```

激活时读取一次完整快照，之后只写入有变化的字段。`unset` 会移除所选作用域的覆盖值，并显露下一层值：

```ts
const snapshot = await host.settings.get();
const current = snapshot.value as { fit: string; dim: number };

await host.settings.mutate("personal", [
  { op: "set", key: "fit", value: "contain" },
  { op: "unset", key: "dim" },
]);
```

快照包含 schema、个人层、工作区层、有效值和可写作用域。版本 1 不提供冲突 token，也不提供宿主生成的设置页面。设置文件只能存放小型 JSON 值。图片、SQLite 文件和缓存应由插件后端处理，renderer 插件既拿不到宿主数据目录路径，也没有通用文件 API。

### 跟随设置变化

本插件存储的设置一旦发生变化，`host.settings.onChange` 就会送来一份完整快照：

```ts
host.settings.onChange((snapshot) => {
  applySettings(snapshot.value as WallpaperSettings);
});
```

每发生一次变化它就触发一次，无论是你的插件写的还是别的客户端写的。重复不算变化：与上一次送出的相同的快照会被丢掉，所以你自己那次写入不会再以回声的形式回来一遍。`mutate` 被拒绝时文件没有被改动，因此什么都不会发布，只有那个 reject 会传到你手里——乐观更新的值保留到 promise 结束为止即可。

订阅时它不会触发。激活时用 `get()` 读一次并保存下来，之后交给 `onChange` 更新：

```ts
let settings = normalize((await host.settings.get()).value);

host.settings.onChange((snapshot) => {
  settings = normalize(snapshot.value);
  repaint(settings);
});
```

底层监听由 Desktop 负责：无论一个插件注册了多少个 listener，每次变化只会重新读取一次该插件的配置，所以在多个地方订阅不会有额外开销。只有最新发起的那次读取有资格发布，所以连续快速写入——比如拖动滑块——即使较早的读取最后才返回，也不会把旧值再塞给 listener。不经过这套 API 的写入——例如 Desktop 运行期间手工改 `plugin-config.json`——不会被观察到。

### 响应 theme 与 locale 变化

`host.environment` 读取当前生效的 theme、它的种子与 UI locale。当 React 之外的东西需要跟随它们时——canvas、动态生成的样式表，或者被缓存下来的值——用 `onChange` 订阅：

```ts
host.environment.onChange(({ locale, theme, themeSeed }) => {
  repaintScene(theme, themeSeed.accent);
  relabelScene(locale);
});
```

每次回调都带完整快照，并且只在值真的发生变化时触发。订阅由 generation 持有，会随插件一起被回收。

`themeSeed` 带的是 Desktop 用来派生整套配色的四个值：`surface`、`ink`、`accent`，以及 0-100 的 `contrast`。当你要绘制 CSS 够不到的东西（比如 canvas）时才需要盯着它：用户只换主题色时 `theme` 仍然是 `dark`，光看主题名不足以知道该重绘。凡是能用 CSS 表达的，直接读 token，不要自己再推一遍 ramp。

`locale` 一定是 Desktop 的七个应用 locale 之一——`en`、`zh-Hans`、`ja`、`ko`、`es`、`fr`、`de`，类型是 `DesktopPluginLocale`。Desktop 会先把浏览器语言标签归一化，所以使用 `zh-CN` 或 `en-US` 的用户传到插件里时已经是 `zh-Hans` 或 `en`。文案表按应用 locale 建索引、直接读 `host.environment.locale` 即可，不必自己再写一层按语言回退的逻辑。

底层的监听由 Desktop 负责。插件不需要自己监视 `document.documentElement` 的 `data-theme` 或 `lang`，Desktop 如何感知变化也不属于这份契约。

在 React 树里，用同一个订阅把快照放进 state：

```tsx
import { useEffect, useState } from "react";
import type { DesktopPluginViewProps } from "@dotcraft/plugin";

function useTheme(host: DesktopPluginViewProps["host"]) {
  const [theme, setTheme] = useState(host.environment.theme);
  useEffect(() => {
    setTheme(host.environment.theme);
    return host.environment.onChange((environment) => setTheme(environment.theme));
  }, [host]);
  return theme;
}
```

### 提供 theme 或 backdrop presentation

应用级外观统一通过 `host.appearance` 提供。Theme 插件只覆盖自己负责的 light/dark seed
字段，Core 的 Appearance 设置始终作为基础层：

```ts
host.appearance.setThemeSeedOverride({
  light: { surface: "#f7f2e8", ink: "#2d2924", accent: "#b64b3a" },
  dark: { surface: "#171413", ink: "#f3ece7", accent: "#e26a55" },
});
```

Wallpaper 插件把媒体渲染到 `app.background`，再让 Host 在媒体上为每个 shell 区域只合成
一次表面：

```ts
host.appearance.setBackdropPresentation({ surfaceOpacity: 0.72 });
```

每个插件 generation 在两类 contribution 中各有一个槽位。较晚 activation 的优先级更高，
但不会丢掉前一层；传入 `null`，或者插件被禁用、卸载、热重载、activation 失败时，都会显露
前一层。重复提供相同值不会再次发布 theme 变化。Desktop 会校验 seed 颜色，并限制 contrast
和 opacity。

这些调用不会持久化插件选择。请把所选 theme pack 或 opacity 存在 `host.settings`，activation
时重新应用，效果关闭时传入 `null`。不要设置 Desktop 的私有 CSS 变量，也不要通过 wrap
`app` 来实现全局外观效果。

### 读取当前 session

`host.session` 告诉你 Desktop 此刻在做什么，`onChange` 则跟随它的变化：

```ts
host.session.onChange((session) => {
  repaint(session.busy);
});
```

| 字段 | 含义 |
|---|---|
| `workspacePath` | 前台的 workspace，没有打开任何 workspace 时为 `null`。 |
| `threadId` | 当前 thread，欢迎页上为 `null`。 |
| `mode` | `agent` 或 `plan`。 |
| `busy` | 有一轮正在运行，或正在等待用户输入。 |

`workspacePath` 是前台 workspace，不是当前 thread 的 workspace。正因如此，它在 Settings 页、main view，乃至完全没有组件挂载的 effect 里都能读到——也就是会话面板并不存在的那些地方。它与 `host.workspaces.listLocalProjects()` 里 `active` 的那一项一致。在 Composer surface 内部，`context.workspacePath` 仍然报告该 thread 自己的 workspace，两者可能不同。

审批状态、Composer variant 与 minimal chrome 不在这里。它们描述的是 Composer 如何呈现自己，所以留在 Composer surface context 上。

这四个字段是实时读取的，所以组件应把需要的字段放进 state，而不是持有这个对象：

```tsx
const [busy, setBusy] = useState(host.session.busy);
useEffect(() => {
  setBusy(host.session.busy);
  return host.session.onChange((session) => setBusy(session.busy));
}, [host]);
```

## 使用 UI kit

共享 UI 组件从 `@dotcraft/plugin` 导入，插件页面不必复制 Core 的样式就能和 Desktop 其他部分保持一致。官方 builder 会把 hooks 与 JSX 接到 Desktop 的 React runtime 上。

| 分组 | 组件 |
|---|---|
| **控件** | `Button`、`IconButton`、`Input`、`Textarea`、`Select`、`SegmentedControl`、`Combobox`、`Checkbox`、`PillSwitch`、`Slider` |
| **展示** | `Spinner`、`Skeleton`、`ActionTooltip`、`ModalHeader`、`InlineDiff` |
| **Settings 布局** | `SettingsPanelShell`、`SettingsBreadcrumb`、`SettingsGroup`、`SettingsRow` |

报告所选值的控件——`Select`、`Combobox`、`SegmentedControl`——回调名为 `onValueChange`，无障碍名称来自 `ariaLabel`。布尔开关——`Checkbox`、`PillSwitch`——回调名为 `onChange`。

`Slider` 在数值移动时调用 `onValueChange`，并在指针或键盘交互结束时调用一次可选的
`onValueCommit`。通过 `onValueChange` 预览。如果保存每个中间值会产生 I/O，则通过
`onValueCommit` 持久化。数值需要单位时请提供 `valueText`。需要占据整行宽度的控件使用
`SettingsRow orientation="block"`。插件特有的视觉选择器应放进 block row 或
`SettingsGroup flush`，这样既沿用 Settings 的间距与边框，又能自行组织内部布局。`htmlFor`
用于把行标签关联到原生控件，`align="flex-start"` 则让多行 inline row 顶部对齐。

几个互斥选项能放进一行时用 `SegmentedControl`。选项较多，或者每个选项需要描述与图标时用 `Select`：

```tsx
import { SegmentedControl, SettingsGroup, SettingsRow } from "@dotcraft/plugin";

function DensityRow({
  density,
  onDensityChange,
}: {
  density: "cozy" | "compact";
  onDensityChange: (density: "cozy" | "compact") => void;
}) {
  return (
    <SettingsGroup title="Board">
      <SettingsRow
        label="Density"
        control={
          <SegmentedControl
            value={density}
            options={[
              { value: "cozy", label: "Cozy" },
              { value: "compact", label: "Compact" },
            ]}
            onValueChange={onDensityChange}
            ariaLabel="Board density"
          />
        }
      />
    </SettingsGroup>
  );
}
```

### 请求颜色

不透明 RGB 颜色选择统一使用 `host.ui.pickColor`。compact dialog、portal、焦点锁定、通用文案、
Hex 校验与键盘操作都由 Desktop 管理。它接受三位或六位 Hex，并返回规范化的小写
`#rrggbb`；拖动和输入只在弹窗内部预览。

```ts
const result = await host.ui.pickColor({
  title: "选择工作区颜色",
  description: "用于插件中所有表示当前工作区的位置。",
  initialColor: "#8b5cf6",
  allowReset: true,
  defaultColor: "#4566cc",
});

if (result.kind === "select") await save(result.color);
if (result.kind === "reset") await clearOverride();
```

Done 返回 `select`；Reset 立即返回 `reset` 并关闭。Escape、关闭按钮、遮罩、同时发起的另一个
picker 请求或插件销毁都返回 `cancel`。Host 参数不合法时 Promise 以 `TypeError` 拒绝。
不要渲染原生 `input[type="color"]`，也不要自行维护插件颜色弹窗。

## 使用打包的静态资源

在插件源码里 import 一张图片，拿到的值可以直接使用。Builder 会把它解析成产物文件的 URL，因此在模块顶层、入口 bundle 里、拆分出的 chunk 里都已经是正确的：

```tsx
import scene from "./assets/aurora.svg";

function Background() {
  return <div style={{ backgroundImage: `url("${scene}")` }} />;
}
```

Desktop 通过 `dotcraft-plugin://<id>/<revision>/` 提供插件文件，这个地址在构建时无从得知，所以也没有需要手工修补的地方。再用 `new URL(asset, import.meta.url)` 包一层现在只是多余，而不是错误：仍然这么写的插件重新构建后照常工作，因为被包住的值本身已经是绝对 URL。

Builder 会把 `.gif`、`.jpg`、`.jpeg`、`.png`、`.svg`、`.webp` 打包进 `dist/assets/`。在 CSS 里保持普通的相对写法——`url("./assets/aurora.svg")`——样式表会基于自身地址解析它，而那个地址已经在插件路由之下。

## 使用主题 token

Desktop 的整套配色都由四个种子值派生，下表是插件可以读的那一部分。用它们写样式，你的 UI 就会跟着用户的主题、主题色、背景与对比度走，不需要监听任何东西：

```css
.my-plugin-card {
  background: var(--bg-elevated);
  color: var(--text-primary);
  border: 1px solid var(--border-default);
  border-radius: var(--control-radius-md);
  box-shadow: var(--shadow-level-2);
}
```

| 类别 | Token |
|---|---|
| 表面 | `--bg-primary`、`--bg-secondary`、`--bg-tertiary`、`--bg-active`、`--bg-hover`、`--bg-elevated` |
| 文字 | `--text-primary`、`--text-secondary`、`--text-dimmed`、`--text-tertiary`、`--text-disabled` |
| 边框 | `--border-subtle`、`--border-default`、`--border-active` |
| 主题色 | `--accent`、`--accent-hover`、`--on-accent` |
| 状态 | `--success`、`--warning`、`--error`、`--info`、`--success-bg`、`--warning-bg`、`--error-bg` |
| 层级 | `--shadow-level-1`、`--shadow-level-2`、`--shadow-level-3` |
| 字体 | `--font-ui`、`--font-body`、`--font-mono`、`--type-body-size`、`--type-ui-size`、`--type-secondary-size`、`--type-hint-size`、`--type-heading-size` |
| 形状 | `--control-radius-md`、`--button-height`、`--button-height-sm` |
| 种子 | `--seed-surface`、`--seed-ink`、`--seed-accent`、`--seed-contrast` |

`--on-accent` 是在主题色上仍然清晰的前景色，往主题色填充上放文字请用它，不要自己写白色。`--seed-*` 这四个与 `host.environment.themeSeed` 报的是同一批值，只有在 CSS 之外绘制时才需要读。

其余所有自定义属性都是私有的，包括 `--composer-*`、`--sidebar-*`、`--shell-*`、`--main-surface-*`、`--glass-*`、`--tooltip-*`、`--scrollbar-*`、`--shimmer-*`、`--diff-*` 与 `--ansi-*`。它们会随 Desktop 自身的布局工作变动。

## 有意识地使用 DOM 与 CSS

Desktop Plugin 可以访问 renderer DOM，也可以加载全局 CSS，DotCraft 不会阻止。但 DotCraft 自己的 DOM 结构、class 名、私有 CSS 变量、store 与业务组件都不是公共契约。已有公共 surface 或 service 时优先用它们，直接操作 DOM 或 CSS 的维护成本由插件自己承担。

## 分离 UI 与后端职责

自定义背景、Composer decoration、wrapper、plugin surface、renderer service 或 renderer event 只需要 Desktop Plugin。DotCraft 不会为纯 UI 创建对应的 C# 或 AppServer API。

如果功能需要 backend execution、durable host-owned state、Agent tools 或 hooks、其他客户端，或者跨进程与远程协调，再添加 [.NET plugin](./dotnet-plugins) 或 AppServer contract。一个 plugin bundle 可以同时包含这两种模块，但不会仅因为其中一种存在就要求另一种。

## Generation 与重新加载生命周期

Desktop 把整个 content revision 作为一个 generation 激活，并对它调用一次 `activate`。刷新一个没有变化的 revision 不产生任何效果。更新 revision 时，Desktop 先 dispose 旧 generation，再激活新的。构建与刷新的具体步骤见[开发 Desktop Plugin](./desktop-plugins)。

禁用或替换 revision 时，Desktop 会立即撤销 Host 持有的注册，不会等待插件尚未完成的 `activate()` 或 `dispose()` Promise。迟到的 activation 结果已经过期，不会再发布。

Revision 是开发迭代的单元。Desktop Plugin 不内置 file watcher、HMR、只重载组件或局部更新 generation 的机制。重新构建后，再刷新或重新启用插件。

Desktop 不会从远程 AppServer 加载可执行插件代码。使用远程 workspace 时，它只激活本地已经打包，并且 plugin id、version 与 Desktop content revision 都和远程 snapshot 一致的代码。
