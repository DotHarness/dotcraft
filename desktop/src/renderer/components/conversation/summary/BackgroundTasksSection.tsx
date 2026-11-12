import { Square, SquareTerminal, Workflow } from 'lucide-react'
import { useEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { IconButton } from '../../ui/IconButton'
import { Spinner } from '../../ui/Spinner'
import { formatDurationShort } from '../../../utils/formatDurationShort'
import type { SummaryTerminal, SummaryWorkflow } from './feed'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'
import { stopSummaryTerminal, stopSummaryWorkflow } from './store'
import { useSubAgentTaskTree } from '../../../hooks/useSubAgentTaskTree'
import { stopSubAgentTerminal, stopSubAgentWorkflow } from '../../../stores/subAgentTaskActivity'
import { openSubAgent } from '../../../utils/subAgentNavigation'

const STOP_GLYPH = { size: 14, strokeWidth: 1.75 } as const

function StopButton({ label, busy, disabled, onClick }: {
  label: string
  busy: boolean
  disabled: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <IconButton
      size={24}
      icon={busy ? <Spinner size={14} /> : <Square {...STOP_GLYPH} />}
      label={label}
      tooltipLabel={label}
      disabled={disabled}
      aria-busy={busy || undefined}
      onClick={onClick}
    />
  )
}

function useNow(ticking: boolean): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!ticking) return
    setNow(Date.now())
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [ticking])
  return now
}

function elapsed(startedAt: string | null, now: number): string | undefined {
  const start = Date.parse(startedAt ?? '')
  return Number.isFinite(start) ? formatDurationShort(now - start) : undefined
}

export function BackgroundTasksSection({ services, threadId, terminals: ownTerminals, workflows: ownWorkflows, workflowCalls }: {
  services: SummaryServices
  threadId: string
  terminals: readonly SummaryTerminal[]
  workflows: readonly SummaryWorkflow[]
  workflowCalls: ReadonlyMap<string, string>
}): JSX.Element | null {
  const t = useT()
  const { owners } = useSubAgentTaskTree(threadId)
  const terminals = [
    ...ownTerminals.map(terminal => ({ ...terminal, ownerThreadId: threadId })),
    ...owners.flatMap(owner => owner.terminals.map(terminal => ({ ...terminal, ownerThreadId: owner.threadId })))
  ]
  const workflows = [
    ...ownWorkflows.map(workflow => ({ ...workflow, ownerThreadId: threadId })),
    ...owners.flatMap(owner => owner.workflows.map(workflow => ({ ...workflow, ownerThreadId: owner.threadId })))
  ]
  const [stopping, setStopping] = useState<string | null>(null)
  const operation = useRef<symbol | null>(null)
  const scope = services.appServer.connectionKey()
  useEffect(() => {
    operation.current = null
    setStopping(null)
    return () => { operation.current = null }
  }, [services, threadId, scope])
  const now = useNow(terminals.length > 0 || workflows.length > 0)
  if (terminals.length === 0 && workflows.length === 0) return null

  const settle = async (id: string, work: () => Promise<unknown>, failure: string): Promise<void> => {
    if (operation.current !== null) return
    const token = Symbol()
    operation.current = token
    const current = (): boolean => token === operation.current && scope === services.appServer.connectionKey()
    setStopping(id)
    try {
      await work()
    } catch {
      if (current()) services.ui.showError(failure)
    } finally {
      if (current()) {
        operation.current = null
        setStopping(null)
      }
    }
  }
  const stopTerminal = (ownerThreadId: string, sessionId: string): void => void settle(
    sessionId,
    () => ownerThreadId === threadId ? stopSummaryTerminal(services, threadId, sessionId) : stopSubAgentTerminal(ownerThreadId, sessionId),
    t('summary.stopBackgroundTerminalFailed')
  )
  const stopWorkflow = (ownerThreadId: string, runId: string): void => void settle(
    runId,
    () => ownerThreadId === threadId ? stopSummaryWorkflow(services, threadId, runId) : stopSubAgentWorkflow(ownerThreadId, runId),
    t('summary.stopWorkflowFailed')
  )
  const reveal = (ownerThreadId: string, callId: string | null | undefined): (() => void) | undefined =>
    ownerThreadId !== threadId ? () => openSubAgent(threadId, ownerThreadId)
      : callId ? () => services.navigation.revealToolCall(threadId, callId) : undefined

  return (
    <Section title={t('summary.backgroundTasks')}>
      {workflows.map((workflow) => (
        <Row
          key={workflow.runId}
          leading={<Workflow {...GLYPH} />}
          label={workflow.name}
          meta={workflow.paused ? t('summary.paused') : elapsed(workflow.startedAt, now)}
          title={workflow.name}
          onClick={reveal(workflow.ownerThreadId, workflowCalls.get(workflow.runId))}
          trailing={(
            <StopButton
              label={t('summary.stopWorkflow')}
              busy={stopping === workflow.runId}
              disabled={stopping !== null}
              onClick={() => stopWorkflow(workflow.ownerThreadId, workflow.runId)}
            />
          )}
        />
      ))}
      {terminals.map((terminal) => {
        const firstLine = terminal.command.split(/\r?\n/, 1)[0]?.trim()
        return (
          <Row
            key={terminal.sessionId}
            leading={<SquareTerminal {...GLYPH} />}
            label={firstLine || t('summary.backgroundTerminal')}
            title={terminal.command || undefined}
            meta={elapsed(terminal.startedAt, now)}
            onClick={reveal(terminal.ownerThreadId, terminal.callId)}
            trailing={(
              <StopButton
                label={t('summary.stopBackgroundTerminal')}
                busy={stopping === terminal.sessionId}
                disabled={stopping !== null}
                onClick={() => stopTerminal(terminal.ownerThreadId, terminal.sessionId)}
              />
            )}
          />
        )
      })}
    </Section>
  )
}
