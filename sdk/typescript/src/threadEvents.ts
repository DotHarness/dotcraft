import type { DotCraftWireClient } from "./client.js";
import { JsonRpcMessage } from "./models.js";
import type { ServerNotificationMethods, ThreadNotification, TurnNotification } from "./generated/appserver/index.js";

const methods = [
  "thread/started", "thread/renamed", "thread/resumed", "thread/archived", "thread/unarchived",
  "thread/status/changed", "thread/queue/updated", "turn/started", "turn/completed", "turn/failed", "turn/cancelled",
  "item/started", "item/completed", "item/agentMessage/delta", "item/reasoning/delta", "item/toolCall/argumentsDelta",
  "item/approval/resolved", "item/tool/requestUserInput/resolved", "item/toolExecution/progress",
  "subagent/progress", "item/usage/delta", "system/event", "plan/updated",
] as const satisfies readonly (keyof ServerNotificationMethods)[];

type ThreadEventMethod = typeof methods[number];
type ThreadEventParams = ServerNotificationMethods[ThreadEventMethod]["params"];

export function threadEventIdentity(method: string | null | undefined, params: Record<string, unknown>): { threadId?: string; turnId?: string } {
  switch (method) {
    case "thread/started":
    case "thread/resumed":
      return { threadId: (params as unknown as ThreadNotification).thread?.id };
    case "turn/started":
    case "turn/completed":
    case "turn/failed":
    case "turn/cancelled": {
      const turn = (params as unknown as TurnNotification).turn;
      return { threadId: turn?.threadId, turnId: turn?.id };
    }
    default: {
      const event = params as ThreadEventParams;
      return { threadId: event.threadId as string | undefined, turnId: event.turnId as string | undefined };
    }
  }
}

export function captureThreadEvents(client: DotCraftWireClient, threadId: string): AsyncIterableIterator<JsonRpcMessage> {
  const queue: JsonRpcMessage[] = [];
  let wake: (() => void) | undefined;
  let finished = false;
  const unsubscribe = methods.map((method) => client.on(method, (params) => {
    if (finished || threadEventIdentity(method, params).threadId !== threadId) return;
    queue.push(JsonRpcMessage.fromDict({ method, params }));
    wake?.();
    wake = undefined;
  }));
  const cleanup = (): void => {
    if (finished) return;
    finished = true;
    unsubscribe.forEach((remove) => remove());
    wake?.();
    wake = undefined;
  };
  unsubscribe.push(client.onStateChanged((state) => {
    if (state === "disconnected" || state === "closed") cleanup();
  }));
  return {
    next: async () => {
      while (!finished && queue.length === 0) await new Promise<void>((resolve) => { wake = resolve; });
      const value = queue.shift();
      return value ? { value, done: false } : { value: undefined, done: true };
    },
    return: async () => { queue.length = 0; cleanup(); return { value: undefined, done: true }; },
    [Symbol.asyncIterator]() { return this; },
  };
}
