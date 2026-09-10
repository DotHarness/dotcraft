import { SatelliteDish } from 'lucide-react'
import { useMemo, type ReactNode } from 'react'
import type { AppLocale } from '../../../shared/locales'
import { useThreadRouteStore } from '../../stores/threadRouteStore'
import type { RemoteToolHostInfo } from '@dotcraft/sdk/contracts'
import type { ConversationItem } from '../../types/conversation'
import {
  formatRemoteToolHostLabel,
  readRemoteToolHostOperation,
  type RemoteToolHostNames
} from '../../utils/remoteToolHostDisplay'
import styles from './RemoteToolHostRow.module.css'

interface RemoteToolHostRowInput {
  item: ConversationItem
  /** False for every other tool, which keeps those rows off this store. */
  enabled: boolean
  threadId: string
  locale: AppLocale
  running: boolean
  success: boolean
}

export interface RemoteToolHostRow {
  /** Only the catalog has anything behind the row; joining and leaving are the sentence. */
  expandable: boolean
  title: ReactNode
}

const NO_HOSTS: RemoteToolHostInfo[] = []

/**
 * The RemoteToolHost row's header. Returns null when the family does not apply or
 * the result did not carry the sentence, so the card keeps its generic label.
 */
export function useRemoteToolHostRow({
  item,
  enabled,
  threadId,
  locale,
  running,
  success
}: RemoteToolHostRowInput): RemoteToolHostRow | null {
  const hosts = useThreadRouteStore((state) => (enabled ? state.hosts : NO_HOSTS))
  const routeHostId = useThreadRouteStore((state) =>
    enabled ? state.routes[threadId]?.hostId : undefined
  )

  const names = useMemo<RemoteToolHostNames>(() => ({
    host: (hostId) => {
      const id = hostId ?? routeHostId
      return id ? hosts.find((host) => host.hostId === id)?.displayName ?? id : undefined
    },
    workspace: (workspaceId) => workspaceId
      ? hosts
        .flatMap((host) => host.workspaces)
        .find((workspace) => workspace.workspaceId === workspaceId)?.displayName
      : undefined
  }), [hosts, routeHostId])

  const label = enabled
    ? formatRemoteToolHostLabel(item, running ? 'running' : success ? 'completed' : 'failed', locale, names)
    : null
  if (label == null) return null

  return {
    expandable: readRemoteToolHostOperation(item) === 'list',
    title: (
      <span className={styles.title} data-testid="remote-tool-host-row-title">
        <SatelliteDish size={13} strokeWidth={1.8} aria-hidden className={styles.glyph} />
        <span className={running ? `${styles.label} tool-running-gradient-text` : styles.label}>
          {label}
        </span>
      </span>
    )
  }
}
