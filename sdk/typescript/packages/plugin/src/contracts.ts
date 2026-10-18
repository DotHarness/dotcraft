import type {
  AppConnectionStartResult,
  AppConnectionStatusResult,
  ClientRequestMethods,
  ServerNotificationMethods,
} from "@dotcraft/sdk/contracts";
import type { ComponentType, CSSProperties, ExoticComponent, ReactNode } from "react";

export type DesktopPluginDispose = () => void;

export interface DesktopPluginMetadata {
  readonly id: string;
  readonly version: string;
  readonly displayName: string;
}

/** The app locales Desktop resolves to. A browser tag such as `zh-CN` normalizes into this set. */
export type DesktopPluginLocale = "en" | "zh-Hans" | "ja" | "ko" | "es" | "fr" | "de";

/** The four values Desktop derives its palette from; see the theme token contract. */
export interface DesktopPluginThemeSeed {
  /** The base plane: the page in dark, the card in light. */
  readonly surface: string;
  readonly ink: string;
  readonly accent: string;
  /** 0-100. */
  readonly contrast: number;
}

export interface DesktopPluginEnvironmentSnapshot {
  readonly locale: DesktopPluginLocale;
  readonly theme: "light" | "dark";
  readonly themeSeed: DesktopPluginThemeSeed;
}

export interface DesktopPluginEnvironment extends DesktopPluginEnvironmentSnapshot {
  /** Notifies when the applied theme, seed, or locale changes; disposed with the plugin generation. */
  onChange(listener: (environment: DesktopPluginEnvironmentSnapshot) => void): DesktopPluginDispose;
}

export type DesktopPluginThemeSeedOverrides = Partial<
  Record<"light" | "dark", Partial<DesktopPluginThemeSeed>>
>;

export interface DesktopPluginBackdropPresentation {
  /** Opacity of each Host-owned shell surface, from 0 (clear) to 1 (opaque). */
  readonly surfaceOpacity: number;
}

export interface DesktopPluginAppearance {
  /** Replaces this plugin generation's theme layer. Pass null to reveal the layer below it. */
  setThemeSeedOverride(value: DesktopPluginThemeSeedOverrides | null): void;
  /** Replaces this plugin generation's backdrop presentation. Pass null when no media is active. */
  setBackdropPresentation(value: DesktopPluginBackdropPresentation | null): void;
}

export interface DesktopPluginSessionSnapshot {
  /** The foreground workspace, not the active thread's; a Composer surface context reports that one. */
  readonly workspacePath: string | null;
  readonly threadId: string | null;
  readonly mode: "agent" | "plan";
  readonly busy: boolean;
}

export interface DesktopPluginSession extends DesktopPluginSessionSnapshot {
  /** Notifies when workspace, thread, mode, or busy state changes; disposed with the plugin generation. */
  onChange(listener: (session: DesktopPluginSessionSnapshot) => void): DesktopPluginDispose;
}

export interface DesktopPluginAppSurfaceContext {
  readonly rootElement: HTMLElement;
}

export interface DesktopPluginComposerSurfaceContext {
  readonly workspacePath: string | null;
  readonly threadId: string | null;
  readonly mode: "agent" | "plan";
  readonly busy: boolean;
  readonly awaitingApproval: boolean;
  readonly variant: "default" | "agentBuilder";
  readonly minimalChrome: boolean;
}

export type DesktopPluginMascotActivity =
  | "idle"
  | "focused"
  | "dragging"
  | "working"
  | "decision"
  | "success"
  | "error"
  | "sleeping";

export interface DesktopPluginComposerMascotSurfaceContext
  extends DesktopPluginComposerSurfaceContext {
  readonly size: number;
  readonly activity: DesktopPluginMascotActivity;
  readonly expression: "neutral" | "happy" | "operator" | "sleep";
  readonly light: "default" | "success" | "error";
  readonly submitRevision: number;
  readonly reasoningEffort: "off" | "low" | "medium" | "high" | "extraHigh" | "max";
  readonly speed: "standard" | "fast";
  readonly reducedMotion: boolean;
}

export interface DesktopPluginThreadSurfaceContext {
  readonly workspacePath: string | null;
  readonly threadId: string;
  readonly busy: boolean;
}

