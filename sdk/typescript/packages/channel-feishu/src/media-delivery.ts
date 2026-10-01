import { mediaSourceFromToolPath, prepareMediaBytes } from "@dotcraft/channel/media";
import type { ChannelToolDescriptor } from "@dotcraft/channel";

import { buildFileCaptionCard } from "./card-builder.js";
import { errorMessage, logError, logInfo, shortId } from "./logging.js";
import type { FeishuOutboundRouter } from "./outbound-router.js";

export const FEISHU_SEND_FILE_TOOL = "FeishuSendFileToCurrentChat";
export const FEISHU_SEND_IMAGE_TOOL = "FeishuSendImageToCurrentChat";

const FILE_MAX_BYTES = 30 * 1024 * 1024;
const IMAGE_MAX_BYTES = 10 * 1024 * 1024;

type MediaKind = "file" | "image";
type DeliverySource = "structured" | "tool";
type MediaRouter = Pick<FeishuOutboundRouter, "sendCard" | "sendFile" | "sendImage">;

export class FeishuMediaDelivery {
  constructor(private readonly router: MediaRouter) {}

  getDeliveryCapabilities(): Record<string, unknown> {
    return {
      structuredDelivery: true,
      media: {
        file: {
          maxBytes: FILE_MAX_BYTES,
          supportsHostPath: false,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
        },
        image: {
          maxBytes: IMAGE_MAX_BYTES,
          supportsHostPath: false,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
        },
      },
    };
  }

  getChannelTools(): ChannelToolDescriptor[] {
    return [
      {
        name: FEISHU_SEND_FILE_TOOL,
        description: "Send a real file attachment to the current Feishu chat.",
        requiresChatContext: true,
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
        display: {
          icon: "\u{1F4CE}",
          title: "Send file to current Feishu chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            filePath: { type: "string" },
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["filePath"],
        },
      },
      {
        name: FEISHU_SEND_IMAGE_TOOL,
        description: "Send a real image to the current Feishu chat.",
        requiresChatContext: true,
        approval: {
          kind: "file",
          targetArgument: "imagePath",
          operation: "read",
        },
        display: {
          icon: "\u{1F5BC}",
          title: "Send image to current Feishu chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            imagePath: { type: "string" },
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["imagePath"],
        },
      },
    ];
  }

  handlesTool(tool: string): boolean {
    return tool === FEISHU_SEND_FILE_TOOL || tool === FEISHU_SEND_IMAGE_TOOL;
  }

  async send(target: string, message: Record<string, unknown>): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind !== "file" && kind !== "image") {
      return {
        delivered: false,
        errorCode: "UnsupportedDeliveryKind",
        errorMessage: `Feishu example does not implement structured '${kind}' delivery yet.`,
      };
    }

    const fileName = String(message.fileName ?? (kind === "image" ? "image.png" : "attachment"));
    try {
      const sent = await this.deliver(target, kind, {
        source: (message.source as Record<string, unknown> | undefined) ?? {},
        fileName,
        mediaType: String(message.mediaType ?? "").trim() || undefined,
        caption: String(message.caption ?? ""),
      }, "structured");
      return {
        delivered: true,
        remoteMessageId: sent.messageId,
        remoteMediaId: sent.mediaKey,
      };
    } catch (error) {
      logError(`outbound.send.${kind}.failed`, {
        source: "structured",
        target: shortId(target),
        fileName,
        message: errorMessage(error),
      });
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: errorMessage(error),
      };
    }
  }

  async executeToolCall(
    tool: string,
    target: string,
    args: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind: MediaKind = tool === FEISHU_SEND_IMAGE_TOOL ? "image" : "file";
    const pathArgument = kind === "image" ? "imagePath" : "filePath";
    const hostPath = String(args[pathArgument] ?? "");
    if (!hostPath) {
      return {
        success: false,
        errorCode: kind === "image" ? "MissingImagePath" : "MissingFilePath",
        errorMessage: `Feishu ${kind} sending requires a ${pathArgument}.`,
      };
    }

    try {
      const fileName = String(args.fileName ?? "");
      const sent = await this.deliver(target, kind, {
        source: mediaSourceFromToolPath(hostPath, { fieldName: pathArgument }),
        fileName: fileName || undefined,
        caption: String(args.caption ?? ""),
      }, "tool");
      return {
        success: true,
        contentItems: [{
          type: "text",
          text: sent.captionDelivered
            ? `Sent ${sent.fileName} to the current chat.`
            : `Sent ${sent.fileName} to the current chat, but its caption could not be sent.`,
        }],
        structuredContent: {
          delivered: true,
          fileName: sent.fileName,
          remoteMessageId: sent.messageId,
          ...(kind === "image" ? { imageKey: sent.mediaKey } : { fileKey: sent.mediaKey }),
        },
      };
    } catch (error) {
      return {
        success: false,
        errorCode: "AdapterToolCallFailed",
        errorMessage: errorMessage(error),
      };
    }
  }

  private async deliver(
    target: string,
    kind: MediaKind,
    payload: {
      source: Record<string, unknown>;
      fileName?: string;
      mediaType?: string;
      caption: string;
    },
    source: DeliverySource,
  ): Promise<{ fileName: string; messageId: string; mediaKey: string; captionDelivered: boolean }> {
    const prepared = await prepareMediaBytes(payload.source, {
      fileName: payload.fileName,
      mediaType: payload.mediaType,
      maxBytes: kind === "image" ? IMAGE_MAX_BYTES : FILE_MAX_BYTES,
    });
    logInfo(`outbound.send.${kind}`, {
      source,
      target: shortId(target),
      fileName: prepared.fileName,
      bytes: prepared.bytes.length,
    });

    let sent: { messageId: string; mediaKey: string };
    if (kind === "image") {
      const result = await this.router.sendImage(target, { data: prepared.bytes });
      sent = { messageId: result.messageId, mediaKey: result.imageKey };
    } else {
      const result = await this.router.sendFile(target, {
        fileName: prepared.fileName,
        data: prepared.bytes,
        mediaType: prepared.mediaType,
      });
      sent = { messageId: result.messageId, mediaKey: result.fileKey };
    }

    const captionDelivered = await this.sendCaptionCard(target, payload.caption, prepared.fileName, source);
    return { fileName: prepared.fileName, ...sent, captionDelivered };
  }

  private async sendCaptionCard(
    target: string,
    caption: string,
    fileName: string,
    source: DeliverySource,
  ): Promise<boolean> {
    const normalized = caption.trim();
    if (!normalized) return true;
    try {
      await this.router.sendCard(target, buildFileCaptionCard(normalized, fileName));
    } catch (error) {
      logError("outbound.send.file.caption_card_failed", {
        source,
        target: shortId(target),
        fileName,
        message: errorMessage(error),
      });
      return false;
    }
    logInfo("outbound.send.file.caption_card_sent", {
      source,
      target: shortId(target),
      fileName,
      captionChars: normalized.length,
    });
    return true;
  }
}
