import {
  ERR_APPROVAL_TIMEOUT,
  ERR_THREAD_NOT_ACTIVE,
  ERR_THREAD_NOT_FOUND,
  ERR_TURN_IN_PROGRESS,
} from "./models.js";

export class DotCraftError extends Error {
  readonly code: string;
  override readonly cause?: unknown;

  constructor(code: string, message: string, options?: { cause?: unknown }) {
    super(message);
    this.name = "DotCraftError";
    this.code = code;
    this.cause = options?.cause;
  }
}

function jsonRpcDetail(data: unknown): string | null {
  if (!data || typeof data !== "object" || !("detail" in data)) return null;
  const detail = (data as { detail?: unknown }).detail;
  return typeof detail === "string" && detail.trim() ? detail : null;
}

export class JsonRpcError extends DotCraftError {
  readonly rpcCode: number;
  readonly rpcMessage: string;
  readonly data?: unknown;

  constructor(rpcCode: number, rpcMessage: string, data?: unknown, code = "jsonRpcError") {
    const detail = jsonRpcDetail(data);
    super(code, detail ? `${rpcMessage}: ${detail}` : rpcMessage, { cause: data });
    this.name = "JsonRpcError";
    this.rpcCode = rpcCode;
    this.rpcMessage = rpcMessage;
    this.data = data;
  }
}

export class TurnInProgressError extends JsonRpcError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "turnInProgress");
    this.name = "TurnInProgressError";
  }
}

export class ThreadNotFoundError extends JsonRpcError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "threadNotFound");
    this.name = "ThreadNotFoundError";
  }
}

export class ThreadNotActiveError extends JsonRpcError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "threadNotActive");
    this.name = "ThreadNotActiveError";
  }
}

export class ApprovalTimeoutError extends JsonRpcError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "approvalTimeout");
    this.name = "ApprovalTimeoutError";
  }
}

export class InitializationError extends DotCraftError {
  constructor(message: string, cause?: unknown) {
    super("initializationFailed", message, { cause });
    this.name = "InitializationError";
  }
}

export class RequestTimeoutError extends DotCraftError {
  constructor(method: string, timeoutMs: number) {
    super("requestTimeout", `Request '${method}' timed out after ${timeoutMs}ms.`);
    this.name = "RequestTimeoutError";
  }
}

export class ReconnectQueueFullError extends DotCraftError {
  constructor(limit: number) {
    super("reconnectQueueFull", `Reconnect queue reached its ${limit} message limit.`);
    this.name = "ReconnectQueueFullError";
  }
}

export class TurnFailedError extends DotCraftError {
  readonly turn?: unknown;

  constructor(message: string, turn?: unknown) {
    super("turnFailed", message);
    this.name = "TurnFailedError";
    this.turn = turn;
  }
}

export class TurnCancelledError extends DotCraftError {
  readonly turn?: unknown;

  constructor(message = "Turn was cancelled.", turn?: unknown) {
    super("turnCancelled", message);
    this.name = "TurnCancelledError";
    this.turn = turn;
  }
}

export class ProtocolViolationError extends DotCraftError {
  constructor(message: string, cause?: unknown) {
    super("protocolViolation", message, { cause });
    this.name = "ProtocolViolationError";
  }
}

export class RunDisconnectedError extends DotCraftError {
  constructor(message = "Run disconnected before completion.", cause?: unknown) {
    super("runDisconnected", message, { cause });
    this.name = "RunDisconnectedError";
  }
}

export function toJsonRpcError(rpcCode: number, rpcMessage: string, data?: unknown): JsonRpcError {
  if (rpcCode === ERR_TURN_IN_PROGRESS) return new TurnInProgressError(rpcCode, rpcMessage, data);
  if (rpcCode === ERR_THREAD_NOT_FOUND) return new ThreadNotFoundError(rpcCode, rpcMessage, data);
  if (rpcCode === ERR_THREAD_NOT_ACTIVE) return new ThreadNotActiveError(rpcCode, rpcMessage, data);
  if (rpcCode === ERR_APPROVAL_TIMEOUT) return new ApprovalTimeoutError(rpcCode, rpcMessage, data);
  return new JsonRpcError(rpcCode, rpcMessage, data);
}
