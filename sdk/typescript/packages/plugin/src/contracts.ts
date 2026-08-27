import type {
  AppConnectionStartResult,
  AppConnectionStatusResult,
  ClientRequestMethods,
  ServerNotificationMethods,
} from "@dotcraft/sdk/contracts";
import type { ComponentType, CSSProperties, ExoticComponent } from "react";

export type DesktopPluginDispose = () => void;

export interface DesktopPluginMetadata {
  readonly id: string;
  readonly version: string;
  readonly displayName: string;
}

export interface DesktopPluginEnvironment {
  readonly locale: string;
  readonly theme: "light" | "dark";
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

export interface DesktopPluginUi {
  showToast(options: DesktopPluginToastOptions): DesktopPluginDispose;
  confirm(options: DesktopPluginConfirmOptions): Promise<boolean>;
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
  readonly navigation: DesktopPluginNavigation;
  readonly ui: DesktopPluginUi;
  readonly appServer: DesktopPluginAppServer;
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
export type DesktopPluginContributionIcon = string | DesktopPluginIconComponent;

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

export interface DesktopPluginComposerActionContext {
  readonly workspacePath: string | null;
  readonly threadId: string;
  readonly mode: "agent" | "plan";
  readonly busy: boolean;
  readonly awaitingApproval: boolean;
}

export interface DesktopPluginComposerActionProps extends DesktopPluginViewProps {
  readonly context: DesktopPluginComposerActionContext;
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

export interface DesktopPluginComposerActionContribution {
  readonly id: string;
  readonly label: DesktopLocalizedText;
  readonly icon?: DesktopPluginContributionIcon;
  readonly order?: number;
  readonly isAvailable?: (context: DesktopPluginComposerActionContext) => boolean;
  readonly component: ComponentType<DesktopPluginComposerActionProps>;
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
  readonly composerActions?: readonly DesktopPluginComposerActionContribution[];
  readonly messageActions?: readonly DesktopPluginMessageActionContribution[];
  readonly dispose?: () => void | Promise<void>;
}

export type DesktopPluginActivate = (
  host: DesktopPluginHost,
) => DesktopPluginActivation | Promise<DesktopPluginActivation>;
