import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "@dotcraft/channel";
import { canUseNativeSingleChoiceUserInput, normalizeUserInputQuestions } from "@dotcraft/channel";
import {
  CARD_LOCALES,
  cardText,
  localizedText,
  type CardMessageKey,
  type LocalizedText,
} from "./card-locales.js";
import { chunkMarkdown, normalizeMarkdownForFeishu } from "./formatting.js";

export const DEFAULT_CARD_TITLE = "DotCraft";
export type TurnStatusPhase = "working" | "retrying" | "done" | "failed" | "stopped";

export interface QuestionPosition {
  index: number;
  count: number;
}

const STATUS_TEXT_KEYS = {
  working: "working",
  retrying: "retrying",
  done: "doneAfter",
  failed: "failedAfter",
  stopped: "stoppedAfter",
} as const satisfies Record<TurnStatusPhase, CardMessageKey>;

const STATUS_ICONS: Record<Exclude<TurnStatusPhase, "working" | "retrying">, { token: string; color: string }> = {
  done: { token: "succeed_filled", color: "green" },
  failed: { token: "error_filled", color: "red" },
  stopped: { token: "stop_outlined", color: "grey" },
};

export function buildTurnStatusCard(
  phase: TurnStatusPhase,
  elapsedMs: number,
  loadingIconImgKey?: string,
): Record<string, unknown> {
  const text = cardText(STATUS_TEXT_KEYS[phase], { elapsed: formatElapsed(elapsedMs) });
  const settled = phase !== "working" && phase !== "retrying";
  const icon = settled
    ? { tag: "standard_icon", ...STATUS_ICONS[phase], size: "16px 16px" }
    : loadingIconImgKey
      ? { tag: "custom_icon", img_key: loadingIconImgKey, size: "16px 16px" }
      : { tag: "standard_icon", token: "loading_outlined", color: "grey", size: "16px 16px" };
  return buildReplyV2Card(text, [{
    tag: "markdown",
    ...mapLocalized(text, (value) => `<font color="grey">${value}</font>`),
    text_size: "notation",
    icon,
  }]);
}

