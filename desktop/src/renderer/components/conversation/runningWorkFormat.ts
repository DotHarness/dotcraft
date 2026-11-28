import { desktopSummaryServices } from './summary/summaryServices'
import type { RunningOtherKind, RunningWork } from './summary/store'
import { setSummaryShown } from './summary/view'

type Translate = (key: string, vars?: Record<string, string | number>) => string

function isSubagentsOnly(work: RunningWork): boolean {
  return work.subagents > 0 && work.others === 0
}

export function openRunningWork(work: RunningWork): void {
  if (isSubagentsOnly(work)) {
    desktopSummaryServices.navigation.openDetailPanel('subagents')
    return
  }
  setSummaryShown(true)
}

function subagentsLabel(t: Translate, count: number): string {
  return t(count === 1 ? 'conversation.runningWork.subagents.one' : 'conversation.runningWork.subagents.other', { count })
}

function tasksLabel(t: Translate, count: number): string {
  return t(count === 1 ? 'conversation.runningTasks.one' : 'conversation.runningTasks.other', { count })
}

function otherBackgroundLabel(t: Translate, kind: RunningOtherKind, count: number): string {
  const plural = count === 1 ? 'one' : 'other'
  switch (kind) {
    case 'shell':
      return t(`conversation.runningWork.backgroundCommands.${plural}`, { count })
    case 'workflow':
      return t(`conversation.runningWork.backgroundWorkflows.${plural}`, { count })
    default:
      return t(`conversation.runningWork.backgroundTasks.${plural}`, { count })
  }
}

export function formatRunningWorkCompact(t: Translate, work: RunningWork): string {
  return isSubagentsOnly(work) ? subagentsLabel(t, work.subagents) : tasksLabel(t, work.total)
}

export function formatRunningWorkDetailed(t: Translate, locale: string, work: RunningWork): string {
  const parts: string[] = []
  if (work.subagents > 0) parts.push(subagentsLabel(t, work.subagents))
  if (work.others > 0) parts.push(otherBackgroundLabel(t, work.othersKind, work.others))
  if (parts.length === 1) return parts[0]
  return new Intl.ListFormat(locale, { type: 'unit', style: 'short' }).format(parts)
}
