function key(threadId: string): string {
  return `dotcraft:plan-approval-input:${threadId}`
}

export function rememberPlanApprovalInput(threadId: string, clientUserMessageId: string): void {
  try { localStorage.setItem(key(threadId), clientUserMessageId) } catch {}
}

export function readPlanApprovalInput(threadId: string): string | null {
  try { return localStorage.getItem(key(threadId)) } catch { return null }
}
