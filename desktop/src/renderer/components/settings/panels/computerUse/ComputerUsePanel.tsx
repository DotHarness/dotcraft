import { useEffect, useState, type JSX } from 'react'

import { useT } from '../../../../contexts/LocaleContext'
import { useConnectionStore } from '../../../../stores/connectionStore'
import { operationFailureMessage, usePluginStore, type PluginEntry } from '../../../../stores/pluginStore'
import { useSkillsStore } from '../../../../stores/skillsStore'
import { addToast } from '../../../../stores/toastStore'
import { useUIStore } from '../../../../stores/uiStore'
import { PluginIcon, pluginSubtitle, pluginTitle } from '../../../plugins/PluginCatalogItem'
import { PluginInstallDialog } from '../../../plugins/PluginInstallDialog'
import { showPluginInstalledToast } from '../../../plugins/pluginToasts'
import { StatusIndicator } from '../../../ui/StatusIndicator'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsPanelShell } from '../../SettingsPanelShell'
import { AlwaysAllowedAppsGroup } from './AlwaysAllowedAppsGroup'
import { ChromeDetailPage } from './ChromeDetailPage'
import { chromeSetupSummary, chromeStatusTone } from './chromeSetup'
import { ComputerUseControlRow } from './ComputerUseControlRow'
import { useChromeSetup } from './useChromeSetup'
import styles from './ComputerUsePanel.module.css'

const COMPUTER_PLUGIN_ID = 'computer'
const CHROME_PLUGIN_ID = 'chrome'

interface ComputerUsePanelProps {
  chromeDetailRequested?: boolean
  onChromeDetailRequestHandled?: () => void
}

