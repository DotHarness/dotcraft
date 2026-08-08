import { useCallback, useEffect, useMemo, useState, type CSSProperties } from 'react'

import type { VoiceErrorCode, VoiceRuntimeSnapshot } from '../../../../shared/voice'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { useConfirmDialog } from '../../ui/ConfirmDialog'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { SettingsSelect } from '../ui/SettingsSelect'
import { VoiceCaptureError } from '../../../voice/audioCapture'
import { isBlockedMicrophonePermission, probeMicrophoneAccess } from '../../../voice/microphoneAccess'
import { formatMicrophoneLabel } from '../../../voice/microphoneLabels'
import { useVoiceStore } from '../../../voice/voiceStore'

const EMPTY_SNAPSHOT: VoiceRuntimeSnapshot = {
  model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
  sessions: [],
  capacity: 2
}

interface AudioInputOption {
  deviceId: string
  label: string
}

export function VoicePanel(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const initializeVoice = useVoiceStore((state) => state.initialize)
  const microphonePermission = useVoiceStore((state) => state.microphonePermission)
  const setMicrophonePermission = useVoiceStore((state) => state.setMicrophonePermission)
  const openMicrophoneSettings = useVoiceStore((state) => state.openMicrophoneSettings)
  const deviceFallback = useVoiceStore((state) => state.deviceFallback)
  const markDeviceFallback = useVoiceStore((state) => state.markDeviceFallback)
  const clearDeviceFallback = useVoiceStore((state) => state.clearDeviceFallback)
  const clearDeviceErrors = useVoiceStore((state) => state.clearDeviceErrors)
  const [snapshot, setSnapshot] = useState<VoiceRuntimeSnapshot>(EMPTY_SNAPSHOT)
  const [devices, setDevices] = useState<AudioInputOption[]>([])
  const [deviceId, setDeviceId] = useState('')
  const [deviceMissing, setDeviceMissing] = useState(false)
  const [deviceIssue, setDeviceIssue] = useState<VoiceErrorCode | null>(null)
  const [busy, setBusy] = useState(false)

  const refreshDevices = useCallback(async (preferredDeviceId = deviceId): Promise<void> => {
    if (!navigator.mediaDevices?.enumerateDevices) {
      setDevices([])
      setDeviceMissing(true)
      return
    }
    const inputs = (await navigator.mediaDevices.enumerateDevices())
      .filter((device) => device.kind === 'audioinput')
      .map((device, index) => ({
        deviceId: device.deviceId,
        label: device.label
          ? formatMicrophoneLabel(device.label)
          : t('settings.voice.microphone.unnamed', { index: index + 1 })
      }))
    setDevices(inputs)
    if (inputs.length > 0) clearDeviceErrors()
    const preferredDeviceMissing = preferredDeviceId !== '' && !inputs.some((device) => device.deviceId === preferredDeviceId)
    setDeviceMissing(preferredDeviceMissing)
    if (preferredDeviceMissing) {
      setDeviceId('')
      await window.api.settings.set({ voice: { deviceId: '' } }).catch(() => {})
      markDeviceFallback()
    }
  }, [clearDeviceErrors, deviceId, markDeviceFallback, t])

  useEffect(() => {
    initializeVoice()
    let disposed = false
    void Promise.all([window.api.voice.getSnapshot(), window.api.settings.get()])
      .then(([nextSnapshot, settings]) => {
        if (disposed) return
        setSnapshot(nextSnapshot)
        setDeviceId(settings.voice?.deviceId ?? '')
      })
      .catch(() => {})
    const unsubscribe = window.api.voice.onSnapshot((next) => setSnapshot(next))
    return () => {
      disposed = true
      unsubscribe()
    }
  }, [initializeVoice])

  useEffect(() => {
    const media = navigator.mediaDevices
    if (!media?.addEventListener) return
    const onDeviceChange = (): void => {
      if (useVoiceStore.getState().microphonePermission === 'granted') void refreshDevices()
    }
    media.addEventListener('devicechange', onDeviceChange)
    return () => media.removeEventListener('devicechange', onDeviceChange)
  }, [refreshDevices])

  useEffect(() => {
    if (microphonePermission !== 'granted' || deviceId === '' || devices.length > 0) return
    void refreshDevices(deviceId)
  }, [deviceId, devices.length, microphonePermission, refreshDevices])

  const deviceOptions = useMemo(() => [
    {
      value: '',
      label: t('settings.voice.microphone.systemDefault')
    },
    ...devices.map((device) => ({ value: device.deviceId, label: device.label }))
  ], [devices, t])

  async function setPreferredDevice(next: string): Promise<void> {
    setDeviceId(next)
    setDeviceMissing(false)
    setDeviceIssue(null)
    clearDeviceFallback()
    clearDeviceErrors()
    await window.api.settings.set({ voice: { deviceId: next } })
  }

  async function prepareDeviceMenu(): Promise<boolean> {
    try {
      const probe = await probeMicrophoneAccess(deviceId || undefined)
      setMicrophonePermission('granted')
      setDeviceIssue(null)
      clearDeviceErrors()
      if (probe.usedDefaultDevice) {
        setDeviceId('')
        setDeviceMissing(false)
        markDeviceFallback()
        await window.api.settings.set({ voice: { deviceId: '' } })
        await refreshDevices('')
      } else {
        await refreshDevices(deviceId)
      }
      return true
    } catch (error) {
      const code = error instanceof VoiceCaptureError ? error.code : 'device-missing'
      if (code === 'permission-denied') setMicrophonePermission('denied')
      else setDeviceIssue(code)
      return false
    }
  }

  async function run(action: () => Promise<void>): Promise<void> {
    if (busy) return
    setBusy(true)
    try {
      await action()
    } finally {
      setBusy(false)
    }
  }

  async function removeModel(): Promise<void> {
    const accepted = await confirm({
      title: t('settings.voice.model.removeTitle'),
      message: t('settings.voice.model.removeMessage'),
      confirmLabel: t('settings.voice.model.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (accepted) await run(() => window.api.voice.removeModel())
  }

  const model = snapshot.model
  const permissionBlocked = isBlockedMicrophonePermission(microphonePermission)
  const progress = model.bytesTotal && model.bytesTotal > 0
    ? Math.min(100, Math.round((model.bytesDownloaded / model.bytesTotal) * 100))
    : null

  return (
    <SettingsPanelShell
      title={t('settings.tab.voice')}
      description={t('settings.voice.description')}
    >
      <SettingsGroup
        title={t('settings.voice.microphone.title')}
      >
        <SettingsRow
          label={t('settings.voice.microphone.input')}
          description={deviceMissing
            ? t('settings.voice.microphone.missing')
            : deviceIssue === 'device-unavailable'
              ? t('settings.voice.microphone.unavailable')
              : deviceFallback
                ? t('settings.voice.microphone.fallback')
                : t('settings.voice.microphone.hint')}
          control={(
            <SettingsSelect
              value={deviceMissing ? '' : deviceId}
              onValueChange={(value) => { void setPreferredDevice(value) }}
              onBeforeOpen={prepareDeviceMenu}
              options={deviceOptions}
              disabled={permissionBlocked}
              style={{ width: 220 }}
              ariaLabel={t('settings.voice.microphone.input')}
            />
          )}
        />
        {permissionBlocked && (
          <SettingsRow
            label={t('settings.voice.microphone.permissionRequired')}
            description={t('settings.voice.microphone.permissionDescription')}
            control={(
              <Button variant="secondary" onClick={() => { void openMicrophoneSettings() }}>
                {t('settings.voice.microphone.openSystemSettings')}
              </Button>
            )}
          />
        )}
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.voice.models.title')}
      >
        <SettingsRow
          label={t('settings.voice.model.name')}
          description={<ModelDescription phase={model.phase} progress={progress} />}
          control={(
            <ModelAction
              phase={model.phase}
              busy={busy}
              onInstall={() => run(() => window.api.voice.installModel())}
              onCancel={() => run(() => window.api.voice.cancelModelInstall())}
              onRetry={() => run(() => window.api.voice.installModel())}
              onRepair={() => run(() => window.api.voice.repairModel())}
              onRemove={() => { void removeModel() }}
            />
          )}
        />
        {model.phase === 'downloading' && (
          <SettingsRow>
            <progress
              aria-label={t('settings.voice.model.downloading')}
              value={model.bytesDownloaded}
              max={model.bytesTotal ?? undefined}
              style={progressStyle}
            />
          </SettingsRow>
        )}
      </SettingsGroup>
    </SettingsPanelShell>
  )
}

function ModelDescription({
  phase,
  progress
}: {
  phase: VoiceRuntimeSnapshot['model']['phase']
  progress: number | null
}): JSX.Element {
  const t = useT()
  if (phase === 'installed') return <>{t('settings.voice.model.installed')}</>
  if (phase === 'downloading') {
    return <>{progress == null
      ? t('settings.voice.model.downloading')
      : t('settings.voice.model.downloadingProgress', { progress })}</>
  }
  if (phase === 'failed') return <>{t('settings.voice.model.failed')}</>
  if (phase === 'damaged') return <>{t('settings.voice.model.damaged')}</>
  return <>{t('settings.voice.model.notInstalled')}</>
}

function ModelAction({
  phase,
  busy,
  onInstall,
  onCancel,
  onRetry,
  onRepair,
  onRemove
}: {
  phase: VoiceRuntimeSnapshot['model']['phase']
  busy: boolean
  onInstall: () => void
  onCancel: () => void
  onRetry: () => void
  onRepair: () => void
  onRemove: () => void
}): JSX.Element {
  const t = useT()
  if (phase === 'installed') {
    return <Button variant="ghost" disabled={busy} onClick={onRemove}>{t('settings.voice.model.remove')}</Button>
  }
  if (phase === 'downloading') {
    return <Button variant="ghost" disabled={busy} onClick={onCancel}>{t('common.cancel')}</Button>
  }
  if (phase === 'failed') {
    return <Button variant="secondary" disabled={busy} onClick={onRetry}>{t('settings.voice.model.retry')}</Button>
  }
  if (phase === 'damaged') {
    return <Button variant="secondary" disabled={busy} onClick={onRepair}>{t('settings.voice.model.repair')}</Button>
  }
  return <Button variant="secondary" disabled={busy} onClick={onInstall}>{t('settings.voice.model.install')}</Button>
}

const progressStyle: CSSProperties = {
  width: '100%',
  height: 6,
  accentColor: 'var(--accent)'
}
