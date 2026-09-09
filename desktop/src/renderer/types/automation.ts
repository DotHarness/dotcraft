export interface AutomationSchedule {
  kind: 'at' | 'every' | 'daily' | 'weekdays' | 'weekly'
  at?: string | null
  everyMs?: number | null
  hour?: number | null
  minute?: number | null
  timeZone?: string | null
  days?: number[] | null
}
export interface AutomationInput {
  name: string
  prompt: string
  status: 'active' | 'paused' | 'completed'
  executionMode: 'thread' | 'independent'
  targetThreadId?: string | null
  workspaceMode?: 'project' | 'worktree'
  agentProfileId?: string | null
  approvalPolicy: 'workspaceScope' | 'fullAuto'
  schedule: AutomationSchedule
  notificationPolicy: 'important' | 'all' | 'failures'
}
export interface AutomationDefinition extends AutomationInput {
  workspaceMode: 'project' | 'worktree'
  id: string
  version: number
  origin?: { channel: string; userId?: string; groupId?: string; deliveryTarget?: string } | null
  createdAt: string
  updatedAt: string
  nextRunAt?: string | null
}
export interface AutomationRun {
  readAt?: string | null
  id: string
  automationId: string
  definitionVersion: number
  status: 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled' | 'interrupted'
  createdAt: string
  startedAt?: string | null
  completedAt?: string | null
  threadId?: string | null
  turnId?: string | null
  summary?: string | null
  error?: string | null
  worktree?: { path: string; branchName: string } | null
  deliveryStatus: 'pending' | 'sent' | 'skipped' | 'failed'
  deliveryError?: string | null
}
export interface AutomationPreset {
  id: string
  name: string
  prompt: string
  schedule?: AutomationSchedule | null
}
