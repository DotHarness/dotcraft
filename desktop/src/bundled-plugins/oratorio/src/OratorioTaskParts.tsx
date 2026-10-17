import { CircleDot, FileText, Folder, GitPullRequest } from 'lucide-react'
import { GithubGlyph, GitlabGlyph } from './ProviderGlyphs'
import type { OratorioMessageKey, OratorioTranslate } from './oratorio-i18n'
import type { OratorioTask } from './oratorio-model'

export type TaskTone = 'error' | 'warning' | 'success' | 'info' | 'neutral'

export function taskStateKey(task: OratorioTask): OratorioMessageKey {
  if (task.cancelled && task.state !== 'rejected' && task.state !== 'archived') return 'stateCancelled'
  switch (task.state) {
    case 'dispatching': return 'stateQueued'
    case 'running': return 'stateRunning'
    case 'awaiting-review': return 'stateAwaitingReview'
    case 'approved': return 'stateApproved'
    case 'rejected': return 'stateRejected'
    case 'failed': return 'stateFailed'
    case 'archived': return 'stateArchived'
    default: return 'stateTodo'
  }
}

export function taskStateTone(task: OratorioTask): TaskTone {
  if (task.state === 'failed') return 'error'
  if (task.state === 'awaiting-review') return 'warning'
  if (task.state === 'approved') return 'success'
  if (task.state === 'running' || task.state === 'dispatching') return 'info'
  return 'neutral'
}

export function sourceLabel(task: OratorioTask, t: OratorioTranslate): string {
  if (/^[#!]\d/.test(task.sourceLabel)) return task.sourceLabel
  return t(kindKey(task))
}

export function ProviderIcon({ task }: { task: OratorioTask }): JSX.Element {
  if (task.provider === 'github') return <GithubGlyph />
  if (task.provider === 'gitlab') return <GitlabGlyph />
  return <Folder size={14} aria-hidden="true" />
}

export function KindIcon({ task }: { task: OratorioTask }): JSX.Element {
  if (task.kind === 'Pull request') return <GitPullRequest size={13} aria-hidden="true" />
  if (task.kind === 'Issue') return <CircleDot size={13} aria-hidden="true" />
  return <FileText size={13} aria-hidden="true" />
}

function kindKey(task: OratorioTask): OratorioMessageKey {
  if (task.kind === 'Pull request') return task.provider === 'gitlab' ? 'kindMergeRequest' : 'kindPullRequest'
  return task.kind === 'Issue' ? 'kindIssue' : 'kindTask'
}
