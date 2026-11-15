import { create } from 'zustand'
import type { SummaryTerminal } from '../components/conversation/summary/terminalFeed'
import type { SummaryWorkflow } from '../components/conversation/summary/feed'

export interface SubAgentTasks {
  terminals: readonly SummaryTerminal[]
  workflows: readonly SummaryWorkflow[]
}

export const useSubAgentTaskActivity = create<{
  tasksByThread: ReadonlyMap<string, SubAgentTasks>
  reconcilingThreads: ReadonlySet<string>
}>(() => ({ tasksByThread: new Map(), reconcilingThreads: new Set() }))

export function publishThreadTasks(threadId: string, tasks: SubAgentTasks | null): void {
  useSubAgentTaskActivity.setState(state => {
    const active = tasks && (tasks.terminals.length > 0 || tasks.workflows.length > 0)
    if (!active && !state.tasksByThread.has(threadId)) return state
    const tasksByThread = new Map(state.tasksByThread)
    if (active) tasksByThread.set(threadId, tasks)
    else tasksByThread.delete(threadId)
    return { tasksByThread }
  })
}

export function reconcileThreadActivity(threadId: string, pending: boolean): void {
  useSubAgentTaskActivity.setState(state => {
    if (state.reconcilingThreads.has(threadId) === pending) return state
    const reconcilingThreads = new Set(state.reconcilingThreads)
    if (pending) reconcilingThreads.add(threadId)
    else reconcilingThreads.delete(threadId)
    return { reconcilingThreads }
  })
}
