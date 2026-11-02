import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { parseToolResultObject } from './toolCallDisplay'
import { resolveCoreToolRenderPlan } from './toolRendererRegistry'

type TaskKind = 'shell' | 'agent' | 'workflow'

const TASK_STOP_KEYS: Record<TaskKind, { stopped: string; stopping: string }> = {
  shell: { stopped: 'toolCall.taskStop.stoppedTerminal', stopping: 'toolCall.taskStop.stoppingTerminal' },
  agent: { stopped: 'toolCall.taskStop.stoppedSubagent', stopping: 'toolCall.taskStop.stoppingSubagent' },
  workflow: { stopped: 'toolCall.taskStop.stoppedWorkflow', stopping: 'toolCall.taskStop.stoppingWorkflow' }
}

export interface TaskStopDisplay {
  kind: TaskKind
  taskId: string | null
  label: string | null
  stopping: boolean
}

export function getTaskStopDisplay(item: ConversationItem, running: boolean): TaskStopDisplay | null {
  const plan = resolveCoreToolRenderPlan(item)
  if (plan?.family !== 'task' || (!running && item.success === false)) return null
  const result = running ? undefined : parseToolResultObject(item.result)
  const taskId = getString(result?.taskId) ?? getString(item.arguments?.taskId)
  const kind = isTaskKind(plan.options.kind) ? plan.options.kind
    : isTaskKind(result?.kind) ? result.kind : kindOfTaskId(taskId ?? '')
  return {
    kind,
    taskId,
    label: getString(result?.label),
    stopping: running || result?.status === 'stopping'
  }
}

export function formatTaskStopLabel(display: TaskStopDisplay, locale: AppLocale): string {
  const keys = TASK_STOP_KEYS[display.kind]
  const key = display.stopping ? keys.stopping : keys.stopped
  return display.label ? translate(locale, key, { label: display.label }) : translate(locale, `${key}Generic`)
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

function getString(value: unknown): string | null {
  return typeof value === 'string' ? value.trim() || null : null
}
