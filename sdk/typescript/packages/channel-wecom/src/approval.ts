import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "@dotcraft/sdk";

export const WE_COM_APPROVAL_CANCEL = DECISION_CANCEL;

export function parseWeComApprovalDecision(text: string): string | null {
  const raw = text.trim();
  const normalized = raw.toLowerCase();

  if (
    ["同意全部", "允许全部"].some((candidate) => raw.includes(candidate)) ||
    ["yes all", "approve all", "y all"].includes(normalized)
  ) {
    return DECISION_ACCEPT_FOR_SESSION;
  }

  if (["同意", "允许", "yes", "y", "approve"].includes(normalized) || raw === "同意" || raw === "允许") {
    return DECISION_ACCEPT;
  }

  if (["拒绝", "不同意", "no", "n", "reject", "deny"].includes(normalized) || raw === "拒绝") {
    return DECISION_DECLINE;
  }

  return null;
}

