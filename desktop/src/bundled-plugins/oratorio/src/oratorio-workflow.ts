import type { OratorioTask, TaskStage } from './oratorio-model'

export type QuickActionId =
  | 'dispatch'
  | 'implement'
  | 'auto-target'
  | 'review-only'
  | 'open-thread'
  | 'cancel-run'
  | 'retry'
  | 'review-draft'
  | 'review-delivery'
  | 'review-follow-ups'
  | 'approve'
  | 'request-changes'
  | 'reject'
  | 're-review'
  | 'archive'
  | 'reopen'

export interface QuickActionGroup {
  kind: 'start' | 'run' | 'draft' | 'decision' | 'closed'
  title: string
  description: string
  actions: QuickActionId[]
}
export const TASK_STAGES: Array<{ id: TaskStage; label: string }> = [
  { id: 'intake', label: 'Intake' },
  { id: 'analysis', label: 'Analysis' },
  { id: 'review', label: 'Review' },
  { id: 'decision', label: 'Decision' },
  { id: 'closed', label: 'Closed' },
]

export function lifecycleStageForTask(task: OratorioTask): TaskStage {
  if (task.state === 'approved' || task.state === 'rejected' || task.state === 'archived') return 'closed'
  if (task.state === 'awaiting-review') return 'review'
  if (task.state === 'dispatching' || task.state === 'running' || task.state === 'failed' || (task.run && task.run.status !== 'succeeded')) return 'analysis'
  return 'intake'
}
export function deriveQuickActionGroups(task: OratorioTask): QuickActionGroup[] {
  const groups: QuickActionGroup[] = []
  const { artifacts, capabilities } = task

  if ((artifacts.actionableReviewDrafts ?? 0) > 0) {
    groups.push({ kind: 'draft', title: 'Review the Agent draft', description: 'Resolve or publish the prepared review before recording a decision.', actions: ['review-draft'] })
  } else if ((artifacts.actionableImplementationDrafts ?? 0) > 0) {
    groups.push({ kind: 'draft', title: 'Review the delivery draft', description: 'Inspect the generated branch and choose how it should be delivered.', actions: ['review-delivery'] })
  } else if ((artifacts.actionableFollowUpDrafts ?? 0) > 0) {
    groups.push({ kind: 'draft', title: 'Review follow-up work', description: 'Edit, create, or discard the proposed follow-up tasks.', actions: ['review-follow-ups'] })
  }

  if (task.state === 'discovered') {
    const startActions: QuickActionId[] = []
    if (capabilities.implement) startActions.push('implement')
    if (capabilities.autoTarget) startActions.push('auto-target')
    if (capabilities.reviewOnly) startActions.push('review-only')
    if (!startActions.length && capabilities.dispatch) startActions.push('dispatch')
    if (startActions.length) groups.push({ kind: 'start', title: 'Start Agent work', description: 'Choose the delivery mode for this task.', actions: startActions })
  }

  if (task.state === 'dispatching' || task.state === 'running') {
    const runActions: QuickActionId[] = []
    if (task.run?.threadAvailable) runActions.push('open-thread')
    if (capabilities.cancelRun) runActions.push('cancel-run')
    groups.push({ kind: 'run', title: task.state === 'running' ? 'Agent is working' : 'Run is queued', description: task.run?.activity ?? 'Waiting for the next run update.', actions: runActions })
  }

  if (task.state === 'failed') {
    const runActions: QuickActionId[] = []
    if (capabilities.retry) runActions.push('retry')
    if (task.run?.threadAvailable) runActions.push('open-thread')
    groups.push({ kind: 'run', title: 'Run needs attention', description: task.actionError ?? task.run?.activity ?? 'The run stopped before completion.', actions: runActions })
  }

  if (task.state === 'awaiting-review') {
    groups.push({ kind: 'decision', title: 'Review decision', description: capabilities.decide ? 'Accept the result, return it with feedback, or reject it.' : 'Decision actions are unavailable with the current policy.', actions: ['approve', 'request-changes', 'reject'] })
  }

  if (task.state === 'approved' || task.state === 'rejected') {
    const closedActions: QuickActionId[] = []
    if (capabilities.reReview) closedActions.push('re-review')
    if (capabilities.archive) closedActions.push('archive')
    if (closedActions.length) groups.push({ kind: 'closed', title: 'Completed work', description: 'File the result away or review a newer source revision.', actions: closedActions })
  }

  if (task.state === 'archived' && capabilities.reopen) {
    groups.push({ kind: 'closed', title: 'Archived task', description: 'Return this task to the active board when more work is required.', actions: ['reopen'] })
  }

  return groups
}