export type DesktopPluginConversationAsideLayout = "gutter" | "shift" | "overlay";

export interface DesktopPluginConversationAsideContext extends DesktopPluginThreadSurfaceContext {
  readonly layout: DesktopPluginConversationAsideLayout;
  readonly width: number;
  /** Holds room beside the conversation while live; only the trailing seat moves the column. */
  pin(): DesktopPluginDispose;
}

/**
 * Known Desktop surface contexts. Plugins may augment this interface with their own surfaces.
 */
export interface DesktopPluginSurfaceContextMap {
  readonly app: DesktopPluginAppSurfaceContext;
  readonly "app.background": DesktopPluginAppSurfaceContext;
  readonly "app.overlay": DesktopPluginAppSurfaceContext;
  readonly "app.status": DesktopPluginAppSurfaceContext;
  readonly composer: DesktopPluginComposerSurfaceContext;
  readonly "composer.mascot": DesktopPluginComposerMascotSurfaceContext;
  readonly "composer.before": DesktopPluginComposerSurfaceContext;
  readonly "composer.after": DesktopPluginComposerSurfaceContext;
  readonly "composer.input": DesktopPluginComposerSurfaceContext;
  readonly "composer.input.attachments": DesktopPluginComposerSurfaceContext;
  readonly "composer.input.editor": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.leading": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.trailing": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.commands": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.permissions": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.mode": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.goal": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.context-usage": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.model": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.voice": DesktopPluginComposerSurfaceContext;
  readonly "composer.toolbar.submit": DesktopPluginComposerSurfaceContext;
  readonly "composer.status": DesktopPluginComposerSurfaceContext;
  readonly "composer.status.workspace": DesktopPluginComposerSurfaceContext;
  readonly "composer.status.subscription": DesktopPluginComposerSurfaceContext;
  readonly "composer.status.trailing": DesktopPluginComposerSurfaceContext;
  readonly "thread.header.actions": DesktopPluginThreadSurfaceContext;
  readonly "conversation.aside.leading": DesktopPluginConversationAsideContext;
  readonly "conversation.aside.trailing": DesktopPluginConversationAsideContext;
}

export type DesktopPluginSurfaceContext<Surface extends string> =
  Surface extends keyof DesktopPluginSurfaceContextMap
    ? DesktopPluginSurfaceContextMap[Surface]
    : unknown;

export interface DesktopPluginSurfaceProps<Surface extends string = string> {
  readonly host: DesktopPluginHost;
  readonly context: DesktopPluginSurfaceContext<Surface>;
}

export interface DesktopPluginSurfaceWrapperProps<Surface extends string = string>
  extends DesktopPluginSurfaceProps<Surface> {
  readonly children: ReactNode;
}

export type DesktopPluginSurfaceComponent<Surface extends string = string> =
  ComponentType<DesktopPluginSurfaceProps<Surface>>;

export type DesktopPluginSurfaceWrapper<Surface extends string = string> =
  ComponentType<DesktopPluginSurfaceWrapperProps<Surface>>;

export type DesktopPluginEffectSetup = () => void | DesktopPluginDispose;

export interface DesktopPluginServices {
  provide<T>(id: string, service: T): DesktopPluginDispose;
  use<T>(id: string): T | undefined;
}

export interface DesktopPluginEvents {
  on<T = unknown>(event: string, listener: (payload: T) => void): DesktopPluginDispose;
  emit<T = unknown>(event: string, payload: T): void;
}

export type DesktopPluginDetailPanelTab = "changes" | "plan" | "subagents";

export interface DesktopPluginOpenTarget {
  id: string;
  label: string;
  /** Image data URL of the app's icon, when one is available. */
  icon?: string;
}

export interface DesktopPluginOpenPathOptions {
  target?: string;
  line?: number;
  column?: number;
}

