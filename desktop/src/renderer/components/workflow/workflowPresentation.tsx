import { useEffect, useState } from 'react'
import { CircleCheck, CircleDot, CircleX } from 'lucide-react'
import type { WorkflowPhaseView } from '@dotcraft/sdk/contracts'
import { formatCompactCount } from '../../utils/formatCompactCount'

export function WorkflowStatusGlyph({ status }: { status: string }): JSX.Element {
  if (status === 'completed' || status === 'succeeded') return <CircleCheck size={15} strokeWidth={1.8} aria-hidden />
  if (status === 'running' || status === 'pending') return <CircleDot size={15} strokeWidth={1.8} aria-hidden />
  return <CircleX size={15} strokeWidth={1.8} aria-hidden />
}

export function workflowTone(status: string): string {
  if (status === 'completed' || status === 'succeeded') return 'success'
  if (status === 'failed') return 'error'
  if (status === 'stopped' || status === 'cancelled') return 'muted'
  return 'running'
}

export function formatWorkflowTokens(input: number, output: number): string {
  return formatCompactCount(input + output)
}

export function formatWorkflowElapsed(start?: string, end?: string): string | null {
  if (!start) return null
  const elapsed = Math.max(0, new Date(end ?? Date.now()).getTime() - new Date(start).getTime())
  const seconds = Math.round(elapsed / 1000)
  if (seconds < 60) return `${seconds}s`
  return `${Math.floor(seconds / 60)}m ${seconds % 60}s`
}

export function formatWorkflowPhaseMetrics(phase: WorkflowPhaseView): string | null {
  if (phase.agents.length === 0) return null
  const inputTokens = phase.agents.reduce((total, agent) => total + agent.inputTokens, 0)
  const outputTokens = phase.agents.reduce((total, agent) => total + agent.outputTokens, 0)
  const toolCallCount = phase.agents.reduce((total, agent) => total + agent.toolCallCount, 0)
  const completed = phase.status === 'completed' || phase.status === 'succeeded'
  const completedAt = completed && phase.agents.every((agent) => agent.completedAt)
    ? new Date(Math.max(...phase.agents.map((agent) => new Date(agent.completedAt!).getTime()))).toISOString()
    : undefined
  const startedAt = completedAt
    ? new Date(Math.min(...phase.agents.map((agent) => new Date(agent.startedAt ?? agent.requestedAt).getTime()))).toISOString()
    : undefined
  const elapsed = formatWorkflowElapsed(startedAt, completedAt)
  return `${formatWorkflowTokens(inputTokens, outputTokens)} tok · ${toolCallCount} tools${elapsed ? ` · ${elapsed}` : ''}`
}

export function useWorkflowElapsed(start?: string, end?: string): string | null {
  const [, setTick] = useState(0)
  useEffect(() => {
    if (!start || end) return
    const intervalId = window.setInterval(() => setTick((value) => value + 1), 1_000)
    return () => window.clearInterval(intervalId)
  }, [end, start])
  return formatWorkflowElapsed(start, end)
}
