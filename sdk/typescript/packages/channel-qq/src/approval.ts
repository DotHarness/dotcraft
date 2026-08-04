import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "@dotcraft/channel";

export function parseQQApprovalDecision(text: string): string | null {
  const raw = text.trim();
  const t = raw.toLowerCase();

  if (
    raw === "\u540c\u610f\u5168\u90e8" ||
    raw === "\u5141\u8bb8\u5168\u90e8" ||
    t === "yes all" ||
    t === "approve all" ||
    t === "y all"
  ) {
    return DECISION_ACCEPT_FOR_SESSION;
  }

  if (
    raw === "\u540c\u610f" ||
    raw === "\u5141\u8bb8" ||
    t === "yes" ||
    t === "y" ||
    t === "approve"
  ) {
    return DECISION_ACCEPT;
  }

  if (
    raw === "\u62d2\u7edd" ||
    raw === "\u4e0d\u540c\u610f" ||
    t === "no" ||
    t === "n" ||
    t === "reject" ||
    t === "deny"
  ) {
    return DECISION_DECLINE;
  }

  if (t === "cancel" || raw === "\u53d6\u6d88") {
    return DECISION_CANCEL;
  }

  return null;
}
