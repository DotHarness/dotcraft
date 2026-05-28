import {
  ERR_APPROVAL_TIMEOUT,
  ERR_THREAD_NOT_ACTIVE,
  ERR_THREAD_NOT_FOUND,
  ERR_TURN_IN_PROGRESS,
} from "./models.js";

export class DotCraftSdkError extends Error {
  readonly code: string;
  override readonly cause?: unknown;

  constructor(code: string, message: string, options?: { cause?: unknown }) {
    super(message);
    this.name = "DotCraftSdkError";
    this.code = code;
    this.cause = options?.cause;
  }
}

function jsonRpcDetail(data: unknown): string | null {
  if (!data || typeof data !== "object" || !("detail" in data)) return null;
  const detail = (data as { detail?: unknown }).detail;
  return typeof detail === "string" && detail.trim() ? detail : null;
}

export class DotCraftError extends DotCraftSdkError {
  readonly rpcCode: number;
  readonly rpcMessage: string;
  readonly data?: unknown;

  constructor(rpcCode: number, rpcMessage: string, data?: unknown, code = "jsonRpcError") {
    const detail = jsonRpcDetail(data);
    super(code, detail ? `${rpcMessage}: ${detail}` : rpcMessage, { cause: data });
    this.name = "DotCraftError";
    this.rpcCode = rpcCode;
    this.rpcMessage = rpcMessage;
    this.data = data;
  }
}

export class TurnInProgressError extends DotCraftError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "TurnInProgress");
    this.name = "TurnInProgressError";
  }
}

export class ThreadNotFoundError extends DotCraftError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "ThreadNotFound");
    this.name = "ThreadNotFoundError";
  }
}

export class ThreadNotActiveError extends DotCraftError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "ThreadNotActive");
    this.name = "ThreadNotActiveError";
  }
}

export class ApprovalTimeoutError extends DotCraftError {
  constructor(rpcCode: number, rpcMessage: string, data?: unknown) {
    super(rpcCode, rpcMessage, data, "ApprovalTimeout");
    this.name = "ApprovalTimeoutError";
  }
}

export class InitializationError extends DotCraftSdkError {
  constructor(message: string, cause?: unknown) {
    super("InitializationFailed", message, { cause });
    this.name = "InitializationError";
  }
}

export class TurnFailedError extends DotCraftSdkError {
  readonly turn?: unknown;

  constructor(message: string, turn?: unknown) {
    super("TurnFailed", message);
    this.name = "TurnFailedError";
    this.turn = turn;
  }
}

export class TurnCancelledError extends DotCraftSdkError {
  readonly turn?: unknown;

  constructor(message = "Turn was cancelled.", turn?: unknown) {
    super("TurnCancelled", message);
    this.name = "TurnCancelledError";
    this.turn = turn;
  }
}

export function toDotCraftError(rpcCode: number, rpcMessage: string, data?: unknown): DotCraftError {
  if (rpcCode === ERR_TURN_IN_PROGRESS) return new TurnInProgressError(rpcCode, rpcMessage, data);
  if (rpcCode === ERR_THREAD_NOT_FOUND) return new ThreadNotFoundError(rpcCode, rpcMessage, data);
  if (rpcCode === ERR_THREAD_NOT_ACTIVE) return new ThreadNotActiveError(rpcCode, rpcMessage, data);
  if (rpcCode === ERR_APPROVAL_TIMEOUT) return new ApprovalTimeoutError(rpcCode, rpcMessage, data);
  return new DotCraftError(rpcCode, rpcMessage, data);
}
