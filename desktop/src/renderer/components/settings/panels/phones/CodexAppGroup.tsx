import { useCallback, useEffect, useState, type JSX } from 'react'

import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { PillSwitch } from '../../../ui/PillSwitch'
import { useT } from '../../../../contexts/LocaleContext'
import { connectCodexRemote, useCodexRemoteStore } from '../../../../stores/codexRemoteStore'
import { addToast } from '../../../../stores/toastStore'
import { useUIStore } from '../../../../stores/uiStore'
import type { CodexRemoteStatus } from '../../../../../shared/codexRemote'
import { StatusIndicator, statusTextStyle, type StatusIndicatorTone } from '../settingsStatusStyles'
import { CodexPairDialog } from './CodexPairDialog'

function statusLine(
  t: ReturnType<typeof useT>,
  status: CodexRemoteStatus
): { tone: StatusIndicatorTone; text: string } | null {
  switch (status.state) {
    case 'connecting':
      return { tone: 'pending', text: t('settings.codexApp.status.connecting') }
    case 'on':
      return { tone: 'success', text: t('settings.codexApp.status.on', { name: status.hostName }) }
    case 'failed':
      return { tone: 'error', text: status.failureMessage || t('settings.codexApp.status.failed') }
    default:
      return null
  }
}

export function CodexAppGroup(): JSX.Element | null {
  const t = useT()
  const status = useCodexRemoteStore((state) => state.status)
  const loadFailure = useCodexRemoteStore((state) => state.loadFailure)
  const pendingEnabled = useCodexRemoteStore((state) => state.pendingEnabled)
  const [pairing, setPairing] = useState(false)
  const closePairing = useCallback(() => setPairing(false), [])

  useEffect(() => connectCodexRemote(), [])

  const running = status !== null && status.state !== 'off'
  const switching = pendingEnabled !== null
  const line = loadFailure
    ? { tone: 'error' as const, text: loadFailure.message || t('settings.phones.hubNoAnswer') }
    : status ? statusLine(t, status) : null

  async function toggle(next: boolean): Promise<void> {
    const failure = await useCodexRemoteStore.getState().setEnabled(next)
    if (failure && !next) addToast(t('settings.codexApp.disableFailed'), 'error')
  }

  function openProviders(): void {
    useUIStore.getState().setActiveSettingsTab('llmService')
  }

  return (
    <SettingsGroup title={t('settings.codexApp.title')}>
      <SettingsRow
        label={t('settings.codexApp.access.label')}
        description={
          line ? (
            <span style={statusTextStyle()}>
              <StatusIndicator tone={line.tone} />
              {line.text}
            </span>
          ) : (
            t('settings.codexApp.access.description')
          )
        }
        control={
          <ActionTooltip
            label=""
            disabledReason={status && !status.signedIn ? t('settings.codexApp.access.requiresSignIn') : undefined}
          >
            <PillSwitch
              checked={pendingEnabled ?? running}
              disabled={switching || !status?.signedIn}
              aria-busy={switching}
              aria-label={t('settings.codexApp.access.label')}
              onChange={(next) => void toggle(next)}
            />
          </ActionTooltip>
        }
      />

      {status && !status.signedIn && (
        <SettingsRow
          label={t('settings.codexApp.signIn.label')}
          description={t('settings.codexApp.signIn.description')}
          control={
            <Button variant="secondary" onClick={openProviders}>
              {t('settings.codexApp.signIn.action')}
            </Button>
          }
        />
      )}

      {status?.signedIn && status.state === 'on' && (
        <SettingsRow
          label={t('settings.codexApp.pair.label')}
          description={t('settings.codexApp.pair.description')}
          control={
            <Button variant="secondary" onClick={() => setPairing(true)}>
              {t('settings.codexApp.pair.action')}
            </Button>
          }
        />
      )}

      {pairing && status && <CodexPairDialog hostName={status.hostName} onClose={closePairing} />}
    </SettingsGroup>
  )
}
