import { readFile } from "node:fs/promises";
import { createServer as createHttpServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { createServer as createHttpsServer } from "node:https";
import { URL } from "node:url";

import { WeComBizMsgCrypt } from "./wecom-crypto.js";
import { WeComPusher } from "./wecom-pusher.js";
import {
  parseWeComMessage,
  parseWeComParameters,
  normalizeWeComTextContent,
  validateWeComMessage,
  WeComMsgType,
  type WeComFrom,
  type WeComMessage,
} from "./wecom-types.js";

export type WeComTextMessageHandler = (parameters: string[], from: WeComFrom, pusher: WeComPusher) => Promise<void>;
export type WeComCommonMessageHandler = (message: WeComMessage, pusher: WeComPusher) => Promise<void>;
export type WeComEventMessageHandler = (
  eventType: string,
  chatType: string,
  from: WeComFrom,
  pusher: WeComPusher,
) => Promise<string | null | undefined>;

export interface WeComServerOptions {
  host: string;
  port: number;
  scheme?: "http" | "https";
  tls?: {
    certPath?: string;
    keyPath?: string;
  };
}

export class WeComBotRegistry {
  private readonly entries = new Map<string, { path: string; token: string; aesKey: string }>();
  private readonly crypts = new Map<string, WeComBizMsgCrypt>();
  private readonly handlers = new Map<
    string,
    { textHandler?: WeComTextMessageHandler; commonHandler?: WeComCommonMessageHandler; eventHandler?: WeComEventMessageHandler }
  >();
  private readonly webhookUrlCache = new Map<string, string>();

  register(path: string, token: string, aesKey: string): void {
    const normalized = normalizePath(path);
    if (!token) throw new Error("Token must not be empty.");
    if (!aesKey) throw new Error("EncodingAESKey must not be empty.");
    this.entries.set(normalized, { path: normalized, token, aesKey });
    this.crypts.set(normalized, new WeComBizMsgCrypt(token, aesKey));
  }

  setHandlers(
    path: string,
    textHandler?: WeComTextMessageHandler,
    commonHandler?: WeComCommonMessageHandler,
    eventHandler?: WeComEventMessageHandler,
  ): void {
    this.handlers.set(normalizePath(path), { textHandler, commonHandler, eventHandler });
  }

  getAllPaths(): string[] {
    return [...this.entries.keys()];
  }

  getCrypt(path: string): WeComBizMsgCrypt | undefined {
    return this.crypts.get(normalizePath(path));
  }

  getHandlers(path: string): { textHandler?: WeComTextMessageHandler; commonHandler?: WeComCommonMessageHandler; eventHandler?: WeComEventMessageHandler } | undefined {
    return this.handlers.get(normalizePath(path));
  }

  cacheWebhookUrl(chatId: string, webhookUrl: string | undefined): void {
    if (chatId && webhookUrl) this.webhookUrlCache.set(chatId, webhookUrl);
  }

  getWebhookUrl(chatId: string): string | undefined {
    return this.webhookUrlCache.get(chatId);
  }
}

export class WeComBotServer {
  private server: Server | undefined;

  constructor(
    private readonly registry: WeComBotRegistry,
    private readonly options: WeComServerOptions,
  ) {}

  async start(): Promise<void> {
    if (this.server) return;
    this.server = await this.createServer();
    await new Promise<void>((resolve, reject) => {
      const onError = (error: Error) => {
        this.server?.off("error", onError);
        reject(error);
      };
      this.server?.once("error", onError);
      this.server?.listen(this.options.port, this.options.host, () => {
        this.server?.off("error", onError);
        resolve();
      });
    });
  }

  async stop(): Promise<void> {
    const server = this.server;
    this.server = undefined;
    if (!server) return;
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }

  private async createServer(): Promise<Server> {
    const listener = (req: IncomingMessage, res: ServerResponse) => {
      void this.handle(req, res).catch((error) => {
        console.error("[wecom] callback handling failed:", error);
        if (!res.headersSent) res.statusCode = 500;
        res.end();
      });
    };

    if (this.options.scheme === "https") {
      const certPath = this.options.tls?.certPath;
      const keyPath = this.options.tls?.keyPath;
      if (!certPath || !keyPath) throw new Error("HTTPS requires wecom.tls.certPath and wecom.tls.keyPath.");
      return createHttpsServer({ cert: await readFile(certPath), key: await readFile(keyPath) }, listener);
    }

    return createHttpServer(listener);
  }

  private async handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "localhost"}`);
    const path = normalizePath(decodeURIComponent(url.pathname));
    if (req.method === "GET") {
      await this.handleVerifyUrl(path, url, res);
      return;
    }
    if (req.method === "POST") {
      await this.handleMessage(path, url, req, res);
      return;
    }
    res.statusCode = 405;
    res.end("Method not allowed");
  }

  private async handleVerifyUrl(path: string, url: URL, res: ServerResponse): Promise<void> {
    const msgSignature = url.searchParams.get("msg_signature") ?? "";
    const timestamp = url.searchParams.get("timestamp") ?? "";
    const nonce = url.searchParams.get("nonce") ?? "";
    const echoStr = url.searchParams.get("echostr") ?? "";
    if (!msgSignature || !timestamp || !nonce || !echoStr) {
      res.statusCode = 400;
      res.end("Missing parameters");
      return;
    }

    const crypt = this.registry.getCrypt(path);
    if (!crypt) {
      res.statusCode = 404;
      res.end("Bot not found");
      return;
    }

    try {
      res.end(crypt.verifyUrl(msgSignature, timestamp, nonce, echoStr));
    } catch (error) {
      res.statusCode = 400;
      res.end(`Verification failed: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  private async handleMessage(path: string, url: URL, req: IncomingMessage, res: ServerResponse): Promise<void> {
    const msgSignature = url.searchParams.get("msg_signature") ?? "";
    const timestamp = url.searchParams.get("timestamp") ?? "";
    const nonce = url.searchParams.get("nonce") ?? "";
    if (!msgSignature || !timestamp || !nonce) {
      res.statusCode = 400;
      res.end("Missing parameters");
      return;
    }

    const crypt = this.registry.getCrypt(path);
    if (!crypt) {
      res.statusCode = 404;
      res.end("Bot not found");
      return;
    }

    const handlers = this.registry.getHandlers(path);
    if (!handlers || (!handlers.textHandler && !handlers.commonHandler && !handlers.eventHandler)) {
      res.statusCode = 200;
      res.end();
      return;
    }

    const body = await readRequestBody(req);
    const plaintext = crypt.decryptMsg(msgSignature, timestamp, nonce, body);
    const message = parseWeComMessage(plaintext);
    if (!message || !validateWeComMessage(message)) {
      res.statusCode = 400;
      res.end();
      return;
    }

    if (message.msgType === WeComMsgType.Event) {
      await this.handleEventMessage(message, handlers, crypt, timestamp, nonce, res);
      return;
    }

    res.statusCode = 200;
    res.end();
    void this.processMessage(message, handlers).catch((error) => {
      console.error("[wecom] async message processing failed:", error);
    });
  }

  private async handleEventMessage(
    message: WeComMessage,
    handlers: NonNullable<ReturnType<WeComBotRegistry["getHandlers"]>>,
    crypt: WeComBizMsgCrypt,
    timestamp: string,
    nonce: string,
    res: ServerResponse,
  ): Promise<void> {
    if (!handlers.eventHandler) {
      res.statusCode = 200;
      res.end();
      return;
    }

    this.registry.cacheWebhookUrl(message.chatId, message.webhookUrl);
    const pusher = new WeComPusher(message.chatId, message.webhookUrl);
    const responseText = await handlers.eventHandler(
      message.event?.eventType ?? "",
      message.chatType,
      message.from ?? emptyFrom(),
      pusher,
    );
    if (!responseText) {
      res.statusCode = 200;
      res.end();
      return;
    }

    res.setHeader("Content-Type", "application/xml");
    res.end(crypt.encryptMsg(responseText, timestamp, nonce));
  }

  private async processMessage(
    message: WeComMessage,
    handlers: NonNullable<ReturnType<WeComBotRegistry["getHandlers"]>>,
  ): Promise<void> {
    const webhookUrl = message.webhookUrl || message.responseUrl || "";
    this.registry.cacheWebhookUrl(message.chatId, webhookUrl);
    const pusher = new WeComPusher(message.chatId, webhookUrl);

    if (message.msgType === WeComMsgType.Voice && handlers.textHandler) {
      const content = normalizeWeComTextContent(message.voice?.content ?? "");
      await handlers.textHandler(parseWeComParameters(content, message.chatType), message.from ?? emptyFrom(), pusher);
      return;
    }

    if (message.msgType === WeComMsgType.Text && handlers.textHandler) {
      const content = normalizeWeComTextContent(message.text?.content ?? "");
      await handlers.textHandler(parseWeComParameters(content, message.chatType), message.from ?? emptyFrom(), pusher);
      return;
    }

    if (handlers.commonHandler) {
      await handlers.commonHandler(message, pusher);
    }
  }
}

function normalizePath(path: string): string {
  const trimmed = path.trim();
  if (!trimmed || trimmed === "/") throw new Error("Path must not be empty.");
  return trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
}

function readRequestBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on("data", (chunk) => chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)));
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf-8")));
    req.on("error", reject);
  });
}

function emptyFrom(): WeComFrom {
  return { userId: "", name: "", alias: "" };
}

