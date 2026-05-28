import type { FeishuCardActionEvent } from "./feishu-types.js";

/**
 * Parses approval callback payload from a card action (object or JSON string).
 */
export function parseApprovalActionFromCardEvent(
  value: Record<string, unknown> | string | undefined,
): { requestId: string; decision: string } | null {
  if (value == null) return null;
  if (typeof value === "object") {
    const o = value as Record<string, unknown>;
    if (o.kind !== "approval") return null;
    return {
      requestId: String(o.requestId ?? ""),
      decision: String(o.decision ?? ""),
    };
  }
  try {
    const o = JSON.parse(value) as Record<string, unknown>;
    if (o.kind !== "approval") return null;
    return {
      requestId: String(o.requestId ?? ""),
      decision: String(o.decision ?? ""),
    };
  } catch {
    return null;
  }
}

export type CardActionDedupKeyResult = {
  /** Stable key for remember(); distinct per user click on a specific approval decision. */
  key: string;
  /** True when critical fields were missing and the key may collide across unrelated actions. */
  weak: boolean;
};

/**
 * Builds a deduplication key for Feishu `card.action.trigger` events.
 * When `event_id` is absent, `token` alone is not sufficient (often constant), so we combine
 * open_message_id, requestId, decision, and operator identity.
 */
export function buildCardActionDedupKey(event: FeishuCardActionEvent): CardActionDedupKeyResult {
  const eventId = (event.event_id ?? "").trim();
  if (eventId.length > 0) {
    return { key: `action:event:${eventId}`, weak: false };
  }

  const openMessageId = String(event.context?.open_message_id ?? "").trim();
  const approval = parseApprovalActionFromCardEvent(event.action?.value);
  const requestId = approval?.requestId?.trim() ?? "";
  const decision = approval?.decision?.trim() ?? "";
  const operatorId =
    String(event.operator?.open_id ?? event.operator?.user_id ?? event.operator?.union_id ?? "").trim() ||
    "unknown_operator";
  const token = String(event.token ?? "").trim();
  const actionTag = String(event.action?.tag ?? "").trim();

  if (openMessageId && requestId && decision) {
    return {
      key: `action:approval:${openMessageId}:${requestId}:${decision}:${operatorId}`,
      weak: false,
    };
  }

  const key = `action:weak:${token}:${openMessageId}:${requestId}:${decision}:${operatorId}:${actionTag}`;
  return { key, weak: true };
}