export interface DesktopPluginNavigation {
  openMainView(contributionId: string): void;
  openSettingsPage(contributionId: string): void;
  openThread(threadId: string, workspacePath?: string): Promise<void>;
  /** Opens an http(s) URL in the user's default browser and handles a `dotcraft:` link inside Desktop; rejects other schemes. */
  openExternal(url: string): Promise<void>;
  /** Opens a workspace file in the Detail Panel file viewer; a relative path resolves against the current thread's workspace. */
  openFile(path: string): Promise<void>;
  /** Opens a local path in another app; rejects on a remote workspace or when the target is unavailable. */
  openPath(path: string, options?: DesktopPluginOpenPathOptions): Promise<void>;
  listOpenTargets(): Promise<DesktopPluginOpenTarget[]>;
  openDetailPanel(tab: DesktopPluginDetailPanelTab): void;
  openAutomation(automationId: string): void;
  /** Opens the thread and reveals the tool item with this call id in its conversation. */
  revealToolCall(threadId: string, callId: string): void;
  onOpenUrl(listener: (url: string) => boolean): DesktopPluginDispose;
}

export type DesktopPluginSubAgentState = "working" | "waiting" | "done" | "failed" | "cancelled";

export interface DesktopPluginSubAgent {
  readonly parentThreadId: string;
  readonly childThreadId: string;
  readonly agentPath: string | null;
  readonly nickname: string;
  readonly state: DesktopPluginSubAgentState;
}

export interface DesktopPluginSubAgents {
  list(parentThreadId: string): readonly DesktopPluginSubAgent[];
  /** Notifies with the complete list when a child's identity or state changes; never fires on subscribe. */
  onChange(
    parentThreadId: string,
    listener: (subagents: readonly DesktopPluginSubAgent[]) => void,
  ): DesktopPluginDispose;
  reveal(parentThreadId: string, childThreadId: string): void;
}

export type DesktopPluginProjectActionIcon = "tool" | "run" | "debug" | "test";

export interface DesktopPluginProjectAction {
  readonly id: string;
  readonly name: string;
  readonly icon: DesktopPluginProjectActionIcon;
  readonly command: string;
}

export interface DesktopPluginProjectEnvironment {
  readonly id: string;
  readonly name: string;
  readonly path: string;
  readonly isDefault: boolean;
  /** Why the environment needs attention; it then has no actions. */
  readonly error?: string;
  /** Actions for this platform, most recently run first. */
  readonly actions: readonly DesktopPluginProjectAction[];
}

export interface DesktopPluginProjectActionsSnapshot {
  readonly environments: readonly DesktopPluginProjectEnvironment[];
  readonly selectedEnvironmentId: string | null;
}

export interface DesktopPluginSavedProjectAction {
  readonly environmentId: string;
  readonly action: DesktopPluginProjectAction;
}

export interface DesktopPluginProjectActions {
  /** Rejects in a remote workspace. */
  list(workspacePath: string): Promise<DesktopPluginProjectActionsSnapshot>;
  onChange(
    workspacePath: string,
    listener: (snapshot: DesktopPluginProjectActionsSnapshot) => void,
  ): DesktopPluginDispose;
  select(workspacePath: string, environmentId: string): Promise<void>;
  run(request: { threadId: string; environmentId: string; actionId: string }): Promise<void>;
  openTerminal(request: { threadId: string }): Promise<void>;
  /** Resolves to `null` when the user cancels or the saved action does not run on this platform. */
  promptAddAction(request: {
    workspacePath: string;
    environmentId?: string;
  }): Promise<DesktopPluginSavedProjectAction | null>;
}

export interface DesktopPluginToastOptions {
  readonly message: string;
  readonly tone?: "neutral" | "success" | "warning" | "error";
  readonly durationMs?: number;
  readonly action?: {
    readonly label: string;
    readonly run: () => void;
  };
}

export interface DesktopPluginAddOptions {
  /** Ascending; defaults to 100. Additions sharing an order keep registration order. */
  readonly order?: number;
}

export interface DesktopPluginColorPickerBaseOptions {
  readonly title: string;
  readonly description?: string;
  readonly initialColor: string;
}

export type DesktopPluginColorPickerOptions =
  | (DesktopPluginColorPickerBaseOptions & {
      readonly allowReset: true;
      readonly defaultColor: string;
    })
  | (DesktopPluginColorPickerBaseOptions & {
      readonly allowReset?: false;
      readonly defaultColor?: never;
    });

