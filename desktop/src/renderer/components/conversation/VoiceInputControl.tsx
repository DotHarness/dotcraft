import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Mic, RotateCcw, Square } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { sessionForThread, useVoiceStore } from '../../voice/voiceStore'
import { isBlockedMicrophonePermission } from '../../voice/microphoneAccess'
import { ActionTooltip } from '../ui/ActionTooltip'
import { VoiceSetupDialog, type VoiceSetupStage } from './VoiceSetupDialog'

interface VoiceInputControlProps {
  threadId: string
}

export function VoiceInputStatus({ threadId }: VoiceInputControlProps): JSX.Element | null {
  const recording = useVoiceStore((state) => (
    state.recording?.threadId === threadId ? state.recording : null
  ))
  if (!recording) return null
  return (
    <div style={waveformStatusStyle}>
      <VoiceWaveform level={recording.level} />
    </div>
  )
}

export function VoiceInputControl({ threadId }: VoiceInputControlProps): JSX.Element {
  const t = useT()
  const initialize = useVoiceStore((state) => state.initialize)
  const snapshot = useVoiceStore((state) => state.snapshot)
  const globalRecording = useVoiceStore((state) => state.recording)
  const microphonePermission = useVoiceStore((state) => state.microphonePermission)
  const recording = globalRecording?.threadId === threadId ? globalRecording : null
  const localError = useVoiceStore((state) => state.localErrors[threadId])
  const startRecording = useVoiceStore((state) => state.startRecording)
  const stopRecording = useVoiceStore((state) => state.stopRecording)
  const abortRecording = useVoiceStore((state) => state.abortRecording)
  const retry = useVoiceStore((state) => state.retry)
  const openMicrophoneSettings = useVoiceStore((state) => state.openMicrophoneSettings)
  const [setupStage, setSetupStage] = useState<VoiceSetupStage | null>(null)
  const shortcutHeld = useRef(false)

  useEffect(() => initialize(), [initialize])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' && useVoiceStore.getState().recording?.threadId === threadId) {
        event.preventDefault()
        void abortRecording()
        return
      }
      if (event.code !== 'KeyD' || !event.ctrlKey || !event.shiftKey || event.repeat || !document.hasFocus()) return
      if (snapshot.model.phase !== 'installed') return
      if (isBlockedMicrophonePermission(useVoiceStore.getState().microphonePermission)) {
        setSetupStage('recovery')
        return
      }
      shortcutHeld.current = true
      event.preventDefault()
      void startRecording(threadId)
    }
    const onKeyUp = (event: KeyboardEvent): void => {
      if (event.code !== 'KeyD' || !shortcutHeld.current) return
      shortcutHeld.current = false
      event.preventDefault()
      void stopRecording('insert')
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
      if (useVoiceStore.getState().recording?.threadId === threadId) {
        void useVoiceStore.getState().stopRecording('insert')
      }
    }
  }, [abortRecording, snapshot.model.phase, startRecording, stopRecording, threadId])

  const session = sessionForThread(snapshot, threadId)
  const occupied = snapshot.sessions.length + (globalRecording ? 1 : 0)
  const queueFull = !recording && !session && occupied >= snapshot.capacity

  const view = useMemo(() => {
    if (recording) return { label: t('voice.control.stop'), disabled: false, kind: 'recording' as const }
    if (session?.phase === 'retryable') return { label: t('voice.control.retry'), disabled: false, kind: 'retry' as const }
    if (session?.phase === 'queued' || session?.phase === 'transcribing') {
      return { label: t('voice.control.processing'), disabled: true, kind: 'processing' as const }
    }
    if (snapshot.model.phase === 'downloading') {
      return { label: t('settings.voice.model.downloading'), disabled: true, kind: 'downloading' as const }
    }
    if (queueFull || localError === 'queue-full') {
      return { label: t('voice.control.busy'), disabled: true, kind: 'mic' as const }
    }
    if (localError === 'device-missing') {
      return { label: t('voice.control.deviceMissing'), disabled: false, kind: 'mic' as const }
    }
    if (isBlockedMicrophonePermission(microphonePermission)) {
      return { label: t('voice.control.permissionDenied'), disabled: false, kind: 'mic' as const }
    }
    if (localError === 'device-unavailable') {
      return { label: t('voice.control.deviceUnavailable'), disabled: false, kind: 'mic' as const }
    }
    return { label: t('voice.control.start'), disabled: false, kind: 'mic' as const }
  }, [localError, microphonePermission, queueFull, recording, session?.phase, snapshot.model.phase, t])

  async function activate(): Promise<void> {
    if (view.disabled) return
    if (recording) {
      await stopRecording('insert')
      return
    }
    if (session?.phase === 'retryable') {
      await retry(session.sessionId)
      return
    }
    if (snapshot.model.phase !== 'installed') {
      setSetupStage('setup')
      return
    }
    if (isBlockedMicrophonePermission(microphonePermission)) {
      setSetupStage('recovery')
      return
    }
    await startRecording(threadId)
  }

  async function continueSetup(): Promise<void> {
    if (setupStage === 'setup') {
      setSetupStage(null)
      void window.api.voice.installModel()
      return
    }
    setSetupStage(null)
    await openMicrophoneSettings()
  }

  const progress = snapshot.model.bytesTotal && snapshot.model.bytesTotal > 0
    ? snapshot.model.bytesDownloaded / snapshot.model.bytesTotal
    : 0.2
  const elapsedMs = recording?.elapsedMs
    ?? (session?.phase === 'queued' || session?.phase === 'transcribing' ? session.durationMs : null)

  return (
    <>
      {elapsedMs != null && <time style={timerStyle}>{formatElapsed(elapsedMs)}</time>}
      <ActionTooltip label={view.label} shortcut={view.kind === 'mic' ? ['Ctrl', 'Shift', 'D'] : undefined} placement="top">
        <button
          type="button"
          aria-label={view.label}
          aria-pressed={recording ? true : undefined}
          disabled={view.disabled}
          onClick={() => { void activate() }}
          style={controlStyle(recording != null)}
        >
          {view.kind === 'recording' || view.kind === 'processing'
            ? <Square size={11} fill="currentColor" strokeWidth={0} aria-hidden style={{ display: 'block' }} />
            : view.kind === 'retry'
              ? <RotateCcw size={16} aria-hidden />
              : view.kind === 'downloading'
                ? <DownloadRing progress={progress} />
                : <Mic size={16} aria-hidden />}
        </button>
      </ActionTooltip>
      {setupStage && (
        <VoiceSetupDialog
          stage={setupStage}
          onContinue={() => { void continueSetup() }}
          onCancel={() => setSetupStage(null)}
        />
      )}
    </>
  )
}

