import { buildReplyCards } from "./card-builder.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuSendResult } from "./feishu-types.js";

export async function sendReplyCards(
  client: FeishuClient,
  target: string,
  replyText: string,
  cardTitle?: string,
): Promise<void> {
  const cards = buildReplyCards(replyText, cardTitle);
  for (const card of cards) {
    await client.sendInteractiveCard(target, card);
  }
}

export async function sendSingleCard(
  client: FeishuClient,
  target: string,
  card: Record<string, unknown>,
): Promise<FeishuSendResult> {
  return await client.sendInteractiveCard(target, card);
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
  target: string,
  card: Record<string, unknown>,
  messageId = "",
): Promise<{ messageId: string }> {
  if (messageId) {
    await updateCard(client, messageId, card);
    return { messageId };
  }
  return await sendSingleCard(client, target, card);
}
