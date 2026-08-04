import type {
  ServerCapabilities,
  ServerInfo,
  SessionThread,
  SessionTurn,
} from "./generated/appserver/index.js";

export function makeThread(
  id: string,
  status: string,
  workspacePath = "",
  userId = "",
  originChannel = "sdk",
): SessionThread {
  const now = "2026-01-01T00:00:00.000Z";
  return {
    id,
    status,
    workspacePath,
    effectiveWorkspacePath: workspacePath,
    cwd: workspacePath,
    userId,
    originChannel,
    createdAt: now,
    lastActiveAt: now,
    ephemeral: false,
    historyMode: "server",
    metadata: {},
    queuedInputs: [],
    runtime: {},
    runtimeWorkspaceRoots: [],
    sessionId: id,
    source: { kind: "test" },
    worktree: null,
  } as SessionThread;
}

export function makeTurn(id: string, threadId: string, status: string): SessionTurn {
  return { id, threadId, status, startedAt: "2026-01-01T00:00:00.000Z" };
}

export function makeServerInfo(name: string, version: string, protocolVersion: string): ServerInfo {
  return { name, version, protocolVersion };
}

export function makeServerCapabilities(): ServerCapabilities {
  return { protocolVersion: "1", version: "1", threadManagement: true, threadSubscriptions: true };
}
