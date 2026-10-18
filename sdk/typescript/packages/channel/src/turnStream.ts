import type { JsonRpcMessage } from "@dotcraft/sdk/wire";

export interface ChannelTurn {
  threadId: string;
  turnId: string;
  channelContext: string;
}

export interface TurnItemActivity {
  kind: "text" | "reasoning" | "tool";
  phase: "started" | "completed";
  itemId: string;
}

export type ChannelTurnOutcome =
  | { status: "completed" }
  | { status: "failed"; error: string }
  | { status: "cancelled" }
  | { status: "disconnected" };

export interface TurnStreamHandlers {
  onActivity(turn: ChannelTurn, activity: TurnItemActivity): Promise<void>;
  onTurnRetrying(turn: ChannelTurn): Promise<void>;
  onHandlerError?(hook: string, error: unknown): void;
}

export function activityKindForItemType(itemType: string): TurnItemActivity["kind"] | null {
  switch (itemType) {
    case "agentMessage":
      return "text";
    case "reasoningContent":
      return "reasoning";
    case "toolCall":
    case "mcpToolCall":
    case "dynamicToolCall":
    case "toolResult":
    case "commandExecution":
      return "tool";
    default:
      return null;
  }
}

export async function consumeTurnStream(
  eventStream: AsyncIterableIterator<JsonRpcMessage>,
  turn: ChannelTurn,
  handlers: TurnStreamHandlers,
): Promise<ChannelTurnOutcome> {
  const observe = async (hook: string, run: () => Promise<void>): Promise<void> => {
    try {
      await run();
    } catch (error) {
      handlers.onHandlerError?.(hook, error);
    }
  };

  for await (const event of eventStream) {
    const params = (event.params as Record<string, unknown> | undefined) ?? {};
    switch (event.method) {
      case "item/started":
      case "item/completed": {
        const item = (params.item as Record<string, unknown> | undefined) ?? {};
        const kind = activityKindForItemType(String(item.type ?? ""));
        const itemId = String(item.id ?? "");
        if (!kind || !itemId) break;
        const phase = event.method === "item/started" ? "started" : "completed";
        await observe("onActivity", () => handlers.onActivity(turn, { kind, phase, itemId }));
        break;
      }
      case "system/event":
        if (params.kind === "streamError" && (params.turnId == null || params.turnId === turn.turnId)) {
          await observe("onTurnRetrying", () => handlers.onTurnRetrying(turn));
        }
        break;
      case "turn/completed":
        return { status: "completed" };
      case "turn/failed": {
        const failed = (params.turn as Record<string, unknown> | undefined) ?? {};
        return { status: "failed", error: String(failed.error ?? params.error ?? "Unknown error") };
      }
      case "turn/cancelled":
        return { status: "cancelled" };
    }
  }
  return { status: "disconnected" };
}
