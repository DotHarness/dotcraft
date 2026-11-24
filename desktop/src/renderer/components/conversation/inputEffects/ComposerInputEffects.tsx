import { createContext, useEffect, useRef, type RefObject } from 'react'
import { getDocumentThemeMode } from '../../../utils/theme'
import { isVoiceProcessingForThread, useVoiceStore } from '../../../voice/voiceStore'
import {
  drawRibbon, drawRibbonExit, typingWaves, TYPING_MS, VISIBLE_ENERGY, type Keystroke, type RibbonExitPoint
} from './drawComposerParticleRibbon'
import styles from './ComposerParticleRibbon.module.css'

export const ComposerTypingFeedbackContext = createContext<(() => void) | null>(null)

interface ComposerInputEffectsProps {
  origin: string
  submitRevision: number
  exitHandoffAt?: number
  typingPulse: RefObject<(() => void) | null>
}

const RIBBON_EXIT_MS = 700
type ExitSlot = 'primary' | 'voice'

function findExitTarget(canvas: HTMLCanvasElement, slot: ExitSlot, width: number, height: number): RibbonExitPoint {
  const card = canvas.closest('[data-composer-card]')
  const fallback = { x: width * 0.95, y: height - 4 }
  if (!card) return fallback
  const bounds = canvas.getBoundingClientRect()
  if (bounds.width <= 0 || bounds.height <= 0) return fallback

  for (const button of card.querySelectorAll<HTMLButtonElement>(`button[data-composer-exit-target="${slot}"]`)) {
    if (button.closest('[data-composer-card]') !== card) continue
    const target = button.getBoundingClientRect()
    if (target.width <= 0 || target.height <= 0) continue
    const x = (target.left + target.width / 2 - bounds.left) * width / bounds.width
    const y = (target.top + target.height / 2 - bounds.top) * height / bounds.height
    if (x >= 0 && x <= width && y >= 0 && y <= height) return { x, y }
  }
  return fallback
}

