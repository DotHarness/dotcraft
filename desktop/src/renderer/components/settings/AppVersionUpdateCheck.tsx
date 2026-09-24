import { useEffect, useState, type JSX } from 'react'

import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { settingsMetaTextStyle } from './settingsTypography'

export function AppVersionUpdateCheck({ version }: { version: string }): JSX.Element {
  const t = useT()
  const [supported, setSupported] = useState(false)
  const [checking, setChecking] = useState(false)
  const [result, setResult] = useState<AppUpdateState | null>(null)

  useEffect(() => {
    let disposed = false
    void window.api.updates.getState()
      .then((state) => {
        if (!disposed) setSupported(state.status !== 'unsupported')
      })
      .catch(() => {})
    return () => {
      disposed = true
    }
  }, [])

  async function check(): Promise<void> {
    setChecking(true)
    try {
      setResult(await window.api.updates.check())
    } finally {
      setChecking(false)
    }
  }

  const outcome = result ? describeOutcome(result, t) : null

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <span style={settingsMetaTextStyle()} aria-live="polite">
        {t('settings.version')} {version}
        {outcome && ` · ${outcome}`}
      </span>
      {supported && (
        <Button size="sm" loading={checking} onClick={() => void check()}>
          {t('settings.checkForUpdates')}
        </Button>
      )}
    </span>
  )
}

function describeOutcome(
  state: AppUpdateState,
  t: (key: string, vars?: Record<string, string | number>) => string
): string | null {
  if (state.status === 'not-available') return t('settings.updateCheck.upToDate')
  if (state.status === 'error') return t('settings.updateCheck.failed')
  if (state.update) return t('settings.updateCheck.available', { version: state.update.latestVersion })
  return null
}
