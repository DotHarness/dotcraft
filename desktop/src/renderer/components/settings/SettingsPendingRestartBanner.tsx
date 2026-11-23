import type { JSX } from 'react'

import { useT } from '../../contexts/LocaleContext'
import { usePendingRestartStore } from '../../stores/pendingRestartStore'
import { Button } from '../ui/Button'

export function SettingsPendingRestartBanner(): JSX.Element | null {
  const t = useT()
  const visible = usePendingRestartStore((state) => state.visible)
  const applying = usePendingRestartStore((state) => state.applying)
  const messageKey = usePendingRestartStore((state) => state.messageKey)
  const applyKey = usePendingRestartStore((state) => state.applyKey)
  const applyingKey = usePendingRestartStore((state) => state.applyingKey)
  const apply = usePendingRestartStore((state) => state.apply)
  const ignore = usePendingRestartStore((state) => state.ignore)
  if (!visible) return null

  return (
    <div className="dotcraft-settings-restart-banner" role="status" aria-live="polite">
      <span className="dotcraft-settings-restart-banner__message">{t(messageKey)}</span>
      <span className="dotcraft-settings-restart-banner__actions">
        <Button variant="ghost" size="sm" onClick={() => ignore()} disabled={applying}>
          {t('settings.pendingRestart.ignore')}
        </Button>
        <Button variant="outline" size="sm" onClick={() => { void apply() }} disabled={applying}>
          {applying ? t(applyingKey) : t(applyKey)}
        </Button>
      </span>
    </div>
  )
}
