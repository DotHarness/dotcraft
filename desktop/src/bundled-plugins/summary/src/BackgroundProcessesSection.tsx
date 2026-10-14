import { IconButton, Spinner, type DesktopPluginHost } from '@dotcraft/plugin'
import { Square, SquareTerminal } from 'lucide-react'
import { useState, type JSX } from 'react'
import type { SummaryTerminal } from './feed'
import type { SummaryStrings } from './i18n'
import { GLYPH, Row, Section } from './SummaryRows'

const STOP_GLYPH = { size: 14, strokeWidth: 1.75 } as const

export function BackgroundProcessesSection({ host, threadId, terminals, strings }: {
  host: DesktopPluginHost
  threadId: string
  terminals: readonly SummaryTerminal[]
  strings: SummaryStrings
}): JSX.Element | null {
  const [stopping, setStopping] = useState<string | null>(null)
  if (terminals.length === 0) return null

  const stopAll = (sessionId: string): void => {
    setStopping(sessionId)
    host.appServer.request('terminal/clean', { threadId }).then(
      () => setStopping(null),
      () => {
        setStopping(null)
        host.ui.showToast({ message: strings.stopBackgroundTerminalsFailed, tone: 'error' })
      }
    )
  }

  return (
    <Section title={strings.backgroundProcesses}>
      {terminals.map((terminal) => {
        const firstLine = terminal.command.split(/\r?\n/, 1)[0]?.trim()
        const busy = stopping === terminal.sessionId
        return (
          <Row
            key={terminal.sessionId}
            leading={<SquareTerminal {...GLYPH} />}
            label={firstLine || strings.backgroundTerminal}
            title={terminal.command || undefined}
            trailing={(
              <IconButton
                size={24}
                icon={busy ? <Spinner size={14} /> : <Square {...STOP_GLYPH} />}
                label={strings.stopBackgroundTerminals}
                tooltipLabel={strings.stopBackgroundTerminals}
                disabled={stopping !== null}
                aria-busy={busy || undefined}
                onClick={() => stopAll(terminal.sessionId)}
              />
            )}
          />
        )
      })}
    </Section>
  )
}
