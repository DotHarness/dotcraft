import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "@dotcraft/sdk";
import {
  buildUserInputPrompt,
  canUseNativeSingleChoiceUserInput,
  normalizeUserInputQuestions,
} from "@dotcraft/sdk/channel";
import { chunkMarkdown, normalizeMarkdownForFeishu, summarizeApprovalOperation } from "./formatting.js";

export const DEFAULT_CARD_TITLE = "DotCraft";

function resolveCardTitle(cardTitle?: string): string {
  const trimmed = (cardTitle ?? "").trim();
  return trimmed.length > 0 && trimmed.length <= 48 ? trimmed : DEFAULT_CARD_TITLE;
}

export function buildReplyCards(replyText: string, cardTitle?: string): Record<string, unknown>[] {
  const chunks = chunkMarkdown(replyText);
  const title = resolveCardTitle(cardTitle);
  return chunks.map((chunk, index) =>
    buildV2Card(
      chunks.length > 1 ? `${title} Reply (${index + 1}/${chunks.length})` : `${title} Reply`,
      "blue",
      [
        {
          tag: "markdown",
          content: normalizeMarkdownForFeishu(chunk),
        },
      ],
    ),
  );
}

export function buildProgressCard(text: string, cardTitle?: string): Record<string, unknown> {
  return buildV2Card(resolveCardTitle(cardTitle), "turquoise", [
    {
      tag: "markdown",
      content: normalizeMarkdownForFeishu(text),
    },
  ]);
}

export function buildTranscriptCard(text: string, isFinal: boolean, cardTitle?: string): Record<string, unknown> {
  return buildV2Card(resolveCardTitle(cardTitle), isFinal ? "blue" : "turquoise", [
    {
      tag: "markdown",
      content: normalizeMarkdownForFeishu(text),
    },
  ]);
}

export function buildFileCaptionCard(caption: string, fileName?: string): Record<string, unknown> {
  const normalizedCaption = normalizeMarkdownForFeishu(caption);
  const normalizedFileName = (fileName ?? "").trim();
  const content = normalizedFileName
    ? `File: \`${normalizeMarkdownForFeishu(normalizedFileName)}\`\n\n${normalizedCaption}`
    : normalizedCaption;

  return buildV2Card("File Note", "indigo", [
    {
      tag: "markdown",
      content,
    },
  ]);
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
  const summary = summarizeApprovalOperation(params.approvalType, params.operation, params.target);
  const reasonBlock = params.reason ? `\nReason: ${params.reason}` : "";
  const requestHint = params.requestId ? `\nRequest: ${params.requestId}` : "";
  const cardTitle = resolveCardTitle(params.cardTitle);
  const buttons = [
    buildApprovalButton("Approve", "primary", `approval_accept_${params.requestId}`, params.requestId, DECISION_ACCEPT),
    buildApprovalButton(
      "Approve Session",
      "default",
      `approval_accept_session_${params.requestId}`,
      params.requestId,
      DECISION_ACCEPT_FOR_SESSION,
    ),
    buildApprovalButton("Decline", "danger", `approval_decline_${params.requestId}`, params.requestId, DECISION_DECLINE),
    buildApprovalButton("Cancel", "default", `approval_cancel_${params.requestId}`, params.requestId, DECISION_CANCEL),
  ];

  return buildV2Card("Approval Required", "orange", [
    {
      tag: "markdown",
      content:
        `${cardTitle} needs approval before continuing.\n\n${summary}${reasonBlock}${requestHint}\n\n` +
        `Timeout: ${params.timeoutSeconds}s`,
    },
    ...buttons,
  ]);
}

export function buildApprovalResolvedCard(params: {
  requestId: string;
  decision: string;
  message?: string;
}): Record<string, unknown> {
  const detail = params.message ? `\n${params.message}` : "";
  return buildV2Card("Approval Resolved", "green", [
    {
      tag: "markdown",
      content: `Request: ${params.requestId}\nDecision: ${params.decision}${detail}`,
    },
  ]);
}

export function buildApprovalTimeoutCard(params: { requestId: string; timeoutSeconds: number }): Record<string, unknown> {
  return buildV2Card("Approval Timed Out", "red", [
    {
      tag: "markdown",
      content: `Request: ${params.requestId}\nDecision: cancel\nTimeout: ${params.timeoutSeconds}s`,
    },
  ]);
}

export function buildUserInputCard(params: {
  request: Record<string, unknown>;
  cardTitle?: string;
  promptTitle?: string;
}): Record<string, unknown> {
  const requestId = String(params.request.requestId ?? "");
  const prompt = buildUserInputPrompt(params.request, {
    title: params.promptTitle ?? `${resolveCardTitle(params.cardTitle)} needs your input`,
  });
  const elements: Array<Record<string, unknown>> = [
    {
      tag: "markdown",
      content: normalizeMarkdownForFeishu(prompt),
    },
  ];

  if (canUseNativeSingleChoiceUserInput(params.request)) {
    const question = normalizeUserInputQuestions(params.request)[0]!;
    question.options.forEach((option, index) => {
      elements.push(buildUserInputButton(option.label, `user_input_${requestId}_${index}`, requestId, index));
    });
  }

  return buildV2Card("Input Required", "indigo", elements);
}

export function buildUserInputResolvedCard(params: {
  requestId: string;
  answerSummary?: string;
}): Record<string, unknown> {
  const detail = params.answerSummary ? `\nAnswer: ${params.answerSummary}` : "";
  return buildV2Card("Input Received", "green", [
    {
      tag: "markdown",
      content: `Request: ${params.requestId}${detail}`,
    },
  ]);
}

export function buildErrorCard(title: string, message: string): Record<string, unknown> {
  return buildV2Card(title, "red", [
    {
      tag: "markdown",
      content: normalizeMarkdownForFeishu(message),
    },
  ]);
}

function buildV2Card(
  title: string,
  template: string,
  bodyElements: Array<Record<string, unknown>>,
): Record<string, unknown> {
  return {
    schema: "2.0",
    config: {
      update_multi: true,
      width_mode: "fill",
    },
    header: {
      title: {
        tag: "plain_text",
        content: title,
      },
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
  const callbackValue = {
    kind: "userInput",
    requestId,
    optionIndex,
  };

  return {
    tag: "button",
    element_id: elementId,
    text: {
      tag: "plain_text",
      content: label.slice(0, 64),
    },
    type: "primary",
    behaviors: [
      {
        type: "callback",
        value: callbackValue,
      },
    ],
  };
}

function buildApprovalButton(
  label: string,
  type: "default" | "primary" | "danger",
  elementId: string,
  requestId: string,
  decision: string,
): Record<string, unknown> {
  const callbackValue = {
    kind: "approval",
    requestId,
    decision,
  };

  return {
    tag: "button",
    element_id: elementId,
    text: {
      tag: "plain_text",
      content: label,
    },
    type,
    behaviors: [
      {
        type: "callback",
        value: callbackValue,
      },
    ],
  };
}
