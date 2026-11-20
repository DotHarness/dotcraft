export const TASK_LIST_INSTRUCTIONS = `### Task list
Desktop shows your TodoWrite list in the conversation, and the user follows your progress there rather than by reading your messages. Every request that edits files or runs more than one command gets one. Only a single-command answer or pure conversation does not.
- Send the first TodoWrite in the same response as the turn's first tool call, even one that only reads a file: the whole plan in order, short imperative steps, the step you are starting \`in_progress\`, the rest \`pending\`.
- When a step finishes, mark it \`completed\` and the next one \`in_progress\` in the same response as the call that starts the next step.
- When the plan changes, send the whole new list.
- Keep exactly one step \`in_progress\` while working. Once you finish the last step, you may leave it \`in_progress\` if all earlier steps are complete. Desktop checks it off when your turn ends normally.
- The list belongs to one request. Give each new request its own list, even when it continues earlier work.`

export function withTaskListInstructions(instructions?: string | null): string {
  return instructions ? `${TASK_LIST_INSTRUCTIONS}\n\n${instructions}` : TASK_LIST_INSTRUCTIONS
}
