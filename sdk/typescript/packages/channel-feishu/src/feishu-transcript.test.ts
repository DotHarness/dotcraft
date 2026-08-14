import assert from "node:assert/strict";
import test from "node:test";

import { DECISION_ACCEPT } from "@dotcraft/channel";
import type { SessionThread, SessionTurn } from "@dotcraft/channel/runtime";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuSendResult } from "./feishu-types.js";
import { FeishuAdapter } from "./feishu-adapter.js";
import { normalizeMarkdownForFeishu } from "./formatting.js";
import { twoApprovalFileSendFixture, type WireEventFixture } from "./transcript-test-fixtures.js";

class MockFeishuClient {
  readonly sentCards: Array<{ target: string; card: Record<string, unknown>; messageId: string }> = [];
  readonly updatedCards: Array<{ messageId: string; card: Record<string, unknown> }> = [];
  private seq = 0;

  async sendInteractiveCard(target: string, card: Record<string, unknown>): Promise<FeishuSendResult> {
    const messageId = `om_test_${++this.seq}`;
    this.sentCards.push({ target, card, messageId });
    return { messageId, chatId: target };
  }

  async updateInteractiveCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    this.updatedCards.push({ messageId, card });
  }

  async sendFile(
    target: string,
    _file: {
      fileName: string;
      data: Buffer;
      mediaType?: string;
    },
  ): Promise<FeishuSendResult & { fileKey: string }> {
    return { messageId: `file_msg_${++this.seq}`, chatId: target, fileKey: `file_key_${this.seq}` };
  }
}

class FailingCardKitClient extends MockFeishuClient {
  constructor(
    private readonly failAt: "create" | "update",
  ) {
    super();
  }

  async createCardKitInstance(_card: Record<string, unknown>): Promise<string> {
    if (this.failAt === "create") throw new Error("CardKit unavailable");
    return "card-test-1";
  }

  async sendCardKitReference(target: string, _cardId: string): Promise<FeishuSendResult> {
    return { messageId: "stream-message-1", chatId: target };
  }

  async updateCardKitElement(): Promise<void> {
    if (this.failAt === "update") throw new Error("CardKit update unavailable");
  }

  async finalizeCardKitInstance(): Promise<void> {}

  async replaceCardKitInstance(): Promise<void> {
    throw new Error("CardKit replacement unavailable");
  }
}

function createTestAdapter(mockFeishu: MockFeishuClient): {
  adapter: FeishuAdapter;
  client: {
    turnStart: () => Promise<SessionTurn>;
    streamEvents: () => AsyncIterableIterator<{ method: string; params: Record<string, unknown> }>;
  };
} {
  const adapter = new FeishuAdapter();
  const client = {} as {
    turnStart: () => Promise<SessionTurn>;
    streamEvents: () => AsyncIterableIterator<{ method: string; params: Record<string, unknown> }>;
  };
  Object.assign(adapter as unknown as Record<string, unknown>, {
    client,
    feishu: mockFeishu as unknown as FeishuClient,
    approvalTimeoutMs: 2000,
    streamingEnabled: false,
  });
  return { adapter, client };
}

function asEventStream(events: WireEventFixture[]): AsyncIterableIterator<{ method: string; params: Record<string, unknown> }> {
  return (async function* () {
    for (const event of events) yield event;
  })();
}

function getCardMarkdown(card: Record<string, unknown>): string {
  const elements = ((card.body as Record<string, unknown> | undefined)?.elements as Array<Record<string, unknown>> | undefined) ?? [];
  for (const element of elements) {
    if (element.tag === "markdown") return String(element.content ?? "");
  }
  return "";
}

function getCardTitle(card: Record<string, unknown>): string {
  const header = (card.header as Record<string, unknown> | undefined) ?? {};
  const title = (header.title as Record<string, unknown> | undefined) ?? {};
  return String(title.content ?? "");
}

