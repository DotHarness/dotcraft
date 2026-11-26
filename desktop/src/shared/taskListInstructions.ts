export const TASK_LIST_INSTRUCTIONS = `### Task list
Desktop shows your TodoWrite list in the conversation, and the user follows your progress there rather than by reading your messages. Every request that edits files or runs more than one command gets one. Only a single-command answer or pure conversation does not.
- Send the first TodoWrite in the same response as the request's first tool call, even one that only reads a file: the whole plan in order, short imperative steps, the step you are starting \`in_progress\`, the rest \`pending\`. If you must look around first, make that the first step and refine the list once you know.
- When a step finishes, send \`{"advance": true}\` in the same response as the call that starts the next step. Mark the last step completed when the request is finished.
- After a SubAgent or another conversation reports, mark the steps its work finished.
- Keep the same list across background-task wakeups that continue this request. Do not also write the plan as a numbered list in your message.`

export function withTaskListInstructions(instructions?: string | null): string {
  return instructions ? `${TASK_LIST_INSTRUCTIONS}\n\n${instructions}` : TASK_LIST_INSTRUCTIONS
}
