import { Square, SquareTerminal, Workflow } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { IconButton } from '../../ui/IconButton'
import { Spinner } from '../../ui/Spinner'
import { formatDurationShort } from '../../../utils/formatDurationShort'
import type { SummaryTerminal, SummaryWorkflow } from './feed'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'

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

export function BackgroundTasksSection({ services, threadId, terminals, workflows, workflowCalls }: {
  services: SummaryServices
  threadId: string
  terminals: readonly SummaryTerminal[]
  workflows: readonly SummaryWorkflow[]
  workflowCalls: ReadonlyMap<string, string>
}): JSX.Element | null {
  const t = useT()
  const [stopping, setStopping] = useState<string | null>(null)
  const now = useNow(terminals.length > 0 || workflows.length > 0)
  if (terminals.length === 0 && workflows.length === 0) return null

  const settle = (id: string, work: Promise<unknown>, failure: string): void => {
    setStopping(id)
    work.then(
      () => setStopping(null),
      () => {
        setStopping(null)
        services.ui.showError(failure)
      }
    )
  }
  const stopTerminals = (sessionId: string): void => settle(
    sessionId,
    Promise.all(terminals.map((terminal) => services.appServer.request('terminal/stop', { sessionId: terminal.sessionId }))),
    t('summary.stopBackgroundTerminalsFailed')
  )
  const stopWorkflow = (runId: string): void => settle(
    runId,
    services.appServer.request('workflow/run/stop', { threadId, runId }),
    t('summary.stopWorkflowFailed')
  )
  const reveal = (callId: string | null | undefined): (() => void) | undefined =>
    callId ? () => services.navigation.revealToolCall(threadId, callId) : undefined

  return (
    <Section title={t('summary.backgroundTasks')}>
      {workflows.map((workflow) => (
        <Row
          key={workflow.runId}
          leading={<Workflow {...GLYPH} />}
          label={workflow.name}
          meta={workflow.paused ? t('summary.paused') : elapsed(workflow.startedAt, now)}
          title={workflow.name}
          onClick={reveal(workflowCalls.get(workflow.runId))}
          trailing={(
            <StopButton
              label={t('summary.stopWorkflow')}
              busy={stopping === workflow.runId}
              disabled={stopping !== null}
              onClick={() => stopWorkflow(workflow.runId)}
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
            onClick={reveal(terminal.callId)}
            trailing={(
              <StopButton
                label={t('summary.stopBackgroundTerminals')}
                busy={stopping === terminal.sessionId}
                disabled={stopping !== null}
                onClick={() => stopTerminals(terminal.sessionId)}
              />
            )}
          />
        )
      })}
    </Section>
  )
}
