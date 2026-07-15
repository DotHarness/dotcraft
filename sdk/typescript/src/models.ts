/**
 * Wire DTO models for the DotCraft AppServer Wire Protocol.
 */

/** Parsed JSON-RPC 2.0 message. */
export class JsonRpcMessage {
  method?: string | null;
  id?: number | string | null;
  params?: Record<string, unknown> | null;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  result?: any;
  error?: { code?: number; message?: string; data?: unknown } | null;

  constructor(init?: Partial<JsonRpcMessage>) {
    if (init) Object.assign(this, init);
  }

  get isRequest(): boolean {
    return this.id != null && this.method != null;
  }

  get isNotification(): boolean {
    return this.id == null && this.method != null;
  }

  get isResponse(): boolean {
    return this.id != null && this.method == null;
  }

  static fromDict(data: Record<string, unknown>): JsonRpcMessage {
    return new JsonRpcMessage({
      method: data.method as string | undefined,
      id: data.id as number | string | undefined,
      params: (data.params as Record<string, unknown>) ?? null,
      result: data.result,
      error: data.error as JsonRpcMessage["error"],
    });
  }

  toDict(): Record<string, unknown> {
    const out: Record<string, unknown> = { jsonrpc: "2.0" };
    if (this.id != null) out.id = this.id;
    if (this.method != null && this.method !== undefined) out.method = this.method;
    if (this.params != null) out.params = this.params;
    if (this.result !== undefined) out.result = this.result;
    if (this.error != null) out.error = this.error;
    return out;
  }
}

export interface SessionIdentityWire {
  channelName: string;
  userId: string;
  workspacePath?: string;
  channelContext?: string;
}

export class Thread {
  constructor(
    public id: string,
    public status: string,
    public workspacePath = "",
    public userId = "",
    public originChannel = "",
    public displayName: string | null = null,
    public createdAt = "",
    public lastActiveAt = "",
    public metadata: Record<string, unknown> = {},
    public turns: unknown[] = [],
  ) {}

  static fromWire(data: Record<string, unknown>): Thread {
    return new Thread(
      String(data.id ?? ""),
      String(data.status ?? ""),
      String(data.workspacePath ?? ""),
      String(data.userId ?? ""),
      String(data.originChannel ?? ""),
      (data.displayName as string) ?? null,
      String(data.createdAt ?? ""),
      String(data.lastActiveAt ?? ""),
      (data.metadata as Record<string, unknown>) ?? {},
      (data.turns as unknown[]) ?? [],
    );
  }
}

export class Turn {
  constructor(
    public id: string,
    public threadId: string,
    public status: string,
    public items: unknown[] = [],
    public startedAt = "",
    public completedAt = "",
    public tokenUsage: Record<string, unknown> | null = null,
    public error: string | null = null,
  ) {}

  static fromWire(data: Record<string, unknown>): Turn {
    return new Turn(
      String(data.id ?? ""),
      String(data.threadId ?? ""),
      String(data.status ?? ""),
      (data.items as unknown[]) ?? [],
      String(data.startedAt ?? ""),
      String(data.completedAt ?? ""),
      (data.tokenUsage as Record<string, unknown>) ?? null,
      (data.error as string) ?? null,
    );
  }
}

export class ServerInfo {
  constructor(
    public name: string,
    public version: string,
    public protocolVersion: string,
    public extensions: string[] = [],
  ) {}

  static fromWire(data: Record<string, unknown>): ServerInfo {
    return new ServerInfo(
      String(data.name ?? ""),
      String(data.version ?? ""),
      String(data.protocolVersion ?? ""),
      (data.extensions as string[]) ?? [],
    );
  }
}

export class ServerCapabilities {
  constructor(
    public threadManagement = false,
    public threadSubscriptions = false,
    public threadGoals = false,
    public manualCompaction = false,
    public manualMemoryConsolidation = false,
    public dynamicToolRebind = false,
    public threadMaintenanceInterrupt = false,
    public approvalFlow = false,
    public requestUserInput = false,
    public modeSwitch = false,
    public configOverride = false,
    public backgroundTerminals = false,
    public cronManagement = false,
    public heartbeatManagement = false,
    public skillsManagement = false,
    public pluginManagement = false,
    public skillVariants = false,
    public commandManagement = false,
    public automations = false,
    public channelStatus = false,
    public modelCatalogManagement = false,
    public providerManagement = false,
    public workspaceConfigManagement = false,
    public memoryManagement = false,
    public dreams = false,
    public mcpManagement = false,
    public mcpServerOrigins = false,
    public externalChannelManagement = false,
    public subAgentManagement = false,
    public subAgentSessions = false,
    public mcpStatus = false,
    public appBindingVersion = 0,
    public extensions: Record<string, unknown> | null = null,
  ) {}

