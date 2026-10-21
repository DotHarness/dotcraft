import { IconButton, Spinner, type DesktopPluginHost } from '@dotcraft/plugin'
import { Square, SquareTerminal, Workflow } from 'lucide-react'
import { useState, type JSX } from 'react'
import type { SummaryTerminal, SummaryWorkflow } from './feed'
import type { SummaryStrings } from './i18n'
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

export function BackgroundTasksSection({ host, threadId, terminals, workflows, workflowCalls, strings }: {
  host: DesktopPluginHost
  threadId: string
  terminals: readonly SummaryTerminal[]
  workflows: readonly SummaryWorkflow[]
  workflowCalls: ReadonlyMap<string, string>
  strings: SummaryStrings
}): JSX.Element | null {
  const [stopping, setStopping] = useState<string | null>(null)
  if (terminals.length === 0 && workflows.length === 0) return null

  const settle = (id: string, work: Promise<unknown>, failure: string): void => {
    setStopping(id)
    work.then(
      () => setStopping(null),
      () => {
        setStopping(null)
        host.ui.showToast({ message: failure, tone: 'error' })
      }
    )
  }
  const stopTerminals = (sessionId: string): void => settle(
    sessionId,
    Promise.all(terminals.map((terminal) => host.appServer.request('terminal/stop', { sessionId: terminal.sessionId }))),
    strings.stopBackgroundTerminalsFailed
  )
  const stopWorkflow = (runId: string): void => settle(
    runId,
    host.appServer.request('workflow/run/stop', { threadId, runId }),
    strings.stopWorkflowFailed
  )
  const reveal = (callId: string | null | undefined): (() => void) | undefined =>
    callId ? () => host.navigation.revealToolCall(threadId, callId) : undefined

  return (
    <Section title={strings.backgroundTasks}>
      {workflows.map((workflow) => (
        <Row
          key={workflow.runId}
          leading={<Workflow {...GLYPH} />}
          label={workflow.name}
          inline={workflow.paused ? strings.paused : undefined}
          title={workflow.name}
          onClick={reveal(workflowCalls.get(workflow.runId))}
          trailing={(
            <StopButton
              label={strings.stopWorkflow}
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
            label={firstLine || strings.backgroundTerminal}
            title={terminal.command || undefined}
            onClick={reveal(terminal.callId)}
            trailing={(
              <StopButton
                label={strings.stopBackgroundTerminals}
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
