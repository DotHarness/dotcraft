import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, extname, join } from "node:path";

import { WeComPusher } from "./wecom-pusher.js";

export const WE_COM_SEND_VOICE_TOOL = "WeComSendVoice";
export const WE_COM_SEND_FILE_TOOL = "WeComSendFile";

export class WeComMediaError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "WeComMediaError";
    this.code = code;
  }
}

export class WeComMediaTools {
  getDeliveryCapabilities(): Record<string, unknown> {
    return {
      structuredDelivery: true,
      media: {
        audio: {
          supportsHostPath: true,
          supportsBase64: true,
        },
        file: {
          supportsHostPath: true,
          supportsBase64: true,
        },
      },
    };
  }

  getChannelTools(): Record<string, unknown>[] {
    return [
      {
        name: WE_COM_SEND_VOICE_TOOL,
        description:
          "Send a voice message in the current WeCom chat. WeCom voice only supports AMR files; use WeComSendFile for other audio formats.",
        requiresChatContext: true,
        display: { icon: "\u{1F3A4}", title: "Send voice in current WeCom chat" },
        inputSchema: {
          type: "object",
          properties: {
            filePath: { type: "string" },
          },
          required: ["filePath"],
        },
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
      },
      {
        name: WE_COM_SEND_FILE_TOOL,
        description: "Send a file in the current WeCom chat. The file must be a local absolute path.",
        requiresChatContext: true,
        display: { icon: "\u{1F4C1}", title: "Send file in current WeCom chat" },
        inputSchema: {
          type: "object",
          properties: {
            filePath: { type: "string" },
          },
          required: ["filePath"],
        },
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
      },
    ];
  }

  async sendStructuredMessage(pusher: WeComPusher, message: Record<string, unknown>): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      await pusher.pushText(String(message.text ?? ""));
      return { delivered: true };
    }
    if (kind === "audio") {
      await this.sendMedia(pusher, message, "voice");
      return { delivered: true };
    }
    if (kind === "file") {
      await this.sendMedia(pusher, message, "file");
      return { delivered: true };
    }

    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `WeCom channel does not support '${kind}' delivery.`,
    };
  }

  async executeToolCall(pusher: WeComPusher, toolName: string, args: Record<string, unknown>): Promise<Record<string, unknown>> {
    const filePath = requiredText(args.filePath, "filePath");
    const kind = toolName === WE_COM_SEND_VOICE_TOOL ? "audio" : toolName === WE_COM_SEND_FILE_TOOL ? "file" : "";
    if (!kind) {
      return {
        success: false,
        errorCode: "UnsupportedChannelTool",
        errorMessage: `WeCom does not expose tool '${toolName}'.`,
      };
    }

    const result = await this.sendStructuredMessage(pusher, {
      kind,
      source: { kind: "hostPath", hostPath: filePath },
      fileName: basename(filePath),
    });

    return {
      success: Boolean(result.delivered),
      contentItems: [
        {
          type: "text",
          text: result.delivered ? "Message sent." : String(result.errorMessage ?? "Tool execution failed."),
        },
      ],
      structuredResult: {
        delivered: Boolean(result.delivered),
        errorCode: result.errorCode ?? null,
        target: pusher.getChatId(),
      },
      errorCode: result.errorCode,
      errorMessage: result.errorMessage,
    };
  }

  private async sendMedia(pusher: WeComPusher, message: Record<string, unknown>, mediaKind: "voice" | "file"): Promise<void> {
    const source = asRecord(message.source);
    const fileName = optionalText(message.fileName) ?? inferFileName(source) ?? `${mediaKind}.bin`;
    if (mediaKind === "voice" && !fileName.toLowerCase().endsWith(".amr")) {
      throw new WeComMediaError("UnsupportedVoiceFormat", "WeCom voice delivery only supports AMR files.");
    }

    let cleanupDir: string | undefined;
    try {
      const path = await resolveFileSource(source, fileName);
      cleanupDir = path.cleanupDir;
      const mediaId = await pusher.uploadMediaFromPath(path.path, fileName, mediaKind);
      if (mediaKind === "voice") await pusher.pushVoice(mediaId);
      else await pusher.pushFile(mediaId);
    } finally {
      if (cleanupDir) await rm(cleanupDir, { recursive: true, force: true }).catch(() => undefined);
    }
  }
}

async function resolveFileSource(source: Record<string, unknown>, fileName: string): Promise<{ path: string; cleanupDir?: string }> {
  const kind = String(source.kind ?? "");
  if (kind === "hostPath") return { path: requiredText(source.hostPath, "hostPath") };
  if (kind === "dataBase64") {
    const dir = await mkdtemp(join(tmpdir(), "dotcraft-wecom-file-"));
    const path = join(dir, fileName);
    await writeFile(path, Buffer.from(requiredText(source.dataBase64, "dataBase64"), "base64"));
    return { path, cleanupDir: dir };
  }
  throw new WeComMediaError("UnsupportedMediaSource", `WeCom media delivery only supports hostPath or dataBase64, got '${kind}'.`);
}

function inferFileName(source: Record<string, unknown>): string | null {
  const hostPath = optionalText(source.hostPath);
  if (hostPath) return basename(hostPath);
  const ext = extname(optionalText(source.url) ?? "");
  return ext ? `attachment${ext}` : null;
}

function requiredText(value: unknown, field: string): string {
  const text = String(value ?? "").trim();
  if (!text) throw new WeComMediaError("InvalidArguments", `${field} is required.`);
  return text;
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

