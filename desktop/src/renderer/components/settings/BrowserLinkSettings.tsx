import type { JSX } from 'react'
import type { LinkPreferences } from '../../../shared/desktopSettings'
import { useT } from '../../contexts/LocaleContext'
import { useLinkPreferencesStore } from '../../stores/linkPreferencesStore'
import { addToast } from '../../stores/toastStore'
import { PillSwitch } from '../ui/PillSwitch'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import { SegmentedControl } from './ui/SegmentedControl'

export function BrowserLinkSettings(): JSX.Element {
  const t = useT()
  const preferences = useLinkPreferencesStore((state) => state.preferences)
  const save = useLinkPreferencesStore((state) => state.save)
  const options = [
    { value: 'inAppBrowser' as const, label: t('settings.browserUse.links.inAppBrowser') },
    { value: 'systemBrowser' as const, label: t('settings.browserUse.links.systemBrowser') }
  ]

  async function change(patch: Partial<LinkPreferences>): Promise<void> {
    try {
      await save({ ...preferences, ...patch })
    } catch (error) {
      addToast(t('settings.browserUse.links.saveFailed', {
        error: error instanceof Error ? error.message : String(error)
      }), 'error')
    }
  }

  return (
    <SettingsGroup title={t('settings.browserUse.links.title')}>
      <SettingsRow
        label={t('settings.browserUse.links.web')}
        description={t('settings.browserUse.links.webHint')}
        control={
          <SegmentedControl
            value={preferences.web}
            options={options}
            ariaLabel={t('settings.browserUse.links.web')}
            onChange={(next) => { void change({ web: next }) }}
          />
        }
      />
      <SettingsRow
        label={t('settings.browserUse.links.local')}
        description={t('settings.browserUse.links.localHint')}
        control={
          <SegmentedControl
            value={preferences.local}
            options={options}
            ariaLabel={t('settings.browserUse.links.local')}
            onChange={(next) => { void change({ local: next }) }}
          />
        }
      />
      <SettingsRow
        label={t('settings.browserUse.links.favicons')}
        description={t('settings.browserUse.links.faviconsHint')}
        control={
          <PillSwitch
            checked={preferences.favicons}
            aria-label={t('settings.browserUse.links.favicons')}
            onChange={(next) => { void change({ favicons: next }) }}
          />
        }
      />
    </SettingsGroup>
  )
}
