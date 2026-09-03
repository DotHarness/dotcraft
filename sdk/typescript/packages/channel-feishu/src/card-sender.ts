import { buildReplyCards } from "./card-builder.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuSendResult } from "./feishu-types.js";
import type { FeishuOutboundRouter } from "./outbound-router.js";

export async function sendReplyCards(
  router: FeishuOutboundRouter,
  target: string,
  replyText: string,
  cardTitle?: string,
): Promise<void> {
  const cards = buildReplyCards(replyText, cardTitle);
  for (const card of cards) {
    await router.sendCard(target, card);
  }
}

export async function updateCard(
  client: FeishuClient,
  messageId: string,
  card: Record<string, unknown>,
): Promise<void> {
  await client.updateInteractiveCard(messageId, card);
}

export async function createOrUpdateCard(
  client: FeishuClient,
  router: FeishuOutboundRouter,
  target: string,
  card: Record<string, unknown>,
  messageId = "",
): Promise<{ messageId: string }> {
  if (messageId) {
    await updateCard(client, messageId, card);
    return { messageId };
  }
  return await router.sendCard(target, card);
}

export type { FeishuSendResult };