function VoiceWaveform({ level }: { level: number }): JSX.Element {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const historyRef = useRef<number[]>([])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const resizeObserver = new ResizeObserver(() => {
      ensureWaveformCapacity(canvas, historyRef)
      drawWaveform(canvas, historyRef.current)
    })
    resizeObserver.observe(canvas)
    return () => resizeObserver.disconnect()
  }, [])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    ensureWaveformCapacity(canvas, historyRef)
    historyRef.current.push(Math.max(0, Math.min(1, level)))
    historyRef.current.shift()
    drawWaveform(canvas, historyRef.current)
  }, [level])
  return <canvas ref={canvasRef} style={waveformStyle} aria-hidden />
}

function ensureWaveformCapacity(
  canvas: HTMLCanvasElement,
  historyRef: { current: number[] }
): void {
  const count = Math.max(20, Math.floor(canvas.clientWidth / 4))
  if (historyRef.current.length === count) return
  const previous = historyRef.current.slice(-count)
  historyRef.current = [
    ...Array.from({ length: Math.max(0, count - previous.length) }, () => 0),
    ...previous
  ]
}

function drawWaveform(canvas: HTMLCanvasElement, history: number[]): void {
  const context = canvas.getContext('2d')
  const width = canvas.clientWidth
  const height = canvas.clientHeight
  if (!context || width <= 0 || height <= 0) return

  const ratio = window.devicePixelRatio || 1
  const pixelWidth = Math.round(width * ratio)
  const pixelHeight = Math.round(height * ratio)
  if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
    canvas.width = pixelWidth
    canvas.height = pixelHeight
  }

  context.setTransform(ratio, 0, 0, ratio, 0, 0)
  context.clearRect(0, 0, width, height)
  context.fillStyle = getComputedStyle(canvas).color || '#8b8b8b'

  const slot = width / Math.max(1, history.length)
  const barWidth = Math.max(1, slot * 0.48)
  const center = height / 2
  for (let index = 0; index < history.length; index += 1) {
    const sample = history[index] ?? 0
    const barHeight = Math.max(1, Math.min(height * 0.92, sample * height * 4.8))
    context.globalAlpha = sample > 0.01 ? 0.82 : 0.38
    context.fillRect(index * slot, center - barHeight / 2, barWidth, barHeight)
  }
  context.globalAlpha = 1
}

function DownloadRing({ progress }: { progress: number }): JSX.Element {
  const circumference = 2 * Math.PI * 7
  return (
    <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden>
      <circle cx="10" cy="10" r="7" fill="none" stroke="currentColor" opacity="0.2" strokeWidth="2" />
      <circle
        cx="10"
        cy="10"
        r="7"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeDasharray={circumference}
        strokeDashoffset={circumference * (1 - Math.max(0.05, Math.min(1, progress)))}
        transform="rotate(-90 10 10)"
      />
    </svg>
  )
}

function formatElapsed(elapsedMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(elapsedMs / 1_000))
  return `${Math.floor(totalSeconds / 60)}:${String(totalSeconds % 60).padStart(2, '0')}`
}

function controlStyle(active: boolean): CSSProperties {
  return {
    width: 32,
    height: 32,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 0,
    border: 'none',
    borderRadius: 999,
    color: active ? 'var(--text-primary)' : 'var(--composer-footer-text)',
    background: active ? 'var(--bg-tertiary)' : 'transparent'
  }
}

const waveformStatusStyle: CSSProperties = {
  display: 'inline-flex',
  flex: 1,
  minWidth: 0,
  alignItems: 'center',
  color: 'var(--composer-footer-text)'
}

const waveformStyle: CSSProperties = {
  display: 'block',
  flex: 1,
  width: '100%',
  minWidth: 80,
  height: 22,
  color: 'var(--composer-footer-text)'
}

const timerStyle: CSSProperties = {
  minWidth: 32,
  color: 'var(--composer-footer-text)',
  fontSize: 12,
  fontVariantNumeric: 'tabular-nums',
  textAlign: 'right'
}
