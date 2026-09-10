import { Monitor } from 'lucide-react'
import type { JSX } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type {
  RemoteToolHostCatalog,
  RemoteToolHostEntry,
  RemoteToolHostWorkspaceEntry
} from '../../utils/remoteToolHostDisplay'
import styles from './RemoteToolHostListResult.module.css'

interface RemoteToolHostListResultProps {
  catalog: RemoteToolHostCatalog
  locale: AppLocale
}

/**
 * The catalog behind a RemoteToolHost.List row, in the Run on chip's vocabulary
 * so a machine reads the same wherever the user meets it.
 */
export function RemoteToolHostListResult({
  catalog,
  locale
}: RemoteToolHostListResultProps): JSX.Element {
  if (catalog.hosts.length === 0) {
    return (
      <p className={styles.empty} data-testid="remote-tool-host-empty">
        {translate(locale, 'toolCall.remoteToolHost.list.empty')}
      </p>
    )
  }

  return (
    <ul className={styles.list} data-testid="remote-tool-host-list">
      {catalog.hosts.map((host) => (
        <li key={host.hostId} className={styles.host}>
          <span className={styles.hostName}>
            <Monitor size={13} strokeWidth={1.9} aria-hidden className={styles.hostGlyph} />
            <span className={styles.name}>{host.displayName}</span>
            {!host.online && (
              <span className={styles.note}>· {translate(locale, 'composer.runOn.offline')}</span>
            )}
          </span>
          <ul className={styles.workspaces}>
            {host.workspaces.map((workspace) => {
              const current = isCurrent(catalog, host, workspace)
              const note = workspaceNote(host, workspace, current, locale)
              return (
                <li
                  key={workspace.workspaceId}
                  className={styles.workspace}
                  data-current={current ? 'true' : undefined}
                  data-testid={`remote-tool-host-workspace-${host.hostId}:${workspace.workspaceId}`}
                >
                  <span className={styles.name}>{workspace.displayName}</span>
                  {note && <span className={styles.note}>· {note}</span>}
                </li>
              )
            })}
          </ul>
        </li>
      ))}
    </ul>
  )
}

function isCurrent(
  catalog: RemoteToolHostCatalog,
  host: RemoteToolHostEntry,
  workspace: RemoteToolHostWorkspaceEntry
): boolean {
  return catalog.connectedRoute?.hostId === host.hostId
    && catalog.connectedRoute.workspaceId === workspace.workspaceId
}

/** The host line already carries "Offline", so a folder there stays unannotated. */
function workspaceNote(
  host: RemoteToolHostEntry,
  workspace: RemoteToolHostWorkspaceEntry,
  current: boolean,
  locale: AppLocale
): string | null {
  if (current) return translate(locale, 'toolCall.remoteToolHost.list.current')
  if (!host.online || workspace.available) return null
  return translate(locale, `composer.runOn.busy.${workspace.busyOwner === 'self' ? 'self' : 'other'}`)
}