function latestTranscriptText(mock: MockFeishuClient): string {
  for (let idx = mock.updatedCards.length - 1; idx >= 0; idx -= 1) {
    const card = mock.updatedCards[idx]?.card;
    if (card && getCardTitle(card) === "DotCraft") return getCardMarkdown(card);
  }
  for (let idx = mock.sentCards.length - 1; idx >= 0; idx -= 1) {
    const card = mock.sentCards[idx]?.card;
    if (card && getCardTitle(card) === "DotCraft") return getCardMarkdown(card);
  }
  return "";
}

function latestCardByTitle(mock: MockFeishuClient, title: string): Record<string, unknown> | null {
  for (let idx = mock.updatedCards.length - 1; idx >= 0; idx -= 1) {
    const card = mock.updatedCards[idx]?.card;
    if (card && getCardTitle(card) === title) return card;
  }
  for (let idx = mock.sentCards.length - 1; idx >= 0; idx -= 1) {
    const card = mock.sentCards[idx]?.card;
    if (card && getCardTitle(card) === title) return card;
  }
  return null;
}

test("Feishu adapter keeps one evolving transcript card across a multi-segment flow", async () => {
  const mockFeishu = new MockFeishuClient();
  const { adapter, client } = createTestAdapter(mockFeishu);
  (adapter as unknown as { getOrCreateThread: (...args: unknown[]) => Promise<SessionThread> }).getOrCreateThread = async () =>
    ({ id: twoApprovalFileSendFixture.threadId, status: "active" } as SessionThread);
  client.turnStart = async () => ({
    id: twoApprovalFileSendFixture.turnId,
    threadId: twoApprovalFileSendFixture.threadId,
    status: "running",
    startedAt: "2026-01-01T00:00:00.000Z",
  });
  client.streamEvents = () => asEventStream(twoApprovalFileSendFixture.events);

  await (adapter as unknown as { processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void> }).processMessage("u:c", {
    userId: "u",
    userName: "tester",
    text: "send file",
    channelContext: twoApprovalFileSendFixture.channelContext,
  });

  assert.equal(mockFeishu.sentCards.length, 1);
  assert.ok(mockFeishu.updatedCards.length >= 1);
  assert.equal(latestTranscriptText(mockFeishu), normalizeMarkdownForFeishu(twoApprovalFileSendFixture.expectedFinalTranscript));
});

test("Feishu adapter keeps approval card separate from transcript content", async () => {
  const mockFeishu = new MockFeishuClient();
  const { adapter } = createTestAdapter(mockFeishu);

  await (adapter as unknown as {
    onSegmentCompleted: (
      threadId: string,
      turnId: string,
      segmentText: string,
      isFinal: boolean,
      channelContext: string,
    ) => Promise<void>;
  }).onSegmentCompleted("thread-approval", "turn-approval", "这是正文 transcript。", false, "dm:test-user");
  (adapter as unknown as { onThreadContextBound: (threadId: string, channelContext: string) => void }).onThreadContextBound("thread-approval", "dm:test-user");

  const pending = adapter.onApprovalRequest({
    requestId: "request-1",
    threadId: "thread-approval",
    approvalType: "file",
    operation: "read",
    target: "C:\\Untitled.xml",
    reason: "Need user approval",
  });
  await new Promise<void>((resolve) => setImmediate(resolve));
  const handled = adapter.handleCardAction({
    action: { value: { kind: "approval", requestId: "request-1", decision: DECISION_ACCEPT } },
  });
  const decision = await pending;

  assert.equal(handled, true);
  assert.equal(decision, DECISION_ACCEPT);
  assert.ok(mockFeishu.updatedCards.length >= 1);
  assert.equal(latestTranscriptText(mockFeishu), normalizeMarkdownForFeishu("这是正文 transcript。"));
});

