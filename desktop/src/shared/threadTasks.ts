export type NewTaskPromptMode = 'ask' | 'newThread' | 'here'

export type ThreadTaskReasoningEffort = 'low' | 'medium' | 'high' | 'extraHigh' | 'max' | 'ultra'

export type ThreadTaskKind = 'offer' | 'proposal'

export type ThreadTaskState =
  | 'pending'
  | 'accepted'
  | 'starting'
  | 'started'
  | 'keptHere'
  | 'dismissed'
  | 'notAnswered'
  | 'failed'
  | 'expired'

export interface ThreadTaskDefaults {
  model?: string
  reasoningEffort?: ThreadTaskReasoningEffort
}

export interface ThreadTask {
  id: string
  threadId: string
  workspacePath: string
  kind: ThreadTaskKind
  state: ThreadTaskState
  title: string
  prompt?: string
  background?: string
  defaults: ThreadTaskDefaults
  toolCallId?: string
  turnId?: string
  createdAt: string
  childThreadId?: string
  error?: string
}

export type ThreadTaskWorkLocation = 'local' | 'worktree'

export type ThreadTaskApprovalPolicy = 'prompt' | 'autoApprove'

export interface ThreadTaskStartOptions {
  workLocation?: ThreadTaskWorkLocation
  baseRef?: string
  branchName?: string
  environmentPath?: string
  approvalPolicy?: ThreadTaskApprovalPolicy
  providerId?: string
  model?: string
  reasoningEffort?: ThreadTaskReasoningEffort | 'off'
}

export interface ThreadTasksSnapshot {
  threadId: string
  tasks: ThreadTask[]
}

export type ThreadOfferAnswer =
  | { choice: 'newThread'; options?: ThreadTaskStartOptions }
  | { choice: 'here' }

export interface ThreadTaskActionResult {
  ok: boolean
  error?: string
}

export interface ThreadTaskStartResult {
  taskId: string
  ok: boolean
  childThreadId?: string
  error?: string
}

export const THREAD_TASKS_CHANGED_CHANNEL = 'threadTasks:changed'

export function normalizeNewTaskPromptMode(value: unknown): NewTaskPromptMode {
  return value === 'newThread' || value === 'here' ? value : 'ask'
}

export function doThisTaskHereMessage(title: string): string {
  return `Do this task here: “${title}”`
}
