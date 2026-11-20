import { CircleAlert, Server } from 'lucide-react'
import { useEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import type { DevServerScope } from '../../../../shared/devServers'
import { runDevServer } from '../../../utils/devServers'
import { Button } from '../../ui/Button'
import { CopyButton } from '../../ui/CopyButton'
import { Spinner } from '../../ui/Spinner'
import type { BoundServerState } from './useBrowserDevServer'
import './browser-dev-servers.css'

const LOG_POLL_MS = 1_000

export function BrowserServerState({ state, scope, configurationName, logsOpen, onShowLogs }: {
  state: BoundServerState
  scope: DevServerScope | null
  configurationName: string | null
  logsOpen: boolean
  onShowLogs: () => void
}): JSX.Element | null {
  const t = useT()
  const [restarting, setRestarting] = useState(false)
  if (state.kind === 'none') return null
  const restartName = 'server' in state ? state.server.name : configurationName
  const restartScope = scope && 'server' in state ? { ...scope, folder: state.server.folder } : scope
  const restart = restartName && restartScope
    ? () => {
      setRestarting(true)
      void runDevServer(restartScope, restartName, t).finally(() => setRestarting(false))
    }
    : null
  const restartButton = restart ? (
    <Button variant="secondary" size="sm" disabled={restarting} onClick={restart}>{t('devServers.restart')}</Button>
  ) : null

  let icon = <Server size={32} strokeWidth={1.25} aria-hidden />
  let title = ''
  let body: string | null = null
  let actions = restartButton
  switch (state.kind) {
    case 'starting':
      icon = <Spinner size={24} />
      title = t('devServers.state.startingTitle', { name: state.server.name })
      actions = null
      break
    case 'stopped':
      title = t('devServers.state.stopped')
      break
    case 'failed':
      icon = <CircleAlert size={32} strokeWidth={1.25} aria-hidden />
      title = t('devServers.state.failed')
      body = state.lastLine || null
      actions = (
        <>
          {restartButton}
          {logsOpen ? null : <Button variant="ghost" size="sm" onClick={onShowLogs}>{t('devServers.showLogs')}</Button>}
        </>
      )
      break
    case 'portHeld':
      title = t('devServers.state.portHeld', { port: state.port })
      actions = null
      break
    case 'notListening':
      title = t('devServers.state.notListening', { port: state.port })
      break
  }
  return (
    <div className="dc-browser-server-state" role="status">
      <span className="dc-browser-server-state__icon">{icon}</span>
      <span className="dc-browser-server-state__title">{title}</span>
      {body ? <code className="dc-browser-server-state__body">{body}</code> : null}
      {actions ? <span className="dc-browser-server-state__actions">{actions}</span> : null}
    </div>
  )
}

export function BrowserServerLogs({ serverId }: { serverId: string }): JSX.Element {
  const t = useT()
  const [lines, setLines] = useState<string[]>([])
  const bodyRef = useRef<HTMLPreElement>(null)
  useEffect(() => {
    let active = true
    const read = (): void => {
      window.api.devServers.logs(serverId).then((next) => { if (active) setLines(next) }, () => undefined)
    }
    read()
    const timer = window.setInterval(read, LOG_POLL_MS)
    return () => {
      active = false
      window.clearInterval(timer)
    }
  }, [serverId])
  useEffect(() => {
    const body = bodyRef.current
    if (body) body.scrollTop = body.scrollHeight
  }, [lines])
  return (
    <section className="dc-browser-server-logs" aria-label={t('devServers.logs')}>
      <header className="dc-browser-server-logs__header">
        <span>{t('devServers.logs')}</span>
        <CopyButton getText={() => lines.join('\n')} label={t('devServers.copyLogs')} copiedLabel={t('toast.copied')} size={24} iconSize={14} />
      </header>
      <pre ref={bodyRef} className="dc-browser-server-logs__body">{lines.join('\n') || t('devServers.noOutput')}</pre>
    </section>
  )
}
