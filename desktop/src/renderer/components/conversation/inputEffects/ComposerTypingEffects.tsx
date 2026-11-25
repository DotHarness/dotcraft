import { useEffect, useRef } from 'react'
import type { ComposerTypingEffect } from '../../../../shared/appearance'
import { prefersReducedMotion } from '../../../utils/appearance'
import { getDocumentThemeMode } from '../../../utils/theme'
import { useVoiceStore } from '../../../voice/voiceStore'
import { colorAt } from './drawComposerParticleRibbon'
import { bloom, drawDots, resolve, sparks, wake, type Box, type Dot } from './typingParticles'
import styles from './ComposerTypingEffects.module.css'

const WORD_END = /[\s.,!?;:，。！？；：、]/
const SENTENCE_END = /[.!?。！？]/

function boxOf(rect: DOMRect | undefined): Box | null {
  return rect && rect.height > 0 ? { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom } : null
}

function caretBox(): Box | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return null
  const range = selection.getRangeAt(0)
  const rect = range.getBoundingClientRect()
  return boxOf(rect.height > 0 ? rect : range.getClientRects()[0])
}

function glyphBox(): Box | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return null
  const { startContainer, startOffset } = selection.getRangeAt(0)
  if (startContainer.nodeType !== Node.TEXT_NODE || startOffset === 0) return null
  const range = document.createRange()
  range.setStart(startContainer, startOffset - 1)
  range.setEnd(startContainer, startOffset)
  return boxOf(range.getBoundingClientRect())
}

function mountTyping(canvas: HTMLCanvasElement, card: HTMLElement, origin: string, effect: ComposerTypingEffect) {
  const context = canvas.getContext('2d')
  const dots: Dot[] = []
  let frame: number | null = null
  let last = 0
  let tempo = 0
  let hueShift = 0
  let wordFrom: Box | null = null

  function render(now: number) {
    frame = null
    if (!context) return
    const width = canvas.clientWidth
    const height = canvas.clientHeight
    const scale = Math.min(window.devicePixelRatio || 1, 2)
    if (canvas.width !== Math.round(width * scale)) canvas.width = Math.round(width * scale)
    if (canvas.height !== Math.round(height * scale)) canvas.height = Math.round(height * scale)
    context.setTransform(scale, 0, 0, scale, 0, 0)
    context.clearRect(0, 0, width, height)
    const theme = getDocumentThemeMode() === 'dark' ? 'dark' : 'light'
    const bounds = canvas.getBoundingClientRect()
    context.globalCompositeOperation = theme === 'dark' ? 'lighter' : 'source-over'
    drawDots(context, dots, now, { x: bounds.left, y: bounds.top }, (hue) => colorAt(hue, theme))
    if (dots.length > 0) frame = window.requestAnimationFrame(render)
  }

  function type(char: string) {
    const now = performance.now()
    const gap = last ? now - last : 400
    last = now
    tempo = tempo * 0.65 + Math.min(1, 140 / Math.max(40, gap)) * 0.35
    hueShift += 0.09
    const bounds = card.getBoundingClientRect()
    const hueAt = (x: number) => {
      const position = ((((x - bounds.left) / bounds.width) * 0.4 + hueShift) % 2 + 2) % 2
      return position > 1 ? 2 - position : position
    }
    const caret = caretBox()
    if (!caret) return
    const wordEnd = WORD_END.test(char)
    if (effect === 'rhythm') {
      dots.push(...wake(now, caret, hueAt, tempo))
      if (!wordEnd && !wordFrom) wordFrom = caret
      if (wordEnd && wordFrom) {
        dots.push(...bloom(now, { ...caret, left: (wordFrom.left + caret.left) / 2 }, hueAt, Math.max(tempo, 0.6)))
        wordFrom = null
      }
      if (SENTENCE_END.test(char)) dots.push(...sparks(now, caret, hueAt, 1, true))
    } else if (effect === 'bloom') {
      dots.push(...bloom(now, caret, hueAt, tempo))
    } else if (effect === 'sparks') {
      dots.push(...sparks(now, caret, hueAt, tempo, wordEnd))
    } else if (effect === 'resolve') {
      const glyph = glyphBox()
      if (glyph) dots.push(...resolve(now, glyph, hueAt))
    } else if (effect === 'wake') {
      dots.push(...wake(now, caret, hueAt, tempo))
    }
    if (frame == null) frame = window.requestAnimationFrame(render)
  }

  function allowed(): boolean {
    return !prefersReducedMotion() && useVoiceStore.getState().recording?.threadId !== origin
  }

  const onInput = (event: Event) => {
    const input = event as InputEvent
    if (input.isComposing || input.inputType !== 'insertText' || !input.data || !allowed()) return
    type(input.data.slice(-1))
  }
  const onCompositionEnd = (event: CompositionEvent) => {
    if (event.data && allowed()) type(event.data.slice(-1))
  }
  card.addEventListener('input', onInput)
  card.addEventListener('compositionend', onCompositionEnd)
  return () => {
    card.removeEventListener('input', onInput)
    card.removeEventListener('compositionend', onCompositionEnd)
    if (frame != null) window.cancelAnimationFrame(frame)
  }
}

export function ComposerTypingEffects({ origin, effect }: { origin: string; effect: ComposerTypingEffect }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const element = canvas.current
    const card = element?.closest<HTMLElement>('[data-composer-card]')
    if (!element || !card) return
    return mountTyping(element, card, origin, effect)
  }, [origin, effect])

  return <canvas ref={canvas} className={styles.layer} aria-hidden />
}