export type DesktopPluginColorPickerResult =
  | { readonly kind: "select"; readonly color: string }
  | { readonly kind: "reset" }
  | { readonly kind: "cancel" };

export interface DesktopPluginFolderPickerOptions {
  readonly title: string;
}

export interface DesktopPluginUi {
  showToast(options: DesktopPluginToastOptions): DesktopPluginDispose;
  confirm(options: DesktopPluginConfirmOptions): Promise<boolean>;
  pickColor(options: DesktopPluginColorPickerOptions): Promise<DesktopPluginColorPickerResult>;
  /** Resolves to the chosen absolute path on the Desktop machine, or `null` when cancelled. */
  pickFolder(options: DesktopPluginFolderPickerOptions): Promise<string | null>;
  add<Surface extends string>(
    surface: Surface,
    component: DesktopPluginSurfaceComponent<Surface>,
    options?: DesktopPluginAddOptions,
  ): DesktopPluginDispose;
  replace<Surface extends string>(
    surface: Surface,
    component: DesktopPluginSurfaceComponent<Surface>,
  ): DesktopPluginDispose;
  wrap<Surface extends string>(
    surface: Surface,
    wrapper: DesktopPluginSurfaceWrapper<Surface>,
  ): DesktopPluginDispose;
}

export interface DesktopPluginConfirmOptions {
  readonly title: string;
  readonly message: string;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
  readonly danger?: boolean;
}

export interface DesktopPluginAppServer {
  request<M extends keyof ClientRequestMethods>(
    method: M,
    params: ClientRequestMethods[M]["params"],
    timeoutMs?: number,
  ): Promise<ClientRequestMethods[M]["result"]>;

  onNotification<M extends keyof ServerNotificationMethods>(
    method: M,
    listener: (params: ServerNotificationMethods[M]["params"]) => void,
  ): DesktopPluginDispose;
}

export type DesktopPluginSettingType =
  | "text"
  | "textarea"
  | "number"
  | "bool"
  | "select"
  | "stringList"
  | "keyValueMap"
  | "json";

export interface DesktopPluginSettingField {
  readonly key: string;
  readonly type: DesktopPluginSettingType;
  readonly defaultValue?: unknown;
  readonly options?: readonly string[];
  readonly min?: number;
  readonly max?: number;
}

export interface DesktopPluginSettingsSchema {
  readonly fields: readonly DesktopPluginSettingField[];
}

export type DesktopPluginSettingsScope = "personal" | "workspace";

export type DesktopPluginSettingsMutation =
  | Readonly<{ op: "set"; key: string; value: unknown }>
  | Readonly<{ op: "unset"; key: string }>;

export interface DesktopPluginSettingsSnapshot<TValue = Record<string, unknown>> {
  readonly schema: DesktopPluginSettingsSchema;
  readonly personal: Partial<TValue>;
  readonly workspace: Partial<TValue>;
  readonly value: TValue;
  readonly writableScopes: readonly DesktopPluginSettingsScope[];
}

export interface DesktopPluginSettings {
  get<TValue = Record<string, unknown>>(): Promise<DesktopPluginSettingsSnapshot<TValue>>;
  mutate<TValue = Record<string, unknown>>(
    scope: DesktopPluginSettingsScope,
    operations: readonly DesktopPluginSettingsMutation[]
  ): Promise<DesktopPluginSettingsSnapshot<TValue>>;
  /** Notifies on new stored snapshots; skips repeats and stale reads, and never fires on subscribe. */
  onChange<TValue = Record<string, unknown>>(
    listener: (settings: DesktopPluginSettingsSnapshot<TValue>) => void,
  ): DesktopPluginDispose;
}

export interface DesktopPluginAppBindings {
  getConnectionStatus(appId: string): Promise<AppConnectionStatusResult>;
  startConnection(appId: string): Promise<AppConnectionStartResult>;
  openNativeApp(appId: string, url: string): Promise<void>;
}

export interface DesktopPluginAppSurfaces {
  getJson<T = unknown>(appId: string, surfaceId: string, relativePath: string, timeoutMs?: number): Promise<T>;
  postJson<T = unknown>(appId: string, surfaceId: string, relativePath: string, body: unknown, timeoutMs?: number): Promise<T>;
}
export interface DesktopPluginMcpToolCall {
  readonly server: string;
  readonly tool: string;
  readonly arguments?: Readonly<Record<string, unknown>>;
  /** Without a thread, the call runs on a shared hidden thread that denies approval-gated tools. */
  readonly threadId?: string;
}

