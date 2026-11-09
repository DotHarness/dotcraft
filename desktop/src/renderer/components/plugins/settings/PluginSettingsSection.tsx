import { useId, type JSX } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import {
  resolveDesktopPluginLabel,
  useDesktopPluginRegistry,
  type ActiveDesktopPluginSettingsPage
} from '../../../plugins/desktopPluginRegistry'
import type { PluginEntry } from '../../../stores/pluginStore'
import { useUIStore } from '../../../stores/uiStore'
import { SettingsGroup, SettingsRow } from '../../settings/SettingsGroup'
import { SETTINGS_SURFACE_CLASS } from '../../settings/settingsTypography'
import { Button } from '../../ui/Button'
import { McpServerSettings } from './McpServerSettings'
import { PluginConfigSettings } from './PluginConfigSettings'
import { usePluginConfig } from './usePluginConfig'
import { usePluginSettingsServers } from './useMcpServerSettings'
import styles from './PluginSettings.module.css'

export function PluginSettingsSection({ plugin }: { plugin: PluginEntry }): JSX.Element | null {
  const t = useT()
  const headingId = useId()
  const active = plugin.installed && plugin.enabled
  const config = usePluginConfig(plugin.id, active)
  const servers = usePluginSettingsServers(plugin.id, active)
  const settingsPage = useDesktopPluginRegistry((state) =>
    state.settingsPages.find((page) => page.pluginId === plugin.id))
  if (!config.declared && servers.length === 0) return null
  return (
    <section
      className={`${styles.section} ${SETTINGS_SURFACE_CLASS}`}
      aria-labelledby={headingId}
      data-plugin-settings
    >
      <h2 id={headingId} className={styles.heading}>{t('plugins.detail.settings')}</h2>
      {config.declared && (settingsPage
        ? <SettingsPageLink page={settingsPage} />
        : <PluginConfigSettings state={config} />)}
      {servers.map((server) => (
        <McpServerSettings
          key={server.status.name}
          server={server}
          serverName={servers.length > 1 || config.declared ? server.status.declaredName || server.status.name : undefined}
        />
      ))}
    </section>
  )
}

function SettingsPageLink({ page }: { page: ActiveDesktopPluginSettingsPage }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const open = (): void => {
    const ui = useUIStore.getState()
    ui.setActiveSettingsTab(page.settingsKey)
    ui.setActiveMainView('settings')
  }
  return (
    <SettingsGroup>
      <SettingsRow
        label={resolveDesktopPluginLabel(page.label, locale)}
        description={t('plugins.settings.pageDescription')}
        control={<Button onClick={open}>{t('plugins.settings.openPage')}</Button>}
      />
    </SettingsGroup>
  )
}
