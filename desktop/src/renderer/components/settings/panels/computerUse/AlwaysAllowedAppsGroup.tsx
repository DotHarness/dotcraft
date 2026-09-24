import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import { Trash2 } from 'lucide-react'

import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'
import { DesktopIcon } from '../../../ui/AppIcons'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { IconButton } from '../../../ui/IconButton'
import { IdentityMark } from '../../../ui/IdentityMark'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import styles from './ComputerUsePanel.module.css'

interface AllowedApp {
  id: string
  displayName: string
}

async function readAllowedApps(): Promise<AllowedApp[]> {
  const settings = await window.api.settings.get()
  return settings.computerUse?.alwaysAllowedApps ?? []
}

export function AlwaysAllowedAppsGroup(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [apps, setApps] = useState<AllowedApp[]>([])
  const [icons, setIcons] = useState<Record<string, string | null>>({})
  const requestedIcons = useRef(new Set<string>())

  const reload = useCallback(async (): Promise<void> => {
    const next = await readAllowedApps().catch(() => null)
    if (next) setApps(next)
  }, [])

  useEffect(() => {
    void reload()
    const onFocus = (): void => { void reload() }
    window.addEventListener('focus', onFocus)
    return () => window.removeEventListener('focus', onFocus)
  }, [reload])

  useEffect(() => {
    for (const app of apps) {
      if (requestedIcons.current.has(app.id)) continue
      requestedIcons.current.add(app.id)
      void window.api.computerUse.getAppIcon(app.id)
        .catch(() => null)
        .then((icon) => setIcons((current) => ({ ...current, [app.id]: icon })))
    }
  }, [apps])

  async function remove(app: AllowedApp): Promise<void> {
    const accepted = await confirm({
      title: t('settings.computerUse.allowedApps.removeConfirmTitle', { app: app.displayName }),
      message: t('settings.computerUse.allowedApps.removeConfirmBody'),
      confirmLabel: t('settings.computerUse.allowedApps.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!accepted) return
    try {
      // Re-read so an entry the main process added while the dialog was open survives.
      const next = (await readAllowedApps()).filter((entry) => entry.id !== app.id)
      await window.api.settings.set({ computerUse: { alwaysAllowedApps: next } })
      setApps(next)
    } catch (err) {
      addToast(t('settings.saveFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  return (
    <SettingsGroup title={t('settings.computerUse.allowedApps.title')}>
      {apps.length === 0 ? (
        <SettingsRow>
          <div className={styles.empty}>{t('settings.computerUse.allowedApps.empty')}</div>
        </SettingsRow>
      ) : apps.map((app) => (
        <SettingsRow key={app.id}>
          <div className={styles.appRow}>
            <IdentityMark role="compact" size={24} src={icons[app.id]} fallback={<DesktopIcon size={14} />} />
            <span className={styles.appName}>{app.displayName}</span>
            <IconButton
              icon={<Trash2 size={15} aria-hidden />}
              label={t('settings.computerUse.allowedApps.removeAria', { app: app.displayName })}
              tooltipLabel={t('settings.computerUse.allowedApps.remove')}
              tooltipPlacement="top"
              onClick={() => void remove(app)}
            />
          </div>
        </SettingsRow>
      ))}
    </SettingsGroup>
  )
}