  static fromWire(data: Record<string, unknown>): ServerCapabilities {
    return new ServerCapabilities(
      Boolean(data.threadManagement),
      Boolean(data.threadSubscriptions),
      Boolean(data.threadGoals),
      Boolean(data.manualCompaction),
      Boolean(data.manualMemoryConsolidation),
      Boolean(data.dynamicToolRebind),
      Boolean(data.threadMaintenanceInterrupt),
      Boolean(data.approvalFlow),
      Boolean(data.requestUserInput),
      Boolean(data.modeSwitch),
      Boolean(data.configOverride),
      Boolean(data.backgroundTerminals),
      Boolean(data.cronManagement),
      Boolean(data.heartbeatManagement),
      Boolean(data.skillsManagement),
      Boolean(data.pluginManagement),
      Boolean(data.skillVariants),
      Boolean(data.commandManagement),
      Boolean(data.automations),
      Boolean(data.channelStatus),
      Boolean(data.modelCatalogManagement),
      Boolean(data.providerManagement),
      Boolean(data.workspaceConfigManagement),
      Boolean(data.memoryManagement),
      Boolean(data.dreams),
      Boolean(data.mcpManagement),
      Boolean(data.mcpServerOrigins),
      Boolean(data.externalChannelManagement),
      Boolean(data.subAgentManagement),
      Boolean(data.subAgentSessions),
      Boolean(data.mcpStatus),
      Number(data.appBindingVersion ?? 0),
      (data.extensions as Record<string, unknown>) ?? null,
    );
  }
}

export class InitializeResult {
  constructor(
    public serverInfo: ServerInfo,
    public capabilities: ServerCapabilities,
  ) {}

  static fromWire(data: Record<string, unknown>): InitializeResult {
    return new InitializeResult(
      ServerInfo.fromWire((data.serverInfo as Record<string, unknown>) ?? {}),
      ServerCapabilities.fromWire((data.capabilities as Record<string, unknown>) ?? {}),
    );
  }
}

export function textPart(text: string): Record<string, unknown> {
  return { type: "text", text };
}

export function imageUrlPart(url: string): Record<string, unknown> {
  return { type: "image", url };
}

export function localImagePart(path: string): Record<string, unknown> {
  return { type: "localImage", path };
}

export function skillRefPart(name: string): Record<string, unknown> {
  return { type: "skillRef", name };
}

export function commandRefPart(rawText: string): Record<string, unknown> {
  const normalized = rawText.trim();
  const firstSpace = normalized.search(/\s/);
  const token = firstSpace >= 0 ? normalized.slice(0, firstSpace) : normalized;
  const name = token.replace(/^\//, "");
  const argsText = firstSpace >= 0 ? normalized.slice(firstSpace + 1).trim() : "";
  return {
    type: "commandRef",
    name,
    rawText: normalized,
    ...(argsText ? { argsText } : {}),
  };
}

export function fileRefPart(path: string, displayPath?: string): Record<string, unknown> {
  return {
    type: "fileRef",
    path,
    ...(displayPath ? { displayPath } : {}),
  };
}

export const DECISION_ACCEPT = "accept";
export const DECISION_ACCEPT_FOR_SESSION = "acceptForSession";
export const DECISION_ACCEPT_ALWAYS = "acceptAlways";
export const DECISION_DECLINE = "decline";
export const DECISION_CANCEL = "cancel";

export const ERR_NOT_INITIALIZED = -32002;
export const ERR_ALREADY_INITIALIZED = -32003;
export const ERR_THREAD_NOT_FOUND = -32010;
export const ERR_THREAD_NOT_ACTIVE = -32011;
export const ERR_TURN_IN_PROGRESS = -32012;
export const ERR_TURN_NOT_FOUND = -32013;
export const ERR_TURN_NOT_RUNNING = -32014;
export const ERR_APPROVAL_TIMEOUT = -32020;
export const ERR_CHANNEL_REJECTED = -32030;
export const ERR_CRON_NOT_FOUND = -32031;
