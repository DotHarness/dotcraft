import type { ChannelAdapterMessageOpts } from "@dotcraft/channel";

import { directConversationTarget, parseConversationTarget } from "./conversation-target.js";
import type { FeishuCardActionEvent } from "./feishu-types.js";

export interface FeishuSender {
  senderId: string;
  senderName: string;
  senderRole: "admin" | "user";
}

export interface CommandChoiceSelection {
  command: string;
  channelContext: string;
  operatorId: string;
}

export function feishuMessageOptions(
  sender: FeishuSender,
  conversation: { threadUserId: string; channelContext: string; isGroup: boolean },
): Omit<ChannelAdapterMessageOpts, "text"> {
  return {
    userId: conversation.threadUserId,
    userName: sender.senderName,
    channelContext: conversation.channelContext,
    sender: {
      ...sender,
      ...(conversation.isGroup ? { groupId: conversation.channelContext } : {}),
    },
    omitSenderGroupId: !conversation.isGroup,
    directTarget: directConversationTarget(sender.senderId),
  };
}

export function parseCommandChoiceSelection(
  event: FeishuCardActionEvent,
  value: Record<string, unknown>,
): CommandChoiceSelection | null {
  const command = typeof value.command === "string" ? value.command.trim() : "";
  const channelContext = typeof value.channelContext === "string" ? value.channelContext : "";
  const operatorId = String(event.operator?.open_id ?? "");
  const target = parseConversationTarget(channelContext);
  if (!command.startsWith("/") || !target || !operatorId) return null;
  if (target.kind === "dm" && target.id !== operatorId) return null;
  return { command, channelContext, operatorId };
}

export function commandChoiceMessageOptions(
  selection: CommandChoiceSelection,
  senderRole: FeishuSender["senderRole"],
): Omit<ChannelAdapterMessageOpts, "text"> {
  const isGroup = parseConversationTarget(selection.channelContext)?.kind === "group";
  return feishuMessageOptions(
    { senderId: selection.operatorId, senderName: selection.operatorId, senderRole },
    {
      threadUserId: isGroup ? selection.channelContext : selection.operatorId,
      channelContext: selection.channelContext,
      isGroup,
    },
  );
}
