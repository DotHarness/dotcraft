import { CircleAlert, FileCog, Globe, Play, ScrollText, Server, Square, SquareStop } from 'lucide-react'
import { useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import {
  devServerFolderKey,
  isActiveDevServer,
  type DevServerList,
  type DevServerScope,
  type DevServerSnapshot
} from '../../../../shared/devServers'
import { openDevServerTab, runDevServer, stopDevServer } from '../../../utils/devServers'
import { openWorkspaceFileViewer } from '../../../utils/conversationDeepLink'
import { ContextMenu, type ContextMenuEntry } from '../../ui/ContextMenu'
import { IconButton } from '../../ui/IconButton'
import { Spinner } from '../../ui/Spinner'

const MENU_GLYPH = { size: 14, strokeWidth: 1.75 } as const

export function BrowserServersMenu({ scope, list, boundServer, logsOpen, onToggleLogs }: {
  scope: DevServerScope
  list: DevServerList | null
  boundServer: DevServerSnapshot | null
  logsOpen: boolean
  onToggleLogs: () => void
}): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState<{ x: number; y: number; keyboard: boolean } | null>(null)
  const trigger = useRef<HTMLButtonElement | null>(null)
  const active = (list?.servers ?? []).filter(isActiveDevServer)
  const platform = window.api.platform
  const folderKey = list ? devServerFolderKey(list.folder, platform) : null
  const activeByName = new Map(active
    .filter((server) => devServerFolderKey(server.folder, platform) === folderKey)
    .map((server) => [server.name.toLowerCase(), server]))
  const launch = list?.launch
  const configurations = launch?.status === 'ok' ? launch.configurations : []
  const shown = new Set<DevServerSnapshot>()

  const serverRow = (server: DevServerSnapshot): ContextMenuEntry => ({
    label: server.name,
    icon: server.status === 'starting' ? <Spinner size={14} /> : <Server {...MENU_GLYPH} />,
    shortcut: server.status === 'starting' ? t('devServers.starting') : `:${server.port}`,
    title: t('devServers.menu.openInBrowser'),
    onClick: () => openDevServerTab(scope.threadId, server.url, server.name)
  })

  const items: ContextMenuEntry[] = [{ type: 'label', label: t('devServers.servers') }]
  if (!launch || launch.status === 'missing') {
    items.push({ label: t('devServers.noConfiguration'), disabled: true, onClick: () => undefined })
  } else if (launch.status === 'invalid') {
    items.push({
      label: t('devServers.launchFileNeedsAttention'),
      icon: <CircleAlert {...MENU_GLYPH} />,
      title: launch.reason,
      onClick: () => void openWorkspaceFileViewer({ threadId: scope.threadId, workspacePath: scope.folder, absolutePath: launch.path, forceNew: false })
    })
  }
  for (const configuration of configurations) {
    const server = activeByName.get(configuration.name.toLowerCase())
    if (server) {
      shown.add(server)
      items.push(serverRow(server))
    } else if (configuration.url) {
      const url = configuration.url
      items.push({
        label: configuration.name,
        icon: <Globe {...MENU_GLYPH} />,
        shortcut: configuration.port === undefined ? undefined : `:${configuration.port}`,
        title: t('devServers.menu.openInBrowser'),
        onClick: () => openDevServerTab(scope.threadId, url, configuration.name)
      })
    } else {
      items.push({
        label: configuration.name,
        icon: <Play {...MENU_GLYPH} />,
        shortcut: configuration.port === undefined ? undefined : `:${configuration.port}`,
        title: t('devServers.menu.run', { name: configuration.name }),
        onClick: () => void runDevServer(scope, configuration.name, t)
      })
    }
  }
  for (const server of active) {
    if (!shown.has(server)) items.push(serverRow(server))
  }

  const tools: ContextMenuEntry[] = []
  if (boundServer && isActiveDevServer(boundServer)) {
    const bound = boundServer
    tools.push({
      label: t('devServers.menu.stop', { name: bound.name }),
      icon: <Square {...MENU_GLYPH} />,
      onClick: () => void stopDevServer(bound.serverId, t)
    })
  }
  if (boundServer) {
    tools.push({
      label: logsOpen ? t('devServers.hideLogs') : t('devServers.showLogs'),
      icon: <ScrollText {...MENU_GLYPH} />,
      onClick: onToggleLogs
    })
  }
  if (active.length > 0) {
    tools.push({
      label: t('devServers.stopAll'),
      icon: <SquareStop {...MENU_GLYPH} />,
      onClick: () => void window.api.devServers.stopScope(scope).catch(() => undefined)
    })
  }
  if (launch && launch.status !== 'missing') {
    const path = launch.path
    tools.push({
      label: t('devServers.editConfiguration'),
      icon: <FileCog {...MENU_GLYPH} />,
      onClick: () => void openWorkspaceFileViewer({ threadId: scope.threadId, workspacePath: scope.folder, absolutePath: path, forceNew: false })
    })
  }
  if (tools.length > 0) items.push({ type: 'separator' }, ...tools)

  const close = (): void => {
    if (open?.keyboard) trigger.current?.focus()
    setOpen(null)
  }

  return (
    <>
      <IconButton
        ref={trigger}
        icon={<Server size={16} strokeWidth={1.75} aria-hidden />}
        label={t('devServers.servers')}
        tooltipLabel={t('devServers.servers')}
        tooltipPlacement="bottom"
        aria-haspopup="menu"
        aria-expanded={open !== null}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect()
          setOpen({ x: rect.left, y: rect.bottom + 4, keyboard: event.detail === 0 })
        }}
      />
      {open ? <ContextMenu items={items} position={{ x: open.x, y: open.y }} onClose={close} autoFocus={open.keyboard} /> : null}
    </>
  )
}
