import type { JSX } from 'react'
import type { LinkOpenPreferences, LinkOpenTarget } from '../../../shared/desktopSettings'
import { useT } from '../../contexts/LocaleContext'
import { useLinkOpenPreferencesStore } from '../../stores/linkOpenPreferencesStore'
import { addToast } from '../../stores/toastStore'
import { SettingsGroup, SettingsRow } from './SettingsGroup'
import { SegmentedControl } from './ui/SegmentedControl'

export function BrowserLinkSettings(): JSX.Element {
  const t = useT()
  const preferences = useLinkOpenPreferencesStore((state) => state.preferences)
  const save = useLinkOpenPreferencesStore((state) => state.save)
  const options = [
    { value: 'inAppBrowser' as const, label: t('settings.browserUse.links.inAppBrowser') },
    { value: 'systemBrowser' as const, label: t('settings.browserUse.links.systemBrowser') }
  ]

  async function change(key: keyof LinkOpenPreferences, target: LinkOpenTarget): Promise<void> {
    try {
      await save({ ...preferences, [key]: target })
    } catch (error) {
      addToast(t('settings.browserUse.links.saveFailed', {
        error: error instanceof Error ? error.message : String(error)
      }), 'error')
    }
  }

  return (
    <SettingsGroup title={t('settings.browserUse.links.title')} description={t('settings.browserUse.links.shortcutHint')}>
      <SettingsRow
        label={t('settings.browserUse.links.web')}
        description={t('settings.browserUse.links.webHint')}
        control={
          <SegmentedControl
            value={preferences.web}
            options={options}
            ariaLabel={t('settings.browserUse.links.web')}
            onChange={(next) => { void change('web', next) }}
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
            onChange={(next) => { void change('local', next) }}
          />
        }
      />
    </SettingsGroup>
  )
}
