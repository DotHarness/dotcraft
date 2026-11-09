import { useId, type JSX } from 'react'
import { RotateCcw } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useDesktopPluginRegistry } from '../../../plugins/desktopPluginRegistry'
import type { PluginEntry } from '../../../stores/pluginStore'
import { SETTINGS_SURFACE_CLASS } from '../../settings/settingsTypography'
import { IconButton } from '../../ui/IconButton'
import { McpServerSettings } from './McpServerSettings'
import { PluginConfigSettings } from './PluginConfigSettings'
import { hasPersonalOverride } from './pluginConfigModel'
import { usePluginConfig } from './usePluginConfig'
import { usePluginSettingsServers } from './useMcpServerSettings'
import styles from './PluginSettings.module.css'

export function PluginSettingsSection({ plugin }: { plugin: PluginEntry }): JSX.Element | null {
  const t = useT()
  const headingId = useId()
  const active = plugin.installed && plugin.enabled
  const config = usePluginConfig(plugin.id, active)
  const servers = usePluginSettingsServers(plugin.id, active)
  const ownsSettingsUi = useDesktopPluginRegistry((state) =>
    state.settingsPages.some((page) => page.pluginId === plugin.id)
    || state.mainViews.some((view) => view.pluginId === plugin.id))
  const showConfig = config.declared && !ownsSettingsUi
  if (!showConfig && servers.length === 0) return null
  const snapshot = config.snapshot
  const canReset = showConfig
    && snapshot != null
    && snapshot.writableScopes.includes('personal')
    && hasPersonalOverride(snapshot)
  return (
    <section
      className={`${styles.section} ${SETTINGS_SURFACE_CLASS}`}
      aria-labelledby={headingId}
      data-plugin-settings
    >
      <div className={styles.header}>
        <h2 id={headingId} className={styles.heading}>{t('plugins.detail.settings')}</h2>
        {canReset && (
          <IconButton
            className={styles.headerAction}
            icon={<RotateCcw size={14} />}
            label={t('plugins.settings.reset')}
            tooltipLabel={t('plugins.settings.reset')}
            disabled={config.failed || config.pendingChange != null}
            onClick={config.resetAll}
          />
        )}
      </div>
      {showConfig && <PluginConfigSettings state={config} />}
      {servers.map((server) => (
        <McpServerSettings
          key={server.status.name}
          server={server}
          serverName={servers.length > 1 || showConfig ? server.status.declaredName || server.status.name : undefined}
        />
      ))}
    </section>
  )
}
