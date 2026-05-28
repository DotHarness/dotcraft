import { WebSocketServer, type WebSocket } from "ws";

export interface OneBotMessageSegment {
  type: string;
  data?: Record<string, unknown>;
}

export interface OneBotSender {
  user_id?: number;
  nickname?: string;
  card?: string;
  role?: string;
  title?: string;
}

export interface OneBotMessageEvent {
  post_type: "message" | string;
  message_type: "group" | "private" | string;
  sub_type?: string;
  message_id?: number;
  self_id?: number;
  user_id: number;
  group_id?: number;
  message: OneBotMessageSegment[] | string;
  raw_message?: string;
  sender?: OneBotSender;
}

export interface OneBotActionResponse {
  status?: string;
  retcode?: number;
  data?: unknown;
  message?: string;
  wording?: string;
  echo?: string | number;
}

export interface OneBotAction {
  action: string;
  params?: Record<string, unknown>;
  echo?: string;
}

export function isOneBotMessageEvent(value: unknown): value is OneBotMessageEvent {
  if (!value || typeof value !== "object") return false;
  const evt = value as Record<string, unknown>;
  return evt.post_type === "message" && typeof evt.user_id === "number";
}

export function isOneBotActionResponse(value: unknown): value is OneBotActionResponse {
  if (!value || typeof value !== "object") return false;
  return "echo" in value && ("status" in value || "retcode" in value || "data" in value);
}

export function textSegment(text: string): OneBotMessageSegment {
  return { type: "text", data: { text } };
}

export function atSegment(qq: string): OneBotMessageSegment {
  return { type: "at", data: { qq } };
}

export function recordSegment(file: string): OneBotMessageSegment {
  return { type: "record", data: { file } };
}

export function videoSegment(file: string): OneBotMessageSegment {
  return { type: "video", data: { file } };
}

export function getPlainText(message: OneBotMessageSegment[] | string): string {
  if (typeof message === "string") return message;
  return message
    .filter((segment) => segment.type === "text")
    .map((segment) => String(segment.data?.text ?? ""))
    .join("");
}

export function getSenderName(evt: OneBotMessageEvent): string {
  const sender = evt.sender ?? {};
  return String(sender.card || sender.nickname || evt.user_id);
}

export function getAtQQ(segment: OneBotMessageSegment): string | null {
  if (segment.type !== "at") return null;
  const qq = segment.data?.qq;
  return qq === undefined || qq === null ? null : String(qq);
}

export function getImageUrl(segment: OneBotMessageSegment): string | null {
  if (segment.type !== "image") return null;
  const url = segment.data?.url ?? segment.data?.file;
  return url === undefined || url === null ? null : String(url);
}

export function normalizeMessageSegments(message: OneBotMessageSegment[] | string): OneBotMessageSegment[] {
  if (typeof message === "string") return [textSegment(message)];
  return message;
}

export function isActionOk(response: OneBotActionResponse): boolean {
  return response.status === "ok" || response.retcode === 0;
}

export class OneBotReverseWsServer {
  private server: WebSocketServer | null = null;
  private readonly connections = new Set<WebSocket>();
  private readonly pending = new Map<string, {
    resolve: (response: OneBotActionResponse) => void;
    reject: (error: unknown) => void;
    timer: ReturnType<typeof setTimeout>;
  }>();
  private echoCounter = 0;
  private messageHandlers: Array<(evt: OneBotMessageEvent) => void | Promise<void>> = [];

  constructor(
    private readonly host: string,
    private readonly port: number,
    private readonly accessToken = "",
  ) {}

  onMessage(handler: (evt: OneBotMessageEvent) => void | Promise<void>): void {
    this.messageHandlers.push(handler);
  }

  async start(): Promise<void> {
    if (this.server) return;
    this.server = new WebSocketServer({ host: this.host, port: this.port });
    this.server.on("connection", (socket, request) => {
      if (!this.validateAccessToken(request.headers.authorization, request.url ?? "")) {
        socket.close(1008, "invalid access token");
        return;
      }
      this.connections.add(socket);
      socket.on("message", (data) => {
        void this.handleRawMessage(data.toString("utf-8"));
      });
      socket.on("close", () => {
        this.connections.delete(socket);
      });
      socket.on("error", (error) => {
        console.warn("[qq] OneBot socket error:", error);
      });
    });
    await new Promise<void>((resolve) => this.server!.once("listening", resolve));
  }

  async stop(): Promise<void> {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(new Error("OneBot server stopped."));
    }
    this.pending.clear();
    for (const socket of this.connections) {
      socket.close();
    }
    this.connections.clear();
    const server = this.server;
    this.server = null;
    if (server) {
      await new Promise<void>((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
    }
  }

  async sendAction(action: OneBotAction, timeoutMs = 30_000): Promise<OneBotActionResponse> {
    const socket = [...this.connections].find((item) => item.readyState === item.OPEN);
    if (!socket) throw new Error("No active OneBot WebSocket connection.");

    const echo = String(++this.echoCounter);
    const payload = { ...action, echo };
    const responsePromise = new Promise<OneBotActionResponse>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(echo);
        reject(new Error(`OneBot action '${action.action}' timed out.`));
      }, timeoutMs);
      this.pending.set(echo, { resolve, reject, timer });
    });

    socket.send(JSON.stringify(payload));
    return await responsePromise;
  }

  private validateAccessToken(authorization: string | undefined, url: string): boolean {
    if (!this.accessToken) return true;
    if (authorization?.toLowerCase().startsWith("bearer ")) {
      return authorization.slice("bearer ".length) === this.accessToken;
    }
    try {
      const parsed = new URL(url, "ws://localhost");
      return parsed.searchParams.get("access_token") === this.accessToken;
    } catch {
      return false;
    }
  }

  private async handleRawMessage(raw: string): Promise<void> {
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch (error) {
      console.warn("[qq] invalid OneBot JSON:", error);
      return;
    }

    if (isOneBotActionResponse(parsed)) {
      const echo = String(parsed.echo ?? "");
      const pending = this.pending.get(echo);
      if (pending) {
        clearTimeout(pending.timer);
        this.pending.delete(echo);
        pending.resolve(parsed);
      }
      return;
    }

    if (!isOneBotMessageEvent(parsed)) {
      return;
    }

    await Promise.all(this.messageHandlers.map((handler) => handler(parsed)));
  }
}

export function sendGroupMessageAction(groupId: string, message: OneBotMessageSegment[]): OneBotAction {
  return { action: "send_group_msg", params: { group_id: Number(groupId), message } };
}

export function sendPrivateMessageAction(userId: string, message: OneBotMessageSegment[]): OneBotAction {
  return { action: "send_private_msg", params: { user_id: Number(userId), message } };
}

export function uploadGroupFileAction(
  groupId: string,
  file: string,
  name: string,
  folder?: string,
): OneBotAction {
  return {
    action: "upload_group_file",
    params: {
      group_id: Number(groupId),
      file,
      name,
      ...(folder ? { folder } : {}),
    },
  };
}

export function uploadPrivateFileAction(userId: string, file: string, name: string): OneBotAction {
  return {
    action: "upload_private_file",
    params: {
      user_id: Number(userId),
      file,
      name,
    },
  };
}
