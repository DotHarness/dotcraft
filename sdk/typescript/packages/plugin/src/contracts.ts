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
  /**
   * Notifies when the applied theme, its seed, or the UI locale changes, with a complete
   * snapshot. A recolor that leaves the theme name alone still fires.
   * The subscription is generation-owned, like every other Host registration.
   */
  onChange(listener: (environment: DesktopPluginEnvironmentSnapshot) => void): DesktopPluginDispose;
}

export interface DesktopPluginSessionSnapshot {
  /** The foreground workspace, not the active thread's; a Composer surface context reports that one. */
  readonly workspacePath: string | null;
  readonly threadId: string | null;
  readonly mode: "agent" | "plan";
  readonly busy: boolean;
}

export interface DesktopPluginSession extends DesktopPluginSessionSnapshot {
  /**
   * Notifies when the foreground workspace, active thread, mode, or busy state changes, with a
   * complete snapshot. The subscription is generation-owned, like every other Host registration.
   */
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
  readonly reasoningEffort: "off" | "low" | "medium" | "high" | "extraHigh";
  readonly speed: "standard" | "fast";
  readonly contextMax: boolean;
  readonly reducedMotion: boolean;
}

/**
 * Known Desktop surface contexts. Plugins may augment this interface with their own surfaces.
 */
export interface DesktopPluginSurfaceContextMap {
  readonly app: DesktopPluginAppSurfaceContext;
  readonly "app.background": DesktopPluginAppSurfaceContext;
  readonly "app.overlay": DesktopPluginAppSurfaceContext;
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

export interface DesktopPluginNavigation {
  openMainView(contributionId: string): void;
  openSettingsPage(contributionId: string): void;
  openThread(threadId: string, workspacePath?: string): Promise<void>;
  onOpenUrl(listener: (url: string) => boolean): DesktopPluginDispose;
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

export interface DesktopPluginUi {
  showToast(options: DesktopPluginToastOptions): DesktopPluginDispose;
  confirm(options: DesktopPluginConfirmOptions): Promise<boolean>;
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
  /**
   * Notifies once per change to the stored configuration, whether this plugin wrote it or another
   * client did. A rejected `mutate` leaves the file untouched, so it rethrows without notifying.
   * Repeats are not events: a snapshot equal to the last delivered one is dropped, and so is a read
   * that resolves after a newer one was issued. Never fires on subscribe. The subscription is
   * generation-owned, like every other Host registration.
   */
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
  readonly session: DesktopPluginSession;
  effect(setup: DesktopPluginEffectSetup): DesktopPluginDispose;
  readonly services: DesktopPluginServices;
  readonly events: DesktopPluginEvents;
  readonly navigation: DesktopPluginNavigation;
  readonly ui: DesktopPluginUi;
  readonly appServer: DesktopPluginAppServer;
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
