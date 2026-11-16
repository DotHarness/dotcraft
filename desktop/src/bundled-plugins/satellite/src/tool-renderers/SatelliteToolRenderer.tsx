import { useEffect, useMemo, useState } from 'react'
import { SatelliteDish } from 'lucide-react'
import type { DesktopPluginToolRendererProps } from '@dotcraft/plugin'
import { RunningShimmer, ToolDisclosure } from '@dotcraft/plugin'
import { useLocale } from '../i18n'
import { useRemoteToolHost } from '../runtime'
import {
  formatRemoteToolHostLabel,
  getRemoteFileTransferDisplay,
  parseRemoteToolHostCatalog,
  readRemoteToolHostOperation,
  type RemoteToolHostNames,
  type ToolItem
} from './display'
import { RemoteFileTransferResult } from './RemoteFileTransferResult'
import { RemoteToolHostListResult } from './RemoteToolHostListResult'
import styles from './SatelliteToolRenderer.module.css'

export function SatelliteToolRenderer({ presentation }: DesktopPluginToolRendererProps): JSX.Element {
  const locale = useLocale()
  const routing = useRemoteToolHost()
  const [expanded, setExpanded] = useState(false)
  const routeHostId = presentation.threadId ? routing.routes[presentation.threadId]?.hostId : undefined
  const names = useMemo<RemoteToolHostNames>(() => ({
    host: (hostId) => {
      const id = hostId ?? routeHostId
      return id ? routing.hosts.find((host) => host.hostId === id)?.displayName ?? id : undefined
    },
    workspace: (workspaceId) => routing.hosts
      .flatMap((host) => host.workspaces)
      .find((workspace) => workspace.workspaceId === workspaceId)?.displayName
  }), [routing.hosts, routeHostId])
  const item: ToolItem = {
    presentation: { options: presentation.options },
    arguments: presentation.arguments,
    result: presentation.result,
    errorCode: presentation.errorCode,
    errorMessage: presentation.errorMessage,
    transferProgress: presentation.progress
  }
  const phase = presentation.status === 'running'
    ? 'running'
    : presentation.success === false ? 'failed' : 'completed'
  const operation = readRemoteToolHostOperation(item)
  const transfer = operation === 'transfer' ? getRemoteFileTransferDisplay(item, phase, locale, names) : null
  const catalog = operation === 'list' ? parseRemoteToolHostCatalog(presentation.result) : null
  const label = transfer?.label ?? formatRemoteToolHostLabel(item, phase, locale, names) ?? presentation.toolName
  const canExpand = (operation === 'transfer' && transfer != null)
    || (operation === 'list' && phase !== 'running' && Boolean(presentation.result?.trim()))

  useEffect(() => {
    if (!canExpand) setExpanded(false)
  }, [canExpand])

  return (
    <ToolDisclosure
      expanded={canExpand && expanded}
      onToggle={() => { if (canExpand) setExpanded((value) => !value) }}
      expandable={canExpand}
      callId={presentation.toolCallId}
      icon={<SatelliteDish className={styles.icon} size={16} strokeWidth={1.6} aria-hidden />}
      title={<RunningShimmer active={phase === 'running'} className={styles.label}>{label}</RunningShimmer>}
    >
      <div className={styles.result}>
        {transfer
          ? <RemoteFileTransferResult display={transfer} locale={locale} />
          : catalog
            ? <RemoteToolHostListResult catalog={catalog} locale={locale} />
            : <pre>{presentation.result}</pre>}
      </div>
    </ToolDisclosure>
  )
}
