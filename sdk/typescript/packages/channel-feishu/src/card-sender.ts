import { buildReplyCards } from "./card-builder.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuSendResult } from "./feishu-types.js";
import type { FeishuOutboundRouter } from "./outbound-router.js";

export async function sendReplyCards(
  router: FeishuOutboundRouter,
  target: string,
  replyText: string,
  cardTitle?: string,
): Promise<FeishuSendResult[]> {
  const sent: FeishuSendResult[] = [];
  for (const card of buildReplyCards(replyText, cardTitle)) {
    sent.push(await router.sendCard(target, card));
  }
  return sent;
}

export async function updateCard(
  client: FeishuClient,
  messageId: string,
  card: Record<string, unknown>,
): Promise<void> {
  await client.updateInteractiveCard(messageId, card);
}

export type { FeishuSendResult };
