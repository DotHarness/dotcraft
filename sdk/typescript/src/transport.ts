/**
 * Transport layer: StdioTransport and WebSocketTransport.
 */

import { createInterface } from "node:readline";
import WebSocket from "ws";

export class TransportError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TransportError";
  }
}

export class TransportClosed extends TransportError {
  constructor(message = "Transport closed") {
    super(message);
    this.name = "TransportClosed";
  }
}

/** Abstract transport that reads/writes JSON-RPC messages. */
export interface Transport {
  readMessage(): Promise<Record<string, unknown>>;
  writeMessage(msg: Record<string, unknown>): Promise<void>;
  close(): Promise<void>;
}

/**
 * Newline-delimited JSON (JSONL) on stdin/stdout.
 * Used when DotCraft spawns the adapter as a subprocess.
 */
export class StdioTransport implements Transport {
  private closed = false;
  private readonly rl: ReturnType<typeof createInterface>;
  private lineIter: AsyncIterator<string> | null = null;

  constructor() {
    this.rl = createInterface({ input: process.stdin, crlfDelay: Infinity });
  }

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    if (!this.lineIter) {
      this.lineIter = this.rl[Symbol.asyncIterator]();
    }
    // eslint-disable-next-line no-constant-condition
    while (true) {
      const { value, done } = await this.lineIter.next();
      if (done) throw new TransportClosed("stdin closed");
      const text = String(value).trim();
      if (!text) continue;
      return JSON.parse(text) as Record<string, unknown>;
    }
  }

  async writeMessage(msg: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new TransportClosed();
    const line = `${JSON.stringify(msg)}\n`;
    await new Promise<void>((resolve, reject) => {
      process.stdout.write(line, (err) => (err ? reject(err) : resolve()));
    });
  }

  async close(): Promise<void> {
    this.closed = true;
    this.rl.close();
  }
}

export interface WebSocketTransportOptions {
  url: string;
  token?: string | null;
}

/**
 * One JSON-RPC message per WebSocket text frame.
 */
export class WebSocketTransport implements Transport {
  private readonly baseUrl: string;
  private readonly token: string | null | undefined;
  private ws: WebSocket | null = null;
  private closed = false;
  private receivedFrames: string[] = [];
  private waitingReaders: Array<{
    resolve: (msg: Record<string, unknown>) => void;
    reject: (error: unknown) => void;
  }> = [];
  private terminalError: unknown = null;

  constructor(opts: WebSocketTransportOptions) {
    this.baseUrl = opts.url;
    this.token = opts.token;
  }

  private get urlWithToken(): string {
    if (!this.token?.trim()) return this.baseUrl;
    try {
      const parsed = new URL(this.baseUrl);
      if (!parsed.searchParams.has("token")) {
        parsed.searchParams.set("token", this.token.trim());
      }
      return parsed.toString();
    } catch {
      const sep = this.baseUrl.includes("?") ? "&" : "?";
      return `${this.baseUrl}${sep}token=${encodeURIComponent(this.token.trim())}`;
    }
  }

  async connect(): Promise<void> {
    if (this.closed) throw new TransportClosed();
    if (this.ws !== null) return;
    this.ws = new WebSocket(this.urlWithToken);
    await new Promise<void>((resolve, reject) => {
      const w = this.ws!;
      const onOpen = () => {
        cleanup();
        this.attachSocketHandlers(w);
        resolve();
      };
      const onError = (e: Error) => {
        cleanup();
        this.terminalError = e;
        reject(e);
      };
      const onClose = () => {
        cleanup();
        const err = new TransportClosed("WebSocket closed");
        this.terminalError = err;
        reject(err);
      };
      const cleanup = () => {
        w.off("open", onOpen);
        w.off("error", onError);
        w.off("close", onClose);
      };
      w.once("open", onOpen);
      w.once("error", onError);
      w.once("close", onClose);
    });
  }

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    if (this.ws === null) await this.connect();
    if (this.receivedFrames.length > 0) {
      const frame = this.receivedFrames.shift()!;
      return JSON.parse(frame) as Record<string, unknown>;
    }
    if (this.terminalError) {
      throw this.terminalError;
    }
    return await new Promise<Record<string, unknown>>((resolve, reject) => {
      this.waitingReaders.push({ resolve, reject });
    });
  }

  async writeMessage(msg: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new TransportClosed();
    if (this.ws === null) throw new TransportError("WebSocket not connected; call connect() first");
    const text = JSON.stringify(msg);
    await new Promise<void>((resolve, reject) => {
      this.ws!.send(text, (err) => (err ? reject(err) : resolve()));
    });
  }

  async close(): Promise<void> {
    this.closed = true;
    const closeError = new TransportClosed("Transport closed");
    this.terminalError = closeError;
    this.receivedFrames = [];
    this.rejectWaitingReaders(closeError);
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  private attachSocketHandlers(ws: WebSocket): void {
    ws.on("message", (data: WebSocket.RawData) => {
      const text = this.rawDataToUtf8(data);
      if (this.waitingReaders.length > 0) {
        const waiter = this.waitingReaders.shift()!;
        try {
          waiter.resolve(JSON.parse(text) as Record<string, unknown>);
        } catch (error) {
          waiter.reject(error);
        }
        return;
      }
      this.receivedFrames.push(text);
    });
    ws.on("close", () => {
      if (this.closed) return;
      this.closed = true;
      this.ws = null;
      const err = new TransportClosed("WebSocket closed");
      this.terminalError = err;
      this.receivedFrames = [];
      this.rejectWaitingReaders(err);
    });
    ws.on("error", (error: Error) => {
      this.terminalError = error;
      this.rejectWaitingReaders(error);
    });
  }

  private rejectWaitingReaders(error: unknown): void {
    if (this.waitingReaders.length === 0) return;
    const readers = this.waitingReaders;
    this.waitingReaders = [];
    for (const reader of readers) {
      reader.reject(error);
    }
  }

  private rawDataToUtf8(data: WebSocket.RawData): string {
    if (typeof data === "string") return data;
    if (data instanceof Buffer) return data.toString("utf-8");
    if (Array.isArray(data)) return Buffer.concat(data).toString("utf-8");
    if (data instanceof ArrayBuffer) return Buffer.from(new Uint8Array(data)).toString("utf-8");
    if (ArrayBuffer.isView(data)) return Buffer.from(data.buffer, data.byteOffset, data.byteLength).toString("utf-8");
    throw new TransportError("Unsupported WebSocket raw data format");
  }
}