export function formatElapsed(elapsedMs: number): string {
  const totalSeconds = Math.max(0, Math.round(elapsedMs / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

export function resolveCardTitle(cardTitle?: string): string {
  const trimmed = (cardTitle ?? "").trim();
  return trimmed.length > 0 && trimmed.length <= 48 ? trimmed : DEFAULT_CARD_TITLE;
}

export function buildReplySummary(cardTitle?: string): LocalizedText {
  return cardText("replySummary", { title: resolveCardTitle(cardTitle) });
}

export function buildReplyCards(replyText: string, cardTitle?: string): Record<string, unknown>[] {
  const chunks = chunkMarkdown(replyText);
  const summary = buildReplySummary(cardTitle);
  return chunks.map((chunk, index) =>
    buildReplyV2Card(
      chunks.length > 1 ? mapLocalized(summary, (text) => `${text} (${index + 1}/${chunks.length})`) : summary,
      [{ tag: "markdown", content: normalizeMarkdownForFeishu(chunk) }],
    ),
  );
}

export function buildApprovalCard(params: {
  requestId: string;
  approvalType: string;
  operation: string;
  target: string;
  reason: string;
  timeoutSeconds: number;
  cardTitle?: string;
}): Record<string, unknown> {
  const title = resolveCardTitle(params.cardTitle);
  const body = localizedText((t) => {
    const lines = [t("approvalIntro", { title }), ""];
    if (params.approvalType === "shell") {
      lines.push(t("commandLine", { operation: params.operation }));
    } else {
      lines.push(t("operationLine", { operation: params.operation }));
      lines.push(t("targetLine", { target: params.target || t("targetMissing") }));
    }
    if (params.reason) lines.push(t("reasonLine", { reason: params.reason }));
    if (params.requestId) lines.push(t("requestLine", { requestId: params.requestId }));
    lines.push("", t("timeoutLine", { seconds: params.timeoutSeconds }));
    return lines.join("\n");
  });
  const buttons = [
    buildApprovalButton("approve", "primary", `approval_accept_${params.requestId}`, params.requestId, DECISION_ACCEPT),
    buildApprovalButton(
      "approveSession",
      "default",
      `approval_accept_session_${params.requestId}`,
      params.requestId,
      DECISION_ACCEPT_FOR_SESSION,
    ),
    buildApprovalButton("decline", "danger", `approval_decline_${params.requestId}`, params.requestId, DECISION_DECLINE),
    buildApprovalButton("cancel", "default", `approval_cancel_${params.requestId}`, params.requestId, DECISION_CANCEL),
  ];
  return buildV2Card(cardText("approvalTitle"), "orange", [markdownElement(body), ...buttons]);
}

export function buildApprovalResolvedCard(params: {
  requestId: string;
  decision: string;
  message?: string;
}): Record<string, unknown> {
  const body = localizedText((t) => {
    const lines = [t("requestLine", { requestId: params.requestId }), t("decisionLine", { decision: params.decision })];
    if (params.message) lines.push(params.message);
    return lines.join("\n");
  });
  return buildV2Card(cardText("approvalResolvedTitle"), "green", [markdownElement(body)]);
}

export function buildApprovalTimeoutCard(params: { requestId: string; timeoutSeconds: number }): Record<string, unknown> {
  const body = localizedText((t) =>
    [
      t("requestLine", { requestId: params.requestId }),
      t("decisionLine", { decision: DECISION_CANCEL }),
      t("timeoutLine", { seconds: params.timeoutSeconds }),
    ].join("\n"),
  );
  return buildV2Card(cardText("approvalTimeoutTitle"), "red", [markdownElement(body)]);
}

export function buildUserInputCard(params: {
  request: Record<string, unknown>;
  cardTitle?: string;
  questionPosition?: QuestionPosition;
}): Record<string, unknown> {
  const requestId = String(params.request.requestId ?? "");
  const title = resolveCardTitle(params.cardTitle);
  const questions = normalizeUserInputQuestions(params.request);
  const position = params.questionPosition;
  const body = localizedText((t) => {
    const intro = t("inputIntro", { title });
    const lines = [position && position.count > 1 ? `${intro} (${position.index + 1}/${position.count})` : intro];
    if (requestId) lines.push(t("requestLine", { requestId }));
    lines.push("");
    if (questions.length === 0) {
      lines.push(t("noQuestions"));
      return lines.join("\n");
    }
    questions.forEach((question, questionIndex) => {
      const prefix = questions.length > 1 ? `${questionIndex + 1}. ` : "";
      lines.push(`${prefix}${question.header || t("questionHeading", { index: questionIndex + 1 })}`);
      if (question.question) lines.push(question.question);
      if (question.isSecret) lines.push(t("secretWarning"));
      question.options.forEach((option, optionIndex) => {
        const detail = option.description ? ` - ${option.description}` : "";
        lines.push(`${optionIndex + 1}) ${option.label}${detail}`);
      });
      if (question.isOther) lines.push(t("otherOption"));
      lines.push("");
    });
    const question = questions[0]!;
    if (question.options.length > 0 && question.isOther) {
      lines.push(t("replyWithOptionOrOther"));
    } else if (question.options.length > 0) {
      lines.push(t("replyWithOption"));
    } else {
      lines.push(t("replyWithText"));
    }
    return lines.join("\n").trim();
  });
  const elements: Array<Record<string, unknown>> = [
    markdownElement(mapLocalized(body, (text) => normalizeMarkdownForFeishu(text))),
  ];
  if (canUseNativeSingleChoiceUserInput(params.request)) {
    questions[0]!.options.forEach((option, index) => {
      elements.push(buildUserInputButton(option.label, `user_input_${requestId}_${index}`, requestId, index));
    });
  }
  return buildV2Card(cardText("inputTitle"), "indigo", elements);
}

export function buildUserInputResolvedCard(params: {
  requestId: string;
  answerSummary?: string;
}): Record<string, unknown> {
  const body = localizedText((t) => {
    const lines = [t("requestLine", { requestId: params.requestId })];
    if (params.answerSummary) lines.push(t("answerLine", { answer: params.answerSummary }));
    return lines.join("\n");
  });
  return buildV2Card(cardText("inputReceivedTitle"), "green", [markdownElement(body)]);
}

export function buildNewConversationCard(): Record<string, unknown> {
  return buildV2Card(cardText("newConversationTitle"), "blue", [markdownElement(cardText("newConversationBody"))]);
}

const AUTH_NOTICE_TEMPLATES = {
  authSuccessBody: "green",
  authFailedBody: "red",
  authRevokedBody: "grey",
  authStatusBound: "blue",
  authStatusUnbound: "blue",
  authDirectOnlyBody: "orange",
  authDisabledBody: "orange",
} as const;

export type FeishuUserAuthNotice = keyof typeof AUTH_NOTICE_TEMPLATES;

export function buildUserAuthCard(url: string, code: string): Record<string, unknown> {
  return buildV2Card(cardText("authTitle"), "blue", [markdownElement(cardText("authBody", { url, code }))]);
}

export function buildUserAuthNoticeCard(
  notice: FeishuUserAuthNotice,
  params: Record<string, string> = {},
): Record<string, unknown> {
  return buildV2Card(cardText("authTitle"), AUTH_NOTICE_TEMPLATES[notice], [
    markdownElement(cardText(notice, params)),
  ]);
}

export function buildUnsupportedMessageCard(messageType: string): Record<string, unknown> {
  return buildV2Card(cardText("unsupportedTitle"), "red", [
    markdownElement(cardText("unsupportedBody", { type: messageType })),
  ]);
}

function markdownElement(text: LocalizedText): Record<string, unknown> {
  return { tag: "markdown", ...text };
}

function mapLocalized(text: LocalizedText, transform: (value: string) => string): LocalizedText {
  return {
    content: transform(text.content),
    i18n_content: Object.fromEntries(
      Object.entries(text.i18n_content).map(([locale, value]) => [locale, transform(value)]),
    ) as LocalizedText["i18n_content"],
  };
}

function summaryOf(text: LocalizedText): LocalizedText {
  return mapLocalized(text, (value) => value.slice(0, 50));
}

// Headerless on purpose: reply cards should read like plain messages. `summary` only feeds notifications.
function buildReplyV2Card(summary: LocalizedText, bodyElements: Array<Record<string, unknown>>): Record<string, unknown> {
  return {
    schema: "2.0",
    config: {
      update_multi: true,
      width_mode: "fill",
      locales: CARD_LOCALES,
      summary: summaryOf(summary),
    },
    body: {
      elements: bodyElements,
    },
  };
}

function buildV2Card(
  title: LocalizedText,
  template: string,
  bodyElements: Array<Record<string, unknown>>,
): Record<string, unknown> {
  return {
    schema: "2.0",
    config: {
      update_multi: true,
      width_mode: "fill",
      locales: CARD_LOCALES,
    },
    header: {
      title: { tag: "plain_text", ...title },
      template,
    },
    body: {
      elements: bodyElements,
    },
  };
}

function buildUserInputButton(
  label: string,
  elementId: string,
  requestId: string,
  optionIndex: number,
): Record<string, unknown> {
  return {
    tag: "button",
    element_id: elementId,
    text: { tag: "plain_text", content: label.slice(0, 64) },
    type: "primary",
    behaviors: [{ type: "callback", value: { kind: "userInput", requestId, optionIndex } }],
  };
}

function buildApprovalButton(
  label: CardMessageKey,
  type: "default" | "primary" | "danger",
  elementId: string,
  requestId: string,
  decision: string,
): Record<string, unknown> {
  return {
    tag: "button",
    element_id: elementId,
    text: { tag: "plain_text", ...cardText(label) },
    type,
    behaviors: [{ type: "callback", value: { kind: "approval", requestId, decision } }],
  };
}
