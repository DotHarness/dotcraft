import { textPart } from "./models.js";
import type { InputPart, SenderContext as ContractSenderContext } from "./generated/appserver/index.js";
import type { RunInput, SenderContext, DynamicToolBinding, DynamicToolSpec } from "./dotcraft.js";

export function stripRuntimeDynamicToolHandlers(tools: DynamicToolBinding[] | undefined): DynamicToolSpec[] | undefined {
  if (tools === undefined) return undefined;
  return tools.map((declaration) => {
    if (declaration.type === "namespace") {
      return {
        ...declaration,
        tools: declaration.tools.map(({ handler: _handler, ...tool }) => tool),
      };
    }
    const { handler: _handler, ...tool } = declaration;
    return tool;
  });
}



export function normalizeRunInput(input: RunInput, sender?: SenderContext): { input: InputPart[]; sender?: SenderContext } {
  if (typeof input === "string") return { input: [textPart(input)], sender };
  if (Array.isArray(input)) return { input, sender };
  return { input: input.input, sender: input.sender ?? sender };
}

export function toContractSender(sender: SenderContext | undefined): ContractSenderContext | undefined {
  if (!sender) return undefined;
  return {
    ...sender,
    senderName: sender.senderName ?? sender.senderId,
    senderRole: sender.senderRole ?? "user",
  };
}