function mountRibbon(canvas: HTMLCanvasElement, origin: string) {
  const motionQuery = window.matchMedia?.('(prefers-reduced-motion: reduce)')
  let frame: number | null = null
  let clearTimer: number | undefined
  let context: CanvasRenderingContext2D | null | undefined
  let previousFrame = 0
  let strokes: Keystroke[] = []
  let direction = -1
  let exit: { slot: ExitSlot; startedAt?: number; energy: number; time: number } | undefined
  let resumedHandoffAt: number | undefined
  const voice = { energy: 0, time: 0 }

  function isReduced() {
    const choice = document.documentElement.dataset.reduceMotion
    return choice === 'on' || (choice !== 'off' && motionQuery?.matches === true)
  }

  function requestFrame() {
    if (frame == null) frame = window.requestAnimationFrame(render)
  }

  function sweepExit(slot: ExitSlot, startedAt?: number) {
    strokes = []
    exit = isReduced() ? undefined : {
      slot,
      startedAt,
      energy: Math.max(voice.energy, 0.57),
      time: voice.time
    }
    voice.energy = 0
    voice.time = 0
    requestFrame()
  }

  function render(now: number) {
    frame = null
    const { recording } = useVoiceStore.getState()
    const microphone = recording?.threadId === origin ? recording : null
    const reduced = isReduced()
    const dt = Math.max(0, Math.min(48, previousFrame ? now - previousFrame : 16))
    previousFrame = now
    const level = microphone ? (reduced ? 0.55 : Math.max(0, Math.min(1, microphone.level))) : 0
    const target = microphone ? 0.08 + 0.92 * level ** 0.34 : 0
    const smoothing = target > voice.energy ? 49 : microphone ? 188 : 78
    voice.energy = reduced ? target : voice.energy + (target - voice.energy) * (1 - Math.exp(-dt / smoothing))
    if (reduced || (!microphone && voice.energy <= VISIBLE_ENERGY)) voice.time = 0
    else if (microphone) voice.time += dt / 1000

    strokes = strokes.filter(({ at }) => now - at < TYPING_MS)
    // A delayed frame must not consume the exit before it can be drawn.
    const exitProgress = exit ? (now - (exit.startedAt ??= now)) / RIBBON_EXIT_MS : 0
    if (reduced || exitProgress >= 1) exit = undefined

    const width = canvas.clientWidth
    const height = canvas.clientHeight
    if (width > 0 && height > 0) {
      if (context === undefined) context = canvas.getContext('2d')
      if (context) {
        const scale = Math.min(window.devicePixelRatio || 1, 2)
        const pixelWidth = Math.round(width * scale)
        const pixelHeight = Math.round(height * scale)
        if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
          canvas.width = pixelWidth
          canvas.height = pixelHeight
        }
        context.setTransform(scale, 0, 0, scale, 0, 0)
        context.clearRect(0, 0, width, height)
        const theme = getDocumentThemeMode() === 'dark' ? 'dark' : 'light'
        if (exit) {
          const target = findExitTarget(canvas, exit.slot, width, height)
          drawRibbonExit(context, width, height, exitProgress, exit, target, theme)
        } else {
          drawRibbon(context, width, height, voice, typingWaves(reduced ? strokes.slice(-1) : strokes, now, reduced), theme)
        }
      }
    }

    window.clearTimeout(clearTimer)
    if (!reduced && (microphone || voice.energy > VISIBLE_ENERGY || exit || strokes.length > 0)) {
      requestFrame()
    } else if (reduced && strokes.length > 0) {
      const last = strokes[strokes.length - 1]
      clearTimer = window.setTimeout(requestFrame, Math.max(1, last.at + TYPING_MS - now + 1))
    } else previousFrame = 0
  }

  function hasActiveVoice() {
    const { recording, snapshot, finalizing } = useVoiceStore.getState()
    return recording?.threadId === origin || isVoiceProcessingForThread(snapshot, finalizing?.threadId, origin)
  }

  const unsubscribe = useVoiceStore.subscribe((next, previous) => {
    const wasRecording = previous.recording?.threadId === origin
    const recording = next.recording?.threadId === origin
    if (recording === wasRecording) return
    if (recording) {
      strokes = []
      exit = undefined
    } else if (next.finalizing?.threadId === origin && previous.finalizing !== next.finalizing) {
      sweepExit(next.finalizing.intent === 'send' ? 'primary' : 'voice')
    }
    requestFrame()
  })

  const wakeForLayout = () => {
    if (context || frame != null || useVoiceStore.getState().recording?.threadId === origin) requestFrame()
  }
  const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(wakeForLayout)
  resize?.observe(canvas)
  const appearance = new MutationObserver(wakeForLayout)
  appearance.observe(document.documentElement, { attributeFilter: ['data-theme', 'data-reduce-motion'] })
  motionQuery?.addEventListener('change', wakeForLayout)
  wakeForLayout()

  return {
    type() {
      if (hasActiveVoice()) return
      direction = -direction
      strokes = [...strokes.slice(-4), { at: performance.now(), direction }]
      requestFrame()
    },
    submit() {
      if (!hasActiveVoice()) sweepExit('primary')
    },
    resumeHandoff(timestamp: number) {
      const elapsed = Date.now() - timestamp
      if (!(elapsed >= 0 && elapsed < RIBBON_EXIT_MS) || timestamp === resumedHandoffAt || hasActiveVoice()) return
      resumedHandoffAt = timestamp
      sweepExit('primary', performance.now() - elapsed)
    },
    dispose() {
      unsubscribe()
      resize?.disconnect()
      appearance.disconnect()
      motionQuery?.removeEventListener('change', wakeForLayout)
      if (frame != null) window.cancelAnimationFrame(frame)
      window.clearTimeout(clearTimer)
    }
  }
}

export function ComposerInputEffects({ origin, submitRevision, exitHandoffAt, typingPulse }: ComposerInputEffectsProps) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const engine = useRef<ReturnType<typeof mountRibbon> | null>(null)
  const previousSubmit = useRef(submitRevision)
  useEffect(() => {
    if (!canvas.current) return
    const ribbon = mountRibbon(canvas.current, origin)
    engine.current = ribbon
    typingPulse.current = ribbon.type
    return () => {
      engine.current = null
      typingPulse.current = null
      ribbon.dispose()
    }
  }, [origin, typingPulse])

  useEffect(() => {
    if (previousSubmit.current !== submitRevision) engine.current?.submit()
    previousSubmit.current = submitRevision
  }, [submitRevision])

  useEffect(() => {
    if (exitHandoffAt !== undefined) engine.current?.resumeHandoff(exitHandoffAt)
  }, [exitHandoffAt, origin])

  return <canvas ref={canvas} className={styles.ribbon} aria-hidden />
}