export function ComputerUsePanel({
  chromeDetailRequested = false,
  onChromeDetailRequestHandled
}: ComputerUsePanelProps): JSX.Element {
  const t = useT()
  const isWindows = window.api.platform === 'win32'
  const pluginManagementEnabled = useConnectionStore((s) => s.capabilities?.pluginManagement === true)
  const plugins = usePluginStore((s) => s.plugins)
  const fetchPlugins = usePluginStore((s) => s.fetchPlugins)
  const installPlugin = usePluginStore((s) => s.installPlugin)
  const togglePluginEnabled = usePluginStore((s) => s.togglePluginEnabled)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const chromeSetup = useChromeSetup()
  const reloadChromeSetup = chromeSetup.reload
  const [chromeDetailOpen, setChromeDetailOpen] = useState(chromeDetailRequested)
  const [chromeInstallOpen, setChromeInstallOpen] = useState(false)
  const [chromeInstalling, setChromeInstalling] = useState(false)
  const [busyPluginId, setBusyPluginId] = useState<string | null>(null)

  const computerPlugin = isWindows ? plugins.find((plugin) => plugin.id === COMPUTER_PLUGIN_ID) ?? null : null
  const chromePlugin = plugins.find((plugin) => plugin.id === CHROME_PLUGIN_ID) ?? null
  const chromeInstalled = chromePlugin?.installed === true

  useEffect(() => {
    if (pluginManagementEnabled) void fetchPlugins()
  }, [fetchPlugins, pluginManagementEnabled])

  useEffect(() => {
    if (!chromeDetailRequested) return
    setChromeDetailOpen(true)
    onChromeDetailRequestHandled?.()
  }, [chromeDetailRequested, onChromeDetailRequestHandled])

  useEffect(() => {
    if (chromeInstalled) void reloadChromeSetup()
  }, [chromeDetailOpen, chromeInstalled, reloadChromeSetup])

  async function turnPluginOn(plugin: PluginEntry): Promise<boolean> {
    if (!plugin.installed) {
      const result = await installPlugin(plugin.id)
      if (result.outcome === 'notApplied') {
        addToast(operationFailureMessage(result) ?? t('plugins.installFailed'), 'error')
        return false
      }
      await fetchPlugins()
      showPluginInstalledToast(plugin, { message: t('plugins.installSuccess', { name: pluginTitle(plugin) }) })
    }
    const current = usePluginStore.getState().plugins.find((entry) => entry.id === plugin.id) ?? plugin
    if (!current.installed) {
      addToast(t('plugins.installFailed'), 'error')
      return false
    }
    if (!current.enabled) {
      const result = await togglePluginEnabled(plugin.id, true)
      if (result.outcome === 'notApplied') {
        addToast(operationFailureMessage(result) ?? t('plugins.updateFailed'), 'error')
        return false
      }
    }
    await fetchSkills()
    return true
  }

  function openPlugin(plugin: PluginEntry): void {
    void usePluginStore.getState().selectPlugin(plugin.id)
    const ui = useUIStore.getState()
    ui.setPluginCatalogSurface('plugins')
    ui.setActiveMainView('skills')
  }

  async function handleTogglePlugin(plugin: PluginEntry, enabled: boolean): Promise<void> {
    if (busyPluginId) return
    setBusyPluginId(plugin.id)
    try {
      if (enabled) {
        await turnPluginOn(plugin)
        return
      }
      const result = await togglePluginEnabled(plugin.id, false)
      if (result.outcome === 'notApplied') {
        addToast(operationFailureMessage(result) ?? t('plugins.updateFailed'), 'error')
        return
      }
      await fetchSkills()
    } catch {
      addToast(t(plugin.installed ? 'plugins.updateFailed' : 'plugins.installFailed'), 'error')
    } finally {
      setBusyPluginId(null)
    }
  }

  async function handleInstallChromePlugin(): Promise<void> {
    if (!chromePlugin) return
    setChromeInstalling(true)
    try {
      if (!await turnPluginOn(chromePlugin)) return
      setChromeInstallOpen(false)
      setChromeDetailOpen(true)
    } catch {
      addToast(t('plugins.installFailed'), 'error')
    } finally {
      setChromeInstalling(false)
    }
  }

  function handleToggleChrome(enabled: boolean): void {
    if (!chromePlugin) return
    if (enabled && !chromePlugin.installed) {
      setChromeInstallOpen(true)
      return
    }
    void handleTogglePlugin(chromePlugin, enabled)
  }

  function renderChromeDetail(): JSX.Element | string {
    if (!chromePlugin?.installed) return chromePlugin ? pluginSubtitle(chromePlugin) : ''
    const checking = chromeSetup.loading && chromeSetup.status == null
    const summary = chromeSetupSummary(chromeSetup.status, t)
    return (
      <span className={styles.status}>
        <StatusIndicator tone={checking ? 'pending' : chromeStatusTone(summary.tone)} />
        {checking ? t('settings.loading') : summary.label}
      </span>
    )
  }

  if (chromeDetailOpen) {
    return <ChromeDetailPage plugin={chromePlugin} setup={chromeSetup} onBack={() => setChromeDetailOpen(false)} />
  }

  return (
    <SettingsPanelShell title={t('settings.chrome.pageTitle')} description={t('settings.chrome.pageDescription')}>
      <SettingsGroup title={t('settings.chrome.control')}>
        {!pluginManagementEnabled ? (
          <SettingsRow>
            <div className={styles.empty}>{t('plugins.unavailable')}</div>
          </SettingsRow>
        ) : !computerPlugin && !chromePlugin ? (
          <SettingsRow>
            <div className={styles.empty}>{t('plugins.loading')}</div>
          </SettingsRow>
        ) : (
          <>
            {computerPlugin && (
              <ComputerUseControlRow
                icon={<PluginIcon plugin={computerPlugin} role="list" size={38} />}
                name={t('settings.computerUse.anyApp.title')}
                detail={t('settings.computerUse.anyApp.description')}
                onOpen={() => openPlugin(computerPlugin)}
                checked={computerPlugin.installed && computerPlugin.enabled}
                busy={busyPluginId === computerPlugin.id}
                toggleLabel={t('settings.computerUse.anyApp.toggleAria')}
                onToggle={(next) => void handleTogglePlugin(computerPlugin, next)}
              />
            )}
            {chromePlugin && (
              <ComputerUseControlRow
                icon={<PluginIcon plugin={chromePlugin} role="list" size={38} />}
                name={pluginTitle(chromePlugin)}
                detail={renderChromeDetail()}
                manage={chromePlugin.installed
                  ? { label: t('settings.chrome.manage'), onClick: () => setChromeDetailOpen(true) }
                  : undefined}
                onOpen={chromePlugin.installed ? undefined : () => openPlugin(chromePlugin)}
                checked={chromePlugin.installed && chromePlugin.enabled}
                busy={busyPluginId === chromePlugin.id || chromeInstalling}
                toggleLabel={t('settings.chrome.toggleAria')}
                onToggle={handleToggleChrome}
              />
            )}
          </>
        )}
      </SettingsGroup>

      {isWindows && <AlwaysAllowedAppsGroup />}

      {chromePlugin && chromeInstallOpen && (
        <PluginInstallDialog
          plugin={chromePlugin}
          installing={chromeInstalling}
          onClose={() => setChromeInstallOpen(false)}
          onInstall={() => void handleInstallChromePlugin()}
        />
      )}
    </SettingsPanelShell>
  )
}
