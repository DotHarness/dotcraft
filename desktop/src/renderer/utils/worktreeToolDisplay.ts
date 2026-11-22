import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { extractPartialJsonStringValue, getFilename } from './toolCallDisplay'

export type WorktreeToolOperation = 'enter' | 'exit'

export interface WorktreeToolDisplay {
  operation: WorktreeToolOperation
  label: string
  branchName: string | null
  baseRef: string | null
  worktreePath: string | null
  checkoutPath: string | null
  error: string | null
}

interface StructuredWorktreeResult {
  outcome: 'entered' | 'kept' | 'removed' | null
  branchName: string | null
  path: string | null
  checkoutPath: string | null
  baseRef: string | null
}

export function formatWorktreeStreamingLabel(
  operation: WorktreeToolOperation,
  rawArgs: string | null | undefined,
  locale: AppLocale
): string {
  const raw = rawArgs ?? ''
  return formatRunningLabel(
    operation,
    nonBlank(extractPartialJsonStringValue(raw, 'name')),
    nonBlank(extractPartialJsonStringValue(raw, 'path')),
    extractPartialJsonStringValue(raw, 'action') === 'remove',
    locale
  )
}

export function getWorktreeToolDisplay(
  item: ConversationItem,
  operation: WorktreeToolOperation,
  running: boolean,
  locale: AppLocale
): WorktreeToolDisplay {
  const args = item.arguments
  const structured = running ? null : readStructuredResult(item.structuredResult)
  const failed = !running && item.success === false
  const removing = structured?.outcome === 'removed' || nonBlank(args?.action) === 'remove'
  const branchName = structured?.branchName ?? nonBlank(args?.name)
  const worktreePath = structured?.path ?? nonBlank(args?.path)

  const label = running
    ? formatRunningLabel(operation, branchName, worktreePath, removing, locale)
    : failed
      ? translate(locale, operation === 'enter'
          ? 'toolCall.worktree.enterFailed'
          : removing
            ? 'toolCall.worktree.removeFailed'
            : 'toolCall.worktree.exitFailed')
      : translate(locale, operation === 'enter'
          ? 'toolCall.worktree.entered'
          : removing
            ? 'toolCall.worktree.exitedRemoved'
            : 'toolCall.worktree.exitedKept')

  return {
    operation,
    label,
    branchName: running || failed ? null : branchName,
    baseRef: structured?.baseRef ?? null,
    worktreePath,
    checkoutPath: structured?.checkoutPath ?? null,
    error: failed ? cleanError(nonBlank(item.errorMessage) ?? nonBlank(item.result)) : null
  }
}

function formatRunningLabel(
  operation: WorktreeToolOperation,
  branchName: string | null,
  worktreePath: string | null,
  removing: boolean,
  locale: AppLocale
): string {
  if (operation === 'exit') {
    return translate(locale, removing ? 'toolCall.worktree.removing' : 'toolCall.worktree.leaving')
  }
  const target = branchName ?? (worktreePath ? getFilename(worktreePath) : null)
  return target
    ? translate(locale, 'toolCall.worktree.entering', { target })
    : translate(locale, 'toolCall.worktree.enteringGeneric')
}

function readStructuredResult(value: unknown): StructuredWorktreeResult | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const record = value as Record<string, unknown>
  const outcome = record.outcome === 'entered' || record.outcome === 'kept' || record.outcome === 'removed'
    ? record.outcome
    : null
  return {
    outcome,
    branchName: nonBlank(record.branchName),
    path: nonBlank(record.path),
    checkoutPath: nonBlank(record.checkoutPath),
    baseRef: nonBlank(record.baseRef)
  }
}

function nonBlank(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value.trim() : null
}

function cleanError(value: string | null): string | null {
  return value ? value.replace(/^Error:\s*/i, '').trim() : null
}