export interface DesktopPluginMcpToolResult {
  readonly content: readonly unknown[];
  readonly structuredContent?: unknown;
  readonly isError?: boolean;
  readonly _meta?: Readonly<Record<string, unknown>>;
}

export interface DesktopPluginMcp {
  callTool(call: DesktopPluginMcpToolCall): Promise<DesktopPluginMcpToolResult>;
}

export interface DesktopPluginLocalProject {
  readonly path: string;
  readonly name: string;
  readonly active: boolean;
}

export interface DesktopPluginWorkspaceReader {
  listLocalProjects(): Promise<readonly DesktopPluginLocalProject[]>;
}

export interface DesktopPluginOratorioContext {
  readonly provider: "local" | "remote";
  readonly workspacePath: string | null;
  readonly connected: boolean;
  readonly revision: number;
}

export interface DesktopPluginOratorioRequest {
  readonly method: "GET" | "POST" | "PUT" | "PATCH";
  readonly path: string;
  readonly body?: Readonly<Record<string, unknown>>;
}

export interface DesktopPluginOratorioResponse<T = unknown> {
  readonly status: number;
  readonly data: T;
}

export interface DesktopPluginOratorioHandoffRequest {
  readonly requestId: string;
  readonly operation: "connect" | "bind";
  readonly appId: string;
  readonly workspacePath: string;
  readonly summary: string;
}

export interface DesktopPluginOratorioBoardEvent {
  readonly type: string;
  readonly taskId?: string;
  readonly shortId?: string;
  readonly runId?: string;
  readonly taskStatus?: string;
  readonly microStatus?: string;
  readonly boardSortOrder?: number;
  readonly ts?: string;
  readonly payload?: Readonly<{
    type?: string;
    status?: string;
    text?: string;
  }>;
}

export interface DesktopPluginOratorioEvent {
  readonly type: "context-changed" | "data-changed" | "board-event" | "handoff-requested";
  readonly revision: number;
  readonly event?: DesktopPluginOratorioBoardEvent;
  readonly handoff?: DesktopPluginOratorioHandoffRequest;
}

export interface DesktopPluginOratorio {
  getContext(): Promise<DesktopPluginOratorioContext>;
  request<T = unknown>(request: DesktopPluginOratorioRequest): Promise<DesktopPluginOratorioResponse<T>>;
  retry(): Promise<DesktopPluginOratorioContext>;
  getPendingHandoff(): Promise<DesktopPluginOratorioHandoffRequest | null>;
  resolveHandoff(requestId: string, approved: boolean): Promise<void>;
  focusRun(runId: string | null): Promise<void>;
  onEvent(listener: (event: DesktopPluginOratorioEvent) => void): DesktopPluginDispose;
}

export interface DesktopPluginHost {
  readonly plugin: DesktopPluginMetadata;
  readonly environment: DesktopPluginEnvironment;
  readonly appearance: DesktopPluginAppearance;
  readonly session: DesktopPluginSession;
  readonly subagents: DesktopPluginSubAgents;
  readonly projectActions: DesktopPluginProjectActions;
  effect(setup: DesktopPluginEffectSetup): DesktopPluginDispose;
  readonly services: DesktopPluginServices;
  readonly events: DesktopPluginEvents;
  readonly navigation: DesktopPluginNavigation;
  readonly ui: DesktopPluginUi;
  readonly appServer: DesktopPluginAppServer;
  readonly mcp: DesktopPluginMcp;
  readonly settings: DesktopPluginSettings;
  readonly appBindings: DesktopPluginAppBindings;
  readonly appSurfaces: DesktopPluginAppSurfaces;
  readonly workspaces: DesktopPluginWorkspaceReader;
  readonly oratorio: DesktopPluginOratorio;
}

export interface DesktopLocalizedText {
  readonly default: string;
  readonly translations?: Readonly<Record<string, string>>;
}

