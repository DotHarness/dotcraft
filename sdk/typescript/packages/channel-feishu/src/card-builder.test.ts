import assert from "node:assert/strict";
import test from "node:test";

import {
  buildApprovalCard,
  buildReplyCards,
  buildStatusElement,
  buildStatusPatch,
  buildStreamingTranscriptCard,
  buildTranscriptCard,
  buildUserInputCard,
  resolveCardTitle,
} from "./card-builder.js";

function elementsOf(card: Record<string, unknown>): Array<Record<string, unknown>> {
  const body = (card.body as Record<string, unknown> | undefined) ?? {};
  return (body.elements as Array<Record<string, unknown>> | undefined) ?? [];
}

test("buildStreamingTranscriptCard exposes a stable markdown element and terminal state", () => {
  const streaming = buildStreamingTranscriptCard("partial", false, "Bot");
  const final = buildStreamingTranscriptCard("complete", true, "Bot");
  const streamingConfig = streaming.config as Record<string, unknown>;
  const finalConfig = final.config as Record<string, unknown>;
  const element = elementsOf(streaming)[0];

  assert.equal(streamingConfig.streaming_mode, true);
  assert.equal(finalConfig.streaming_mode, false);
  assert.deepEqual(streamingConfig.locales, ["en_us", "zh_cn", "ja_jp", "ko_kr", "es_es", "fr_fr", "de_de"]);
  assert.equal(element?.element_id, "dotcraft_reply");
  assert.equal(streaming.header, undefined);
  assert.equal(final.header, undefined);
});

test("buildStreamingTranscriptCard places the status row after the reply while streaming only", () => {
  const streaming = buildStreamingTranscriptCard("", false, "Bot", { status: "thinking" });
  const final = buildStreamingTranscriptCard("done", true, "Bot", { status: "thinking" });
  const [reply, status] = elementsOf(streaming);
  const i18n = status?.i18n_content as Record<string, string>;
  const icon = status?.icon as Record<string, unknown>;

  assert.equal(status?.element_id, "dotcraft_status");
  assert.equal(reply?.element_id, "dotcraft_reply");
  assert.match(i18n.zh_cn, /思考中/);
  assert.match(i18n.en_us, /Thinking/);
  const working = buildStatusPatch("working");
  assert.match(working.i18n_content.zh_cn, /工作中/);
  assert.match(working.i18n_content.de_de, /Arbeite/);
  assert.equal("tag" in working, false);
  assert.equal(icon.tag, "standard_icon");
  assert.equal(elementsOf(final).length, 1);
  assert.equal(elementsOf(final)[0]?.element_id, "dotcraft_reply");
});

test("buildStatusElement uses the animated icon when an image key exists", () => {
  const animated = buildStatusElement("thinking", "img_v3_loading");
  const icon = animated.icon as Record<string, unknown>;
  assert.equal(icon.tag, "custom_icon");
  assert.equal(icon.img_key, "img_v3_loading");
  assert.equal((buildStatusElement("working").icon as Record<string, unknown>).tag, "standard_icon");
});

test("titled cards and buttons carry every supported locale", () => {
  const approval = buildApprovalCard({
    requestId: "r1",
    approvalType: "shell",
    operation: "rm -rf build",
    target: "",
    reason: "cleanup",
    timeoutSeconds: 10,
    cardTitle: "Bot",
  });
  const header = (approval.header as Record<string, unknown>).title as Record<string, unknown>;
  const i18n = header.i18n_content as Record<string, string>;
  assert.deepEqual(Object.keys(i18n).sort(), ["de_de", "en_us", "es_es", "fr_fr", "ja_jp", "ko_kr", "zh_cn"]);
  assert.equal(header.content, "Approval Required");
  assert.equal(i18n.zh_cn, "需要审批");
  const [body, ...buttons] = elementsOf(approval);
  const bodyI18n = body?.i18n_content as Record<string, string>;
  assert.match(bodyI18n.zh_cn, /Bot 需要你的审批才能继续。/);
  assert.match(bodyI18n.zh_cn, /命令：`rm -rf build`/);
  assert.match(bodyI18n.ja_jp, /理由：cleanup/);
  assert.match(String(body?.content), /Timeout: 10s/);
  assert.equal(buttons.length, 4);
  assert.equal(((buttons[0]?.text as Record<string, unknown>).i18n_content as Record<string, string>).fr_fr, "Approuver");
  assert.deepEqual((approval.config as Record<string, unknown>).locales, ["en_us", "zh_cn", "ja_jp", "ko_kr", "es_es", "fr_fr", "de_de"]);
});

test("user input card renders the question hints in every locale", () => {
  const card = buildUserInputCard({
    request: {
      requestId: "req-2",
      questions: [{ id: "q", header: "Pick", question: "Which?", options: [{ label: "A" }], isOther: true }],
    },
    cardTitle: "Bot",
    questionPosition: { index: 1, count: 3 },
  });
  const body = elementsOf(card)[0];
  const i18n = body?.i18n_content as Record<string, string>;
  assert.match(i18n.en_us, /Bot needs your input \(2\/3\)/);
  assert.match(i18n.zh_cn, /0\) 其他 \/ 自由输入/);
  assert.match(i18n.de_de, /Antworten Sie mit einer Optionsnummer oder/);
});

test("reply and transcript cards carry no header while titled cards keep theirs", () => {
  assert.equal(buildTranscriptCard("hello", false).header, undefined);
  assert.equal(buildReplyCards("hello")[0]?.header, undefined);
  const approval = buildApprovalCard({
    requestId: "r1",
    approvalType: "file",
    operation: "read",
    target: "a.txt",
    reason: "",
    timeoutSeconds: 10,
  });
  assert.notEqual(approval.header, undefined);
});

test("invalid card title falls back to default title", () => {
  const fallback = resolveCardTitle(undefined);
  assert.equal(resolveCardTitle(""), fallback);
  assert.equal(resolveCardTitle("   "), fallback);
  assert.equal(resolveCardTitle("A".repeat(49)), fallback);
  assert.equal(resolveCardTitle("Bot"), "Bot");
});

test("buildUserInputCard uses native callback buttons for single-choice questions", () => {
  const card = buildUserInputCard({
    request: {
      requestId: "req-1",
      questions: [
        {
          id: "mode",
          header: "Pick a mode",
          question: "Which mode?",
          options: [{ label: "Auto" }, { label: "Manual" }],
        },
      ],
    },
  });
  const body = (card.body as Record<string, unknown> | undefined) ?? {};
  const elements = (body.elements as Array<Record<string, unknown>> | undefined) ?? [];
  const buttons = elements.filter((element) => element.tag === "button");

  assert.equal(buttons.length, 2);
  assert.deepEqual(buttons[1]?.behaviors, [
    {
      type: "callback",
      value: {
        kind: "userInput",
        requestId: "req-1",
        optionIndex: 1,
      },
    },
  ]);
});
