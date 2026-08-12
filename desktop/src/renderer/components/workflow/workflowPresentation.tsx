import { useEffect, useState } from 'react'
import { CircleCheck, CircleDot, CircleX } from 'lucide-react'

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
  const value = input + output
  if (value >= 1000) return `${(value / 1000).toFixed(value >= 10000 ? 1 : 2).replace(/\.0$/, '')}k`
  return String(value)
}

export function formatWorkflowElapsed(start?: string, end?: string): string | null {
  if (!start) return null
  const elapsed = Math.max(0, new Date(end ?? Date.now()).getTime() - new Date(start).getTime())
  const seconds = Math.round(elapsed / 1000)
  if (seconds < 60) return `${seconds}s`
  return `${Math.floor(seconds / 60)}m ${seconds % 60}s`
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