export interface DesktopPluginIconProps {
  readonly size?: number | string;
  readonly strokeWidth?: number | string;
  readonly "aria-hidden"?: boolean | "true" | "false";
  readonly style?: CSSProperties;
}

export type DesktopPluginIconComponent =
  | ComponentType<DesktopPluginIconProps>
  | ExoticComponent<DesktopPluginIconProps>;

export type DesktopPluginContributionIcon = DesktopPluginIconComponent;

export interface DesktopPluginViewProps {
  readonly host: DesktopPluginHost;
  readonly contributionId: string;
}

export interface DesktopPluginConversationViewProps extends DesktopPluginViewProps {
  readonly threadId: string;
}

export interface DesktopPluginCommandContext {
  readonly workspacePath: string | null;
  readonly threadId: string | null;
  readonly viewId: string;
}

export interface DesktopPluginAssistantMessageModel {
  readonly id: string;
  readonly threadId: string;
  readonly turnId: string;
  readonly text: string;
  readonly createdAt?: string;
}

export interface DesktopPluginToolPresentationModel {
  readonly id: string;
  readonly threadId: string | null;
  readonly turnId: string;
  readonly presentationId: string;
  readonly options: Readonly<Record<string, unknown>>;
  readonly toolName: string;
  readonly status: "running" | "completed";
  readonly arguments?: Readonly<Record<string, unknown>>;
  readonly result?: string;
  readonly success?: boolean;
  readonly createdAt: string;
  readonly completedAt?: string;
}

export interface DesktopPluginToolRendererProps extends DesktopPluginViewProps {
  readonly presentation: DesktopPluginToolPresentationModel;
}

interface DesktopPluginViewContribution {
  readonly id: string;
  readonly label: DesktopLocalizedText;
  readonly icon?: DesktopPluginContributionIcon;
  readonly order?: number;
  readonly component: ComponentType<DesktopPluginViewProps>;
}

export type DesktopPluginMainViewContribution = DesktopPluginViewContribution;

export type DesktopPluginSettingsPageContribution = DesktopPluginViewContribution;

export interface DesktopPluginConversationViewContribution {
  readonly id: string;
  readonly label: DesktopLocalizedText;
  readonly icon?: DesktopPluginContributionIcon;
  readonly order?: number;
  readonly component: ComponentType<DesktopPluginConversationViewProps>;
}

export interface DesktopPluginCommandContribution {
  readonly id: string;
  readonly label: DesktopLocalizedText;
  readonly description?: DesktopLocalizedText;
  readonly icon?: DesktopPluginContributionIcon;
  readonly order?: number;
  readonly isAvailable?: (context: DesktopPluginCommandContext) => boolean;
  readonly execute: (
    context: DesktopPluginCommandContext,
    host: DesktopPluginHost,
  ) => void | Promise<void>;
}

export interface DesktopPluginToolRendererContribution {
  readonly id: string;
  readonly presentationId: string;
  readonly priority?: number;
  readonly component: ComponentType<DesktopPluginToolRendererProps>;
}

export interface DesktopPluginMessageActionContribution {
  readonly id: string;
  readonly label: DesktopLocalizedText;
  readonly icon?: DesktopPluginContributionIcon;
  readonly order?: number;
  readonly isAvailable?: (message: DesktopPluginAssistantMessageModel) => boolean;
  readonly execute: (
    message: DesktopPluginAssistantMessageModel,
    host: DesktopPluginHost,
  ) => void | Promise<void>;
}

export interface DesktopPluginActivation {
  readonly mainViews?: readonly DesktopPluginMainViewContribution[];
  readonly settingsPages?: readonly DesktopPluginSettingsPageContribution[];
  readonly conversationViews?: readonly DesktopPluginConversationViewContribution[];
  readonly commands?: readonly DesktopPluginCommandContribution[];
  readonly toolRenderers?: readonly DesktopPluginToolRendererContribution[];
  readonly messageActions?: readonly DesktopPluginMessageActionContribution[];
  readonly dispose?: () => void | Promise<void>;
}

export type DesktopPluginActivate = (
  host: DesktopPluginHost,
) => DesktopPluginActivation | void | Promise<DesktopPluginActivation | void>;
