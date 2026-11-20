import { ChevronUp, Ellipsis, Server, Square } from 'lucide-react'
import { useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import type { DevServerSnapshot } from '../../../../shared/devServers'
import { IconButton } from '../../ui/IconButton'
import { Spinner } from '../../ui/Spinner'
import { openDevServerTab, stopDevServer, useDevServers, useDevServerScope } from '../../../utils/devServers'
import { GLYPH, Row, Section } from './SummaryRows'

const ROW_LIMIT = 4
const STOP_GLYPH = { size: 14, strokeWidth: 1.75 } as const

function addressOf(server: DevServerSnapshot): string {
  return `localhost:${server.port}`
}

function stateText(server: DevServerSnapshot, stopping: boolean, t: (key: string) => string): string {
  if (stopping) return t('devServers.stopping')
  if (server.status === 'running') return addressOf(server)
  if (server.status === 'error') return t('devServers.failedToStart')
  return t('devServers.starting')
}

export function ServersSection({ threadId }: { threadId: string }): JSX.Element | null {
  const t = useT()
  const scope = useDevServerScope(threadId)
  const list = useDevServers(scope)
  const [stopping, setStopping] = useState<ReadonlySet<string>>(new Set())
  const [expanded, setExpanded] = useState(false)
  const servers = (list?.servers ?? []).filter((server) => server.status !== 'stopped')
  if (servers.length === 0) return null

  const running = servers.filter((server) => server.status === 'running' || server.status === 'starting').length
  const shown = expanded ? servers : servers.slice(0, ROW_LIMIT)
  const stop = (server: DevServerSnapshot): void => {
    setStopping((current) => new Set(current).add(server.serverId))
    void stopDevServer(server.serverId, t).finally(() => {
      setStopping((current) => {
        const next = new Set(current)
        next.delete(server.serverId)
        return next
      })
    })
  }

  return (
    <Section
      title={t('devServers.servers')}
      actions={<span className="conversation-summary-section__summary">{t('devServers.running', { count: running })}</span>}
    >
      {shown.map((server) => {
        const isStopping = stopping.has(server.serverId)
        const label = server.name || addressOf(server)
        const state = stateText(server, isStopping, t)
        return (
          <Row
            key={server.serverId}
            leading={<Server {...GLYPH} />}
            label={label}
            title={server.configuredPort ? t('devServers.movedPort', { port: server.port, configured: server.configuredPort }) : label}
            meta={<span data-danger={server.status === 'error' || undefined}>{state === label ? null : state}</span>}
            onClick={() => openDevServerTab(threadId, server.url, label)}
            trailing={(
              <IconButton
                size={24}
                icon={isStopping ? <Spinner size={14} /> : <Square {...STOP_GLYPH} />}
                label={server.status === 'error' ? t('devServers.dismiss') : t('devServers.stopServer')}
                tooltipLabel={server.status === 'error' ? t('devServers.dismiss') : t('devServers.stopServer')}
                disabled={isStopping}
                aria-busy={isStopping || undefined}
                onClick={() => stop(server)}
              />
            )}
          />
        )
      })}
      {servers.length > ROW_LIMIT ? (
        <Row
          leading={expanded ? <ChevronUp {...GLYPH} /> : <Ellipsis {...GLYPH} />}
          label={expanded ? t('summary.showLess') : t('devServers.showAll', { count: servers.length })}
          ariaExpanded={expanded}
          muted
          onClick={() => setExpanded((value) => !value)}
        />
      ) : null}
    </Section>
  )
}
