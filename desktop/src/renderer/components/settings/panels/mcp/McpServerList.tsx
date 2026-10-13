import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import type { McpServerConfigWire, McpServerStatusWire } from '../../../../stores/mcpStore'
import { Button } from '../../../ui/Button'
import { PillSwitch } from '../../../ui/PillSwitch'
import { StatusIndicator, type StatusIndicatorTone } from '../../../ui/StatusIndicator'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import {
  settingsHintStyle,
  settingsLabelStyle,
  settingsMetaTextStyle,
  settingsPlaceholderStyle
} from '../../settingsTypography'
import styles from './McpServerList.module.css'

type Translate = ReturnType<typeof useT>

export function isPluginManagedMcpServer(server: McpServerConfigWire, originsEnabled: boolean): boolean {
  return (originsEnabled && server.origin?.kind === 'plugin') || server.readOnly === true
}

interface McpServerListProps {
  servers: McpServerConfigWire[]
  statuses: Record<string, McpServerStatusWire>
  loading: boolean
  error: string | null
  originsEnabled: boolean
  togglingServerName: string | null
  authenticatingServerName: string | null
  onEdit: (server: McpServerConfigWire) => void
  onToggle: (server: McpServerConfigWire, enabled: boolean) => void
  onAuthenticate: (server: McpServerConfigWire) => void
  onViewPlugin: (server: McpServerConfigWire) => void
}

type RowHandlers = Pick<McpServerListProps, 'onEdit' | 'onToggle' | 'onAuthenticate' | 'onViewPlugin'>

export function McpServerList({
  servers,
  statuses,
  loading,
  error,
  originsEnabled,
  togglingServerName,
  authenticatingServerName,
  ...handlers
}: McpServerListProps): JSX.Element {
  const t = useT()
  const own = servers.filter((server) => !isPluginManagedMcpServer(server, originsEnabled))
  const fromPlugins = servers.filter((server) => isPluginManagedMcpServer(server, originsEnabled))
  const row = (server: McpServerConfigWire, pluginManaged: boolean) => (
    <McpServerRow
      key={server.name}
      server={server}
      status={statuses[server.name.trim().toLowerCase()]}
      pluginManaged={pluginManaged}
      toggling={togglingServerName === server.name}
      authenticating={authenticatingServerName === server.name}
      {...handlers}
    />
  )

  return (
    <>
      <SettingsGroup title={t('settings.group.servers')}>
        {loading ? (
          <SettingsRow>
            <div style={settingsPlaceholderStyle()}>{t('settings.mcp.loading')}</div>
          </SettingsRow>
        ) : error ? (
          <SettingsRow>
            <div style={{ ...settingsPlaceholderStyle(), color: 'var(--error)' }}>{error}</div>
          </SettingsRow>
        ) : own.length === 0 ? (
          <SettingsRow>
            <div style={settingsPlaceholderStyle()}>{t('settings.mcp.empty.title')}</div>
          </SettingsRow>
        ) : (
          own.map((server) => row(server, false))
        )}
      </SettingsGroup>
      {!loading && !error && fromPlugins.length > 0 && (
        <SettingsGroup title={t('settings.mcp.fromPlugins')}>
          {fromPlugins.map((server) => row(server, true))}
        </SettingsGroup>
      )}
    </>
  )
}

function McpServerRow({
  server,
  status,
  pluginManaged,
  toggling,
  authenticating,
  onEdit,
  onToggle,
  onAuthenticate,
  onViewPlugin
}: {
  server: McpServerConfigWire
  status: McpServerStatusWire | undefined
  pluginManaged: boolean
  toggling: boolean
  authenticating: boolean
} & RowHandlers): JSX.Element {
  const t = useT()
  const needsSignIn = status?.authStatus === 'notLoggedIn'
  const trouble = needsSignIn ? null : troubleState(t, status)
  const details = [
    pluginManaged ? server.origin?.pluginDisplayName || server.origin?.pluginId : null,
    server.transport === 'stdio' ? t('settings.mcp.transport.stdio') : t('settings.mcp.transport.http'),
    typeof status?.toolCount === 'number' ? t('settings.mcp.toolsCount', { count: status.toolCount }) : null
  ].filter(Boolean)

  const identity = (
    <span className={styles.text}>
      <strong className={styles.name} style={settingsLabelStyle()}>
        {server.name}
      </strong>
      {trouble ? (
        <span className={styles.trouble} style={settingsHintStyle()}>
          <span className={styles.indicator}>
            <StatusIndicator tone={trouble.tone} />
          </span>
          <span>
            {trouble.label}
            {trouble.reason && ` · ${trouble.reason}`}
          </span>
        </span>
      ) : (
        <span className={styles.details} style={settingsMetaTextStyle(true)}>
          {details.join(' · ')}
        </span>
      )}
    </span>
  )

  return (
    <SettingsRow>
      <div className={styles.row} data-busy={toggling || undefined}>
        {pluginManaged ? (
          identity
        ) : (
          <button
            type="button"
            className={styles.open}
            aria-label={`Edit MCP server ${server.name}`}
            onClick={() => onEdit(server)}
          >
            {identity}
          </button>
        )}
        <div className={styles.actions}>
          {needsSignIn && (
            <Button disabled={authenticating} onClick={() => onAuthenticate(server)}>
              {authenticating
                ? t('settings.mcp.authenticating')
                : status?.failureReason === 'reauthenticationRequired'
                  ? t('settings.mcp.reauthenticate')
                  : t('settings.mcp.authenticate')}
            </Button>
          )}
          {pluginManaged ? (
            server.origin?.pluginId && (
              <Button onClick={() => onViewPlugin(server)}>{t('settings.mcp.viewPlugin')}</Button>
            )
          ) : (
            <PillSwitch
              checked={server.enabled}
              disabled={toggling}
              onChange={(checked) => onToggle(server, checked)}
              aria-label={`Toggle MCP server ${server.name}`}
            />
          )}
        </div>
      </div>
    </SettingsRow>
  )
}

/** Healthy and quiet states stay unannounced; a sign-in requirement is carried by the Authenticate action. */
function troubleState(
  t: Translate,
  status: McpServerStatusWire | undefined
): { tone: StatusIndicatorTone; label: string; reason?: string | null } | null {
  switch (status?.startupState) {
    case 'starting':
      return { tone: 'pending', label: t('settings.mcp.status.connecting') }
    case 'error':
      return { tone: 'error', label: t('settings.mcp.status.error'), reason: status.lastError }
    default:
      return null
  }
}
