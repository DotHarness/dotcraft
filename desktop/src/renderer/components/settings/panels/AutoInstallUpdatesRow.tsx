import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { PillSwitch } from '../../ui/PillSwitch'
import { SettingsRow } from '../SettingsGroup'

export function AutoInstallUpdatesRow(): JSX.Element | null {
  const t = useT()
  const [supported, setSupported] = useState(false)
  const [enabled, setEnabled] = useState(true)

  useEffect(() => {
    let cancelled = false
    void Promise.all([window.api.updates.getState(), window.api.settings.get()]).then(([state, settings]) => {
      if (cancelled) return
      setSupported(state.status !== 'unsupported')
      setEnabled(settings.autoInstallUpdates !== false)
    })
    return () => { cancelled = true }
  }, [])

  if (!supported) return null

  async function toggle(checked: boolean): Promise<void> {
    setEnabled(checked)
    try {
      await window.api.settings.set({ autoInstallUpdates: checked })
    } catch (error) {
      setEnabled(!checked)
      addToast(t('settings.saveFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
    }
  }

  return (
    <SettingsRow
      label={t('settings.general.autoInstallUpdates')}
      description={t('settings.general.autoInstallUpdatesHint')}
      control={
        <PillSwitch
          checked={enabled}
          aria-label={t('settings.general.autoInstallUpdates')}
          onChange={(checked) => { void toggle(checked) }}
        />
      }
    />
  )
}
