import type { JSX } from 'react'

import { useT } from '../../../../contexts/LocaleContext'
import type { PluginEntry } from '../../../../stores/pluginStore'
import { PluginIcon, pluginSubtitle } from '../../../plugins/PluginCatalogItem'
import { ExtensionsIcon, OpenInBrowserIcon, RefreshIcon, WrenchIcon } from '../../../ui/AppIcons'
import { IconButton } from '../../../ui/IconButton'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { SettingsGroup } from '../../SettingsGroup'
import { SettingsPanelShell } from '../../SettingsPanelShell'
import {
  CHROME_EXTENSIONS_URL,
  chromeNativeHostActionLabel,
  chromeSetupSummary,
  chromeStatusTone,
  setupResultOk
} from './chromeSetup'
import type { ChromeSetup } from './useChromeSetup'
import styles from './ComputerUsePanel.module.css'

export function ChromeDetailPage({
  plugin,
  setup,
  onBack
}: {
  plugin: PluginEntry | null
  setup: ChromeSetup
  onBack: () => void
}): JSX.Element {
  const t = useT()
  const summary = chromeSetupSummary(setup.status, t)
  const badgeTone = chromeStatusTone(summary.tone)
  const nativeHostLabel = chromeNativeHostActionLabel(setup.status, t)

  return (
    <SettingsPanelShell
      title={t('settings.chrome.detailTitle')}
      description={(plugin && pluginSubtitle(plugin)) || t('settings.chrome.pageDescription')}
      breadcrumb={
        <SettingsBreadcrumb
          parentLabel={t('settings.chrome.pageTitle')}
          currentLabel={t('settings.chrome.detailTitle')}
          onBack={onBack}
        />
      }
    >
      <SettingsGroup title={t('settings.chrome.connectionStatus')} flush>
        <div className={styles.detailStatus}>
          <div className={styles.detailIdentity}>
            {plugin && <PluginIcon plugin={plugin} role="list" size={38} />}
            <span className="dc-status-badge" data-tone={badgeTone === 'neutral' ? undefined : badgeTone}>
              {summary.label}
            </span>
          </div>
          <div className={styles.detailActions}>
            <IconButton
              icon={<RefreshIcon size={15} />}
              label={t('settings.chrome.refreshStatus')}
              tooltipLabel={t('settings.chrome.refreshStatus')}
              tooltipPlacement="top"
              onClick={() => void setup.reload()}
              disabled={setup.loading}
              disabledReason={setup.loading ? t('settings.loading') : undefined}
            />
            <IconButton
              icon={<OpenInBrowserIcon size={15} />}
              label={t('settings.chrome.openChrome')}
              tooltipLabel={t('settings.chrome.openChrome')}
              tooltipPlacement="top"
              onClick={() => void setup.openChrome()}
              disabled={setup.opening}
              disabledReason={setup.opening ? t('settings.chrome.opening') : undefined}
            />
            {setup.status && !setupResultOk(setup.status.extension) && (
              <IconButton
                icon={<ExtensionsIcon size={15} />}
                label={t('settings.chrome.openExtensions')}
                tooltipLabel={t('settings.chrome.openExtensions')}
                tooltipPlacement="top"
                onClick={() => void setup.openChrome(CHROME_EXTENSIONS_URL)}
                disabled={setup.opening}
                disabledReason={setup.opening ? t('settings.chrome.opening') : undefined}
              />
            )}
            <IconButton
              icon={<WrenchIcon size={15} />}
              label={nativeHostLabel}
              tooltipLabel={nativeHostLabel}
              tooltipPlacement="top"
              onClick={() => void setup.installNativeHost()}
              disabled={setup.nativeHostInstalling}
              disabledReason={setup.nativeHostInstalling ? t('settings.chrome.installingNativeHost') : undefined}
            />
          </div>
        </div>
      </SettingsGroup>
    </SettingsPanelShell>
  )
}
