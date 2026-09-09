import { useCallback, useEffect, useRef, useState } from 'react'
import type { AvatarPose } from '../index.js'
import type { MascotExpression } from './types.js'

export type ComposerAvatarGesture = 'blink' | 'look-left' | 'look-right' | 'antenna-bob'

interface ComposerAvatarBehaviorOptions {
  semanticPose: AvatarPose
  baseExpression: MascotExpression
  focused: boolean
  dragOver: boolean
  sleeping: boolean
  waving: boolean
  activeIdle: boolean
  bounceSignal: number
  reducedMotion: boolean
  directionalGestures?: boolean
}

export function useComposerAvatarBehavior(options: ComposerAvatarBehaviorOptions): {
  pose: AvatarPose
  expression: 'base' | 'happy' | 'operator' | 'sleep' | undefined
  gesture: ComposerAvatarGesture | undefined
  gestureSequence: number
  clearGesture: () => void
  completeGesture: (sequence: number) => void
} {
  const [receiptAcknowledging, setReceiptAcknowledging] = useState(false)
  const [gesture, setGesture] = useState<ComposerAvatarGesture>()
  const [gestureSequence, setGestureSequence] = useState(0)
  const previousBounce = useRef(options.bounceSignal)

  useEffect(() => {
    if (options.reducedMotion || options.semanticPose !== 'idle') {
      previousBounce.current = options.bounceSignal
      setReceiptAcknowledging(false)
      return
    }
    if (options.bounceSignal === previousBounce.current) return
    previousBounce.current = options.bounceSignal
    setReceiptAcknowledging(true)
    const timer = window.setTimeout(() => setReceiptAcknowledging(false), 350)
    return () => window.clearTimeout(timer)
  }, [options.bounceSignal, options.reducedMotion, options.semanticPose])

  useEffect(() => {
    if (options.sleeping || options.activeIdle || options.reducedMotion) {
      setGesture(undefined)
      return
    }
    if (options.baseExpression !== 'neutral' && options.baseExpression !== 'happy') return
    let timer = 0
    const schedule = (): void => {
      timer = window.setTimeout(() => {
        if (!document.hidden) {
          const value = Math.random()
          const next = value < 0.5
            ? 'blink'
            : value < 0.72
              ? (Math.random() < 0.5 ? 'look-left' : 'look-right')
              : value < 0.86
                ? 'antenna-bob'
                : undefined
          if (next && (options.directionalGestures !== false || (next !== 'look-left' && next !== 'look-right'))) {
            setGesture(next)
            setGestureSequence((sequence) => sequence + 1)
          }
        }
        schedule()
      }, 2600 + Math.random() * 3200)
    }
    schedule()
    return () => { window.clearTimeout(timer) }
  }, [options.sleeping, options.activeIdle, options.reducedMotion, options.baseExpression, options.directionalGestures])

  const clearGesture = useCallback(() => setGesture(undefined), [])
  const completeGesture = useCallback((sequence: number) => {
    setGesture((current) => sequence === gestureSequence ? undefined : current)
  }, [gestureSequence])

  const expression = options.semanticPose === 'idle'
    ? options.waving || options.focused
      ? 'happy'
      : options.dragOver || options.baseExpression === 'operator'
        ? 'operator'
        : options.sleeping
          ? 'sleep'
          : 'base'
    : undefined
  return {
    pose: options.semanticPose === 'idle' && receiptAcknowledging ? 'acknowledge' : options.semanticPose,
    expression,
    gesture,
    gestureSequence,
    clearGesture,
    completeGesture
  }
}
