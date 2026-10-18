import assert from "node:assert/strict";
import test from "node:test";

import { FeishuAdapter } from "./feishu-adapter.js";
import type { FeishuApprovalCards } from "./approval-cards.js";
import type { ParsedInboundMessage } from "./feishu-types.js";

type CommandTestAdapter = {
  client: Record<string, unknown>;
  router: { sendCard: (target: string, card: Record<string, unknown>) => Promise<Record<string, unknown>> };
  adminUsers: Set<string>;
  approvals: FeishuApprovalCards;
  handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  handleInboundMessage: (message: ParsedInboundMessage) => Promise<void>;
  handleCardAction: (event: Record<string, unknown>) => boolean;
  renderCommandChoices: (target: string, choices: Array<Record<string, string>>, context: unknown) => Promise<void>;
};

function createAdapter(): { adapter: CommandTestAdapter; executed: Array<Record<string, unknown>>; cards: Array<Record<string, unknown>> } {
  const adapter = new FeishuAdapter() as unknown as CommandTestAdapter;
  const executed: Array<Record<string, unknown>> = [];
  const cards: Array<Record<string, unknown>> = [];
  adapter.adminUsers = new Set(["ou_admin"]);
  adapter.router.sendCard = async (_target, card) => {
    cards.push(card);
    return { messageId: `om_${cards.length}` };
  };
  adapter.client.commandList = async () => [{ name: "/new", aliases: [] }, { name: "/model", aliases: [] }];
  adapter.client.threadList = async () => [];
  adapter.client.threadStart = async () => ({ id: "thread-1", status: "active" });
  adapter.client.request = async (method: string) => {
    if (method === "app/channelBinding/resolve") return { binding: null };
    throw new Error(`unexpected request ${method}`);
  };
  adapter.client.commandExecute = async (params: Record<string, unknown>) => {
    executed.push(params);
    return { handled: true };
  };
  return { adapter, executed, cards };
}

function groupMessage(userId: string, text: string): ParsedInboundMessage {
  return {
    kind: "text",
    userId,
    userName: userId,
    threadUserId: "group:oc_team",
    channelContext: "group:oc_team",
    chatId: "oc_team",
    chatType: "group",
    text,
    parts: [{ type: "text", text }],
    messageId: `om_${text}`,
    mentions: [],
    sender: { openId: userId },
  };
}

function senderOf(params: Record<string, unknown> | undefined): Record<string, unknown> {
  return (params?.sender ?? {}) as Record<string, unknown>;
}

test("Feishu /new reaches the server as a command with the sender's role", async () => {
  const { adapter, executed } = createAdapter();

  await adapter.handleInboundMessage(groupMessage("ou_admin", "/new"));
  await adapter.handleInboundMessage(groupMessage("ou_member", "/new"));

  assert.deepEqual(executed.map((params) => params.command), ["/new", "/new"]);
  assert.deepEqual(executed.map((params) => senderOf(params).senderRole), ["admin", "user"]);
});

test("Feishu choice cards run the selected command for the operator", async () => {
  const { adapter, executed, cards } = createAdapter();

  await adapter.renderCommandChoices("group:oc_team", [{ label: "Mini", command: "/model mini" }], {});
  const elements = (cards[0]?.body as { elements: Array<{ behaviors: Array<{ value: Record<string, unknown> }> }> }).elements;
  const value = elements[0]!.behaviors[0]!.value;

  assert.equal(adapter.handleCardAction({ operator: { open_id: "ou_admin" }, action: { value } }), true);
  for (let i = 0; i < 50 && executed.length === 0; i += 1) {
    await new Promise((resolve) => setTimeout(resolve, 5));
  }

  assert.equal(executed[0]?.command, "/model");
  assert.deepEqual(executed[0]?.arguments, ["mini"]);
  assert.equal(senderOf(executed[0]).senderId, "ou_admin");
  assert.equal(senderOf(executed[0]).senderRole, "admin");
  assert.equal(adapter.handleCardAction({
    operator: { open_id: "ou_other" },
    action: { value: { ...value, channelContext: "dm:ou_admin" } },
  }), false);
});

test("Feishu /approve aliases resolve the chat's pending approval card", async () => {
  const { adapter } = createAdapter();
  const forwarded: string[] = [];
  adapter.handleMessage = async (opts) => {
    forwarded.push(String(opts.text));
  };
  const originalWarn = console.warn;
  console.warn = () => {};
  try {
    const decision = adapter.approvals.request("group:oc_team", { requestId: "req-1", operation: "rm" });
    await new Promise((resolve) => setTimeout(resolve, 0));

    await adapter.handleInboundMessage(groupMessage("ou_member", "yes"));
    await adapter.handleInboundMessage(groupMessage("ou_member", "/deny"));

    assert.equal(await decision, "decline");
    assert.deepEqual(forwarded, ["yes"]);
  } finally {
    console.warn = originalWarn;
  }
});
