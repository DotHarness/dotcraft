import { useEffect, useRef, useState } from 'react'
import {
  devServerPortOfUrl,
  type DevServerList,
  type DevServerScope,
  type DevServerSnapshot
} from '../../../../shared/devServers'
import { useDevServers, useDevServerScope } from '../../../utils/devServers'

export type BoundServerState =
  | { readonly kind: 'none' }
  | { readonly kind: 'starting'; readonly server: DevServerSnapshot }
  | { readonly kind: 'stopped'; readonly server: DevServerSnapshot }
  | { readonly kind: 'failed'; readonly server: DevServerSnapshot; readonly lastLine: string }
  | { readonly kind: 'portHeld'; readonly server: DevServerSnapshot; readonly port: number }
  | { readonly kind: 'notListening'; readonly port: number }

export interface BrowserDevServer {
  readonly scope: DevServerScope | null
  readonly list: DevServerList | null
  readonly server: DevServerSnapshot | null
  readonly configurationName: string | null
  readonly state: BoundServerState
}

function boundServer(list: DevServerList | null, port: number | null): DevServerSnapshot | null {
  if (port === null || !list) return null
  const candidates = list.servers.filter((server) => server.port === port)
  return candidates.find((server) => server.status === 'running' || server.status === 'starting') ?? candidates.at(-1) ?? null
}

function configurationFor(list: DevServerList | null, port: number | null): string | null {
  if (port === null || list?.launch.status !== 'ok') return null
  return list.launch.configurations.find((configuration) => configuration.port === port && !configuration.url)?.name ?? null
}

function useLastLogLine(server: DevServerSnapshot | null): string {
  const [line, setLine] = useState('')
  const id = server?.status === 'error' ? server.serverId : null
  useEffect(() => {
    setLine('')
    if (!id) return
    let active = true
    window.api.devServers.logs(id).then((lines) => {
      if (!active) return
      const last = lines.filter((entry) => entry.trim() && !entry.startsWith('[DotCraft]')).at(-1)
      setLine(last?.trim() ?? '')
    }, () => undefined)
    return () => { active = false }
  }, [id])
  return line
}

function useNotListening(port: number | null, probe: boolean, revision: unknown): boolean {
  const [quiet, setQuiet] = useState(false)
  useEffect(() => {
    setQuiet(false)
    if (port === null || !probe) return
    let active = true
    window.api.devServers.isListening(port).then((listening) => { if (active) setQuiet(!listening) }, () => undefined)
    return () => { active = false }
  }, [port, probe, revision])
  return quiet
}

export function useBrowserDevServer(params: { threadId: string | null; tabId: string; currentUrl: string }): BrowserDevServer {
  const scope = useDevServerScope(params.threadId)
  const list = useDevServers(scope)
  const port = devServerPortOfUrl(params.currentUrl)
  const server = boundServer(list, port)
  const configurationName = server ? null : configurationFor(list, port)
  const lastLine = useLastLogLine(server)
  const notListening = useNotListening(port, configurationName !== null || server?.status === 'stopped', list)

  const previous = useRef<{ id: string; status: string } | null>(null)
  useEffect(() => {
    const before = previous.current
    previous.current = server ? { id: server.serverId, status: server.status } : null
    if (server?.status === 'running' && before?.id === server.serverId && before.status !== 'running') {
      void window.api.workspace.viewer.browser.reload({ tabId: params.tabId })
    }
  }, [server, params.tabId])

  let state: BoundServerState = { kind: 'none' }
  if (server?.status === 'starting') state = { kind: 'starting', server }
  else if (server?.status === 'stopped' && notListening) state = { kind: 'stopped', server }
  else if (server?.status === 'error' && server.failure?.kind === 'portInUse' && server.failure.holder === 'otherThread') {
    state = { kind: 'portHeld', server, port: server.port }
  } else if (server?.status === 'error') state = { kind: 'failed', server, lastLine }
  else if (configurationName !== null && notListening && port !== null) state = { kind: 'notListening', port }
  return { scope, list, server, configurationName, state }
}