test("Feishu adapter preserves AgentMessage block boundaries through final reconciliation", async () => {
  const mockFeishu = new MockFeishuClient();
  const { adapter } = createTestAdapter(mockFeishu);
  const hooks = adapter as unknown as {
    onReplyProgress: (
      threadId: string,
      turnId: string,
      replyParts: readonly string[],
      isFinal: boolean,
      channelContext: string,
    ) => Promise<void>;
    onSegmentCompleted: (
      threadId: string,
      turnId: string,
      segmentText: string,
      isFinal: boolean,
      channelContext: string,
    ) => Promise<void>;
    onTurnCompleted: (
      threadId: string,
      turnId: string,
      replyText: string,
      channelContext: string,
      segmentsWereDelivered: boolean,
    ) => Promise<void>;
  };

  await hooks.onReplyProgress("thread-layout", "turn-layout", ["Intro sentence.", "## Heading\n\nBody"], true, "dm:test-user");
  await hooks.onSegmentCompleted("thread-layout", "turn-layout", "## Heading\n\nBody", true, "dm:test-user");
  await hooks.onTurnCompleted("thread-layout", "turn-layout", "Intro sentence.## Heading\n\nBody", "dm:test-user", true);

  assert.equal(latestTranscriptText(mockFeishu), "Intro sentence.\n\n## Heading\n\nBody");
});

for (const failure of ["create", "update"] as const) {
  test(`Feishu adapter falls back to a complete standard card after CardKit ${failure} failure`, async () => {
    const mockFeishu = new FailingCardKitClient(failure);
    const { adapter } = createTestAdapter(mockFeishu);
    Object.assign(adapter as unknown as Record<string, unknown>, { streamingEnabled: true });
    const hooks = adapter as unknown as {
      onReplyProgress: (
        threadId: string,
        turnId: string,
        replyParts: readonly string[],
        isFinal: boolean,
        channelContext: string,
      ) => Promise<void>;
      onSegmentCompleted: (
        threadId: string,
        turnId: string,
        segmentText: string,
        isFinal: boolean,
        channelContext: string,
      ) => Promise<void>;
    };

    await hooks.onReplyProgress("thread-fallback", `turn-${failure}`, ["Intro."], false, "dm:test-user");
    await hooks.onReplyProgress(
      "thread-fallback",
      `turn-${failure}`,
      ["Intro.", "## Result\n\nComplete"],
      true,
      "dm:test-user",
    );
    await hooks.onSegmentCompleted(
      "thread-fallback",
      `turn-${failure}`,
      "## Result\n\nComplete",
      true,
      "dm:test-user",
    );

    assert.equal(latestTranscriptText(mockFeishu), "Intro.\n\n## Result\n\nComplete");
  });
}

test("Feishu adapter sends file caption in a separate card and keeps transcript clean", async () => {
  const mockFeishu = new MockFeishuClient();
  const { adapter } = createTestAdapter(mockFeishu);

  await (adapter as unknown as {
    onSegmentCompleted: (
      threadId: string,
      turnId: string,
      segmentText: string,
      isFinal: boolean,
      channelContext: string,
    ) => Promise<void>;
  }).onSegmentCompleted("thread-caption", "turn-caption", "这是 agent 正文。", false, "dm:test-user");

  const result = await (adapter as unknown as {
    onSend: (target: string, message: Record<string, unknown>, metadata: Record<string, unknown>) => Promise<Record<string, unknown>>;
  }).onSend(
    "dm:test-user",
    {
      kind: "file",
      fileName: "Untitled.xml",
      caption: "这是文件说明 caption。",
      source: {
        kind: "dataBase64",
        dataBase64: Buffer.from("<xml/>", "utf-8").toString("base64"),
      },
    },
    {},
  );

  assert.equal(Boolean(result.delivered), true);
  const transcriptText = latestTranscriptText(mockFeishu);
  assert.equal(transcriptText, normalizeMarkdownForFeishu("这是 agent 正文。"));
  assert.ok(!transcriptText.includes("caption"));

  const captionCard = latestCardByTitle(mockFeishu, "File Note");
  assert.ok(captionCard, "expected a separate File Note card");
  const captionText = getCardMarkdown(captionCard!);
  assert.ok(captionText.includes("这是文件说明 caption。"));
});
