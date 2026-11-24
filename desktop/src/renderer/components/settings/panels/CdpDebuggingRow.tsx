import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { addToast } from '../../../stores/toastStore'
import { useConfirmDialog } from '../../ui/ConfirmDialog'
import { PillSwitch } from '../../ui/PillSwitch'
import { SettingsRow } from '../SettingsGroup'

export function CdpDebuggingRow(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [enabled, setEnabled] = useState(false)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    let cancelled = false
    void window.api.settings.get().then((settings) => {
      if (!cancelled) setEnabled(settings.cdpDebugging === true)
    })
    return () => { cancelled = true }
  }, [])

  async function toggle(checked: boolean): Promise<void> {
    if (checked && !(await confirm({
      title: t('settings.cdpDebugging.warningTitle'),
      message: t('settings.cdpDebugging.warningBody'),
      confirmLabel: t('settings.cdpDebugging.warningConfirm'),
      cancelLabel: t('common.cancel'),
      danger: true
    }))) return
    setEnabled(checked)
    setSaving(true)
    try {
      await window.api.settings.set({ cdpDebugging: checked })
    } catch (error) {
      setEnabled(!checked)
      addToast(error instanceof Error ? error.message : String(error), 'error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <SettingsRow
      label={t('settings.cdpDebugging.label')}
      description={t('settings.cdpDebugging.description')}
      control={
        <PillSwitch
          checked={enabled}
          disabled={saving}
          aria-label={t('settings.cdpDebugging.label')}
          onChange={(checked) => { void toggle(checked) }}
        />
      }
    />
  )
}
