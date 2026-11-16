import type { RemoteToolHostInfo, RemoteToolRouteInfo } from "@dotcraft/sdk/contracts";
import type { DesktopPluginDispose } from "./contracts.js";

export interface DesktopPluginSatelliteWorkspace {
  workspaceId: string;
  path: string;
  busy: boolean;
  busyOwner?: string;
  leaseExpiresAt?: string;
}

export interface DesktopPluginSatellite {
  peerId: string;
  hostId: string;
  displayName: string;
  userName?: string;
  osName?: string;
  connected: boolean;
  enrolledAt?: string;
  lastSeenAt?: string;
  inviteId?: string;
  workspaces: DesktopPluginSatelliteWorkspace[];
  activeLease?: { workspaceId: string; owner?: string; expiresAt?: string };
  capabilities: string[];
}

export interface DesktopPluginSatelliteInvite {
  inviteId: string;
  url: string;
  expiresAt: string;
}

export interface DesktopPluginSatelliteEvent {
  kind: "joined" | "online" | "offline" | "revoked";
  at: string;
  peerId: string;
  inviteId?: string;
  satellite?: DesktopPluginSatellite;
}

export interface DesktopPluginSharePcStatus {
  installed: boolean;
  peers: {
    authorizationMode?: "workspacePreferred" | "fullAccess";
    peerId: string;
    hubLabel: string;
    folderPath?: string;
    pairedAt?: string;
  }[];
}

export interface DesktopPluginSatellites {
  list(): Promise<{ supported: boolean; satellites: DesktopPluginSatellite[]; error?: string }>;
  createInvite(): Promise<DesktopPluginSatelliteInvite>;
  revoke(peerId: string): Promise<unknown>;
  activity(peerId?: string): Promise<DesktopPluginSatelliteEvent[]>;
  shareStatus(): Promise<DesktopPluginSharePcStatus>;
  wasInvitedHere(inviteId: string): Promise<boolean>;
  onEvent(listener: (event: DesktopPluginSatelliteEvent) => void): DesktopPluginDispose;
}

export interface DesktopPluginPendingRoute {
  hostId: string;
  workspaceId: string;
}

export interface DesktopPluginRemoteToolHostSnapshot {
  supported: boolean;
  remote: boolean;
  hosts: RemoteToolHostInfo[];
  routes: Record<string, RemoteToolRouteInfo>;
  pendingRoute: DesktopPluginPendingRoute | null;
  deferredRoutes: Record<string, DesktopPluginPendingRoute | null>;
  connecting: string | null;
}

export interface DesktopPluginRemoteToolHost {
  getSnapshot(): DesktopPluginRemoteToolHostSnapshot;
  onChange(listener: (snapshot: DesktopPluginRemoteToolHostSnapshot) => void): DesktopPluginDispose;
  list(threadId?: string): Promise<void>;
  connect(threadId: string, hostId: string, workspaceId: string): Promise<void>;
  disconnect(threadId: string): Promise<void>;
  setPendingRoute(route: DesktopPluginPendingRoute | null): void;
  deferRoute(threadId: string, route: DesktopPluginPendingRoute | null): void;
  clearDeferredRoute(threadId: string): void;
  applyDeferredRoute(threadId: string): Promise<void>;
  maybeReapply(threadId: string, options?: { turnRunning?: boolean }): void;
}

export interface DesktopPluginScreenStatus {
  kind: "connecting" | "live" | "stalled" | "reconnecting" | "paused" | "offline" | "unavailable";
  reason?: "noCaptureBackend" | "noInteractiveSession" | "noDisplayServer" | "captureFailed" | "hubRejected";
  detail?: string;
  needsAuthorization?: boolean;
}

export interface DesktopPluginScreenFrame {
  viewId: string;
  sequence: number;
  width: number;
  height: number;
  capturedAtUnixMs: number;
  jpeg: ArrayBuffer;
}

export interface DesktopPluginScreenSession {
  readonly viewId: string;
  onState(listener: (status: DesktopPluginScreenStatus) => void): DesktopPluginDispose;
  onFrame(listener: (frame: DesktopPluginScreenFrame) => void): DesktopPluginDispose;
  tune(maxWidth: number): Promise<void>;
  ack(sequence: number): void;
  close(): void;
}

export interface DesktopPluginScreenLayout {
  width?: number;
  position?: { x: number; y: number };
}

export interface DesktopPluginScreenView {
  open(request: { peerId: string; maxWidth: number }): DesktopPluginScreenSession;
  getLayout(): Promise<DesktopPluginScreenLayout>;
  setLayout(layout: DesktopPluginScreenLayout): Promise<void>;
}

export interface DesktopPluginRemoteTransferProgress {
  kind: "remoteFileTransfer";
  stage: "preparing" | "transferring";
  direction: "upload" | "download";
  localPath: string;
  remotePath: string;
  hostId: string;
  hostDisplayName: string;
  transferredBytes: number;
  completedFiles: number;
  completedBytes: number;
  totalBytes?: number;
  totalFiles?: number;
  currentFile?: string;
}
