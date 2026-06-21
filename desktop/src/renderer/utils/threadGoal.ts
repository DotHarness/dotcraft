import type { ThreadGoal } from '../types/thread'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import { stringifyComposerDraftSegments } from '../components/conversation/richInputSerialization'

export interface GoalObjectiveDraft {
  text: string
  segments?: ComposerDraftSegment[]
  files?: ComposerFileAttachment[]
  images?: ImageAttachment[]
}

/**
 * Flatten composer input (rich text + attached files/images) into a single goal
 * objective string. Inline @file / $skill refs are serialized in place; files and
 * images attached out-of-band (paperclip / paste / drop) are appended as labeled
 * path references so the goal stays a plain string the model can read on every
 * continuation turn, under labeled "Referenced files" / "Referenced image files" sections.
 */
export function buildGoalObjective(draft: GoalObjectiveDraft): string {
  const segments = draft.segments && draft.segments.length > 0
    ? draft.segments
    : draft.text.length > 0
      ? [{ type: 'text', value: draft.text } as ComposerDraftSegment]
      : []
  const body = stringifyComposerDraftSegments(segments).trim()

  const sections: string[] = []
  if (body) sections.push(body)

  const filePaths = (draft.files ?? [])
    .map((file) => file.path.trim())
    .filter((path) => path.length > 0)
  if (filePaths.length > 0) {
    const lines = filePaths.map((path, index) => `- [File #${index + 1}]: ${path}`)
    sections.push(`Referenced files:\n${lines.join('\n')}`)
  }

  const imagePaths = (draft.images ?? [])
    .map((image) => (image.tempPath ?? '').trim())
    .filter((path) => path.length > 0)
  if (imagePaths.length > 0) {
    const lines = imagePaths.map((path, index) => `- [Image #${index + 1}]: ${path}`)
    sections.push(`Referenced image files:\n${lines.join('\n')}`)
  }

  return sections.join('\n\n')
}

export type GoalSlashCommand =
  | { kind: 'show' }
  | { kind: 'set'; objective: string }
  | { kind: 'pause' }
  | { kind: 'resume' }
  | { kind: 'clear' }

export function parseGoalSlashCommand(text: string): GoalSlashCommand | null {
  const trimmed = text.trim()
  if (!trimmed.toLowerCase().startsWith('/goal')) return null
  const after = trimmed.slice('/goal'.length)
  if (after.length > 0 && !/^\s/.test(after)) return null
  const args = after.trim()
  if (!args) return { kind: 'show' }
  const normalized = args.toLowerCase()
  if (normalized === 'pause') return { kind: 'pause' }
  if (normalized === 'resume') return { kind: 'resume' }
  if (normalized === 'clear') return { kind: 'clear' }
  return { kind: 'set', objective: args }
}

export function extractGoal(result: unknown): ThreadGoal | null {
  if (result == null || typeof result !== 'object') return null
  const goal = (result as { goal?: unknown }).goal
  return isThreadGoal(goal) ? goal : null
}

export function isThreadGoal(value: unknown): value is ThreadGoal {
  if (value == null || typeof value !== 'object') return false
  const goal = value as Partial<ThreadGoal>
  return typeof goal.threadId === 'string'
    && typeof goal.objective === 'string'
    && typeof goal.tokensUsed === 'number'
    && typeof goal.timeUsedSeconds === 'number'
    && typeof goal.createdAt === 'number'
    && typeof goal.updatedAt === 'number'
    && (goal.status === 'active'
      || goal.status === 'paused'
      || goal.status === 'blocked'
      || goal.status === 'usageLimited'
      || goal.status === 'budgetLimited'
      || goal.status === 'complete')
}

export function formatGoalUsage(goal: ThreadGoal): string {
  const total = Math.max(0, Math.trunc(goal.tokensUsed ?? 0))
  if (typeof goal.tokenBudget === 'number' && Number.isFinite(goal.tokenBudget) && goal.tokenBudget > 0) {
    return `${formatNumber(total)} / ${formatNumber(goal.tokenBudget)} tokens`
  }
  if (total > 0) {
    return `${formatNumber(total)} tokens`
  }
  if (goal.timeUsedSeconds > 0) {
    return formatDuration(goal.timeUsedSeconds)
  }
  return ''
}

export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.trunc(seconds))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  if (hours > 0) return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`
  if (minutes > 0) return `${minutes}m`
  return `${total}s`
}

function formatNumber(value: number): string {
  return value.toLocaleString()
}
