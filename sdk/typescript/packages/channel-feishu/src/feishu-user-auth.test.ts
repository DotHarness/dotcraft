import assert from "node:assert/strict";
import test from "node:test";

import { FeishuAdapter } from "./feishu-adapter.js";
import type { FeishuDeviceAuthorization } from "./feishu-user-identity.js";
import type { ParsedInboundMessage } from "./feishu-types.js";

type SentCard = { target: string; body: string };

const AUTHORIZATION: FeishuDeviceAuthorization = {
  deviceCode: "device-code",
  userCode: "ABCD-1234",
  verificationUriComplete: "https://example.invalid/device?code=ABCD-1234",
  intervalMs: 1,
  expiresAt: Date.now() + 60_000,
};

type AuthCommandTarget = { handleUserAuthCommand: (message: ParsedInboundMessage) => Promise<void> };

function createAdapter(identity: Record<string, unknown>) {
  const cards: SentCard[] = [];
  const adapter = Object.create(FeishuAdapter.prototype) as AuthCommandTarget;
  Object.assign(adapter, {
    userIdentity: identity,
    userAuthAbortController: new AbortController(),
    router: {
      sendCard: async (target: string, card: Record<string, unknown>) => {
        const body = card.body as { elements: Array<{ content?: string }> };
        cards.push({ target, body: body.elements[0]?.content ?? "" });
      },
    },
  });
  return { adapter, cards };
}

function inbound(text: string, chatType: "p2p" | "group" = "p2p"): ParsedInboundMessage {
  return {
    kind: "text",
    messageId: "om_auth",
    chatType,
    channelContext: chatType === "p2p" ? "dm:ou_operator" : "group:oc_team",
    threadUserId: "ou_operator",
    userId: "ou_operator",
    userName: "Operator",
    text,
    parts: [],
  } as unknown as ParsedInboundMessage;
}

test("authorization is refused outside a direct message and when personal access is off", async () => {
  const configured = { isConfigured: () => true };
  const group = createAdapter(configured);
  await group.adapter.handleUserAuthCommand(inbound("/feishu-auth", "group"));
  assert.equal(group.cards.length, 1);
  assert.ok(group.cards[0]?.body.includes("direct message"));

  const disabled = createAdapter({ isConfigured: () => false });
  await disabled.adapter.handleUserAuthCommand(inbound("/feishu-auth"));
  assert.ok(disabled.cards[0]?.body.includes("turned off"));
});

test("status and revoke report the current binding without starting a device flow", async () => {
  let requested = 0;
  let cleared = 0;
  const identity = {
    isConfigured: () => true,
    getBinding: () => ({ openId: "ou_operator", name: "Operator", scopes: [], authorizedAt: "" }),
    clearBinding: () => { cleared += 1; },
    requestAuthorization: async () => { requested += 1; return AUTHORIZATION; },
  };
  const bound = createAdapter(identity);
  await bound.adapter.handleUserAuthCommand(inbound("/feishu-auth status"));
  assert.ok(bound.cards[0]?.body.includes("Operator"));

  await bound.adapter.handleUserAuthCommand(inbound("/feishu-auth revoke"));
  assert.equal(cleared, 1);
  assert.ok(bound.cards[1]?.body.includes("Removed the stored authorization"));
  assert.equal(requested, 0);

  const unbound = createAdapter({ ...identity, getBinding: () => null });
  await unbound.adapter.handleUserAuthCommand(inbound("/feishu-auth status"));
  assert.ok(unbound.cards[0]?.body.includes("No account is authorized"));
});

test("a direct message starts the device flow and reports the bound account", async () => {
  let markSettled = (): void => {};
  const settled = new Promise<void>((resolveSettled) => { markSettled = resolveSettled; });
  const { adapter, cards } = createAdapter({
    isConfigured: () => true,
    getBinding: () => null,
    requestAuthorization: async () => AUTHORIZATION,
    waitForAuthorization: async () => {
      markSettled();
      return { openId: "ou_operator", name: "Operator", scopes: ["offline_access"] };
    },
  });

  await adapter.handleUserAuthCommand(inbound("/feishu-auth"));
  await settled;
  await new Promise((resolveTick) => setImmediate(resolveTick));

  assert.equal(cards[0]?.target, "dm:ou_operator");
  assert.ok(cards[0]?.body.includes("https://example.invalid/device?code=ABCD-1234"));
  assert.ok(cards[0]?.body.includes("ABCD-1234"));
  assert.ok(cards[1]?.body.includes("Operator"));
});

test("a failed device flow tells the operator to try again", async () => {
  const { adapter, cards } = createAdapter({
    isConfigured: () => true,
    getBinding: () => null,
    requestAuthorization: async () => { throw new Error("unreachable"); },
  });

  await adapter.handleUserAuthCommand(inbound("/feishu-auth"));
  assert.equal(cards.length, 1);
  assert.ok(cards[0]?.body.includes("did not finish"));
});
