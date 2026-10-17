import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { parseToolResultObject } from './toolCallDisplay'

type TaskKind = 'shell' | 'agent' | 'workflow'

const TASK_STOP_KEYS: Record<TaskKind, { stopped: string; stopping: string }> = {
  shell: { stopped: 'toolCall.taskStop.stoppedTerminal', stopping: 'toolCall.taskStop.stoppingTerminal' },
  agent: { stopped: 'toolCall.taskStop.stoppedSubagent', stopping: 'toolCall.taskStop.stoppingSubagent' },
  workflow: { stopped: 'toolCall.taskStop.stoppedWorkflow', stopping: 'toolCall.taskStop.stoppingWorkflow' }
}

export interface TaskStopDisplay {
  label: string
  message: string | null
}

/** Null for a failed call, which keeps the generic row and its error. */
export function getTaskStopDisplay(item: ConversationItem, running: boolean, locale: AppLocale): TaskStopDisplay | null {
  if (!running && item.success === false) return null
  const result = running ? undefined : parseToolResultObject(item.result)
  const taskId = typeof item.arguments?.taskId === 'string' ? item.arguments.taskId.trim() : ''
  const kind = isTaskKind(result?.kind) ? result.kind : kindOfTaskId(taskId)
  const label = typeof result?.label === 'string' ? result.label.trim() : ''
  const keys = TASK_STOP_KEYS[kind]
  const key = running || result?.status === 'stopping' ? keys.stopping : keys.stopped
  const message = typeof result?.lastAgentMessage === 'string' && result.lastAgentMessage.trim()
    ? result.lastAgentMessage
    : null
  return {
    label: label ? translate(locale, key, { label }) : translate(locale, `${key}Generic`),
    message
  }
}

export function formatTaskStopRunningLabel(taskId: string | null, locale: AppLocale): string {
  return translate(locale, `${TASK_STOP_KEYS[kindOfTaskId(taskId ?? '')].stopping}Generic`)
}

function isTaskKind(value: unknown): value is TaskKind {
  return value === 'shell' || value === 'agent' || value === 'workflow'
}

function kindOfTaskId(taskId: string): TaskKind {
  if (taskId.startsWith('term_')) return 'shell'
  if (taskId.startsWith('run_')) return 'workflow'
  return 'agent'
}
