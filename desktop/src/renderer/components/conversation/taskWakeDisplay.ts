import { Bot, SquareTerminal, Workflow } from 'lucide-react'
import { translate } from '../../../shared/locales'
import type { AppLocale } from '../../../shared/locales/types'
import { subAgentEndingOutcome } from '../../utils/subAgentEndingDisplay'
import type { TaskWake, TaskWakeKind } from './turnActivityModel'

export const TASK_WAKE_ICONS: Record<TaskWakeKind, typeof SquareTerminal> = {
  terminal: SquareTerminal,
  subagent: Bot,
  workflow: Workflow
}

export const TASK_WAKE_COPY: Record<TaskWakeKind, { param: string; show: string; started: string; generic: string; statuses: Record<string, string> }> = {
  terminal: {
    param: 'command',
    show: 'conversation.showBackgroundCommand',
    started: 'conversation.startedByBackgroundCommand',
    generic: 'conversation.startedByBackgroundCommandGeneric',
    statuses: {
      completed: 'conversation.backgroundCommandFinished',
      failed: 'conversation.backgroundCommandFailed',
      killed: 'conversation.backgroundCommandStopped',
      waitingForInput: 'conversation.backgroundCommandWaitingForInput'
    }
  },
  subagent: {
    param: 'name',
    show: 'conversation.showSubagent',
    started: 'conversation.startedBySubagent',
    generic: 'conversation.startedBySubagentGeneric',
    statuses: {
      completed: 'conversation.subagentFinished',
      failed: 'conversation.subagentFailed',
      cancelled: 'conversation.subagentStopped'
    }
  },
  workflow: {
    param: 'name',
    show: 'conversation.showWorkflow',
    started: 'conversation.startedByWorkflow',
    generic: 'conversation.startedByWorkflowGeneric',
    statuses: {
      succeeded: 'conversation.workflowFinished',
      failed: 'conversation.workflowFailed',
      cancelled: 'conversation.workflowFailed',
      interrupted: 'conversation.workflowFailed',
      stopped: 'conversation.workflowStopped'
    }
  }
}

export function taskWakeLabel(locale: AppLocale, { kind, label, status }: TaskWake): string {
  const copy = TASK_WAKE_COPY[kind]
  if (!label) return translate(locale, copy.generic)
  return translate(locale, (status && copy.statuses[status]) || copy.started, { [copy.param]: label })
}

/** A finished ending is not claimed while the child is still working. */
export function settledSubAgentWake(wake: TaskWake, child: Parameters<typeof subAgentEndingOutcome>[1]): TaskWake {
  return subAgentEndingOutcome(wake.status, child) === null ? { ...wake, status: undefined } : wake
}
