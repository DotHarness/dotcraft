/**
 * Wire DTO models for the DotCraft AppServer Wire Protocol.
 */

import type { InputPart } from "./generated/appserver/index.js";

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

export function textPart(text: string): InputPart {
  return { type: "text", text };
}

export function imageDataUrlPart(dataUrl: string): InputPart {
  return { type: "image", url: dataUrl };
}

export function localImagePart(path: string): InputPart {
  return { type: "localImage", path };
}

export function skillRefPart(name: string): InputPart {
  return { type: "skillRef", name };
}

export function commandRefPart(rawText: string): InputPart {
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

export function fileRefPart(path: string, displayPath?: string): InputPart {
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
