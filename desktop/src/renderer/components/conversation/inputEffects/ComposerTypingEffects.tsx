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

function committedGlyphs(text: string): Box[] | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return null
  const { startContainer, startOffset } = selection.getRangeAt(0)
  if (startContainer.nodeType !== Node.TEXT_NODE) return null
  const from = startOffset - text.length
  if (from < 0 || (startContainer as Text).data.slice(from, startOffset) !== text) return null
  const boxes: Box[] = []
  const range = document.createRange()
  let offset = from
  for (const char of Array.from(text)) {
    range.setStart(startContainer, offset)
    range.setEnd(startContainer, offset + char.length)
    offset += char.length
    const box = boxOf(range.getBoundingClientRect())
    if (box) boxes.push(box)
  }
  return boxes.length === Array.from(text).length ? boxes : null
}

const COMMIT_LIMIT = 16
const COMMIT_STAGGER = 34
const COMPOSING_PEAK = 0.45

function mountTyping(canvas: HTMLCanvasElement, card: HTMLElement, origin: string, effect: ComposerTypingEffect) {
  const context = canvas.getContext('2d')
  const dots: Dot[] = []
  let frame: number | null = null
  let last = 0
  let tempo = 0
  let hueShift = 0
  let wordFrom: Box | null = null
  let composing = ''
  let disposed = false

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

  function beat(drift: number) {
    const now = performance.now()
    const gap = last ? now - last : 400
    last = now
    tempo = tempo * 0.65 + Math.min(1, 140 / Math.max(40, gap)) * 0.35
    hueShift += drift
  }

  function hueAt(): (x: number) => number {
    const bounds = card.getBoundingClientRect()
    const shift = hueShift
    return (x: number) => {
      const position = ((((x - bounds.left) / bounds.width) * 0.4 + shift) % 2 + 2) % 2
      return position > 1 ? 2 - position : position
    }
  }

  function spawn(fresh: Dot[], delay = 0, peak = 1, thin = false) {
    fresh.forEach((item, index) => {
      if (thin && index % 2 === 1) return
      item.delay += delay
      item.peak *= peak
      dots.push(item)
    })
    if (frame == null) frame = window.requestAnimationFrame(render)
  }

  /** The per-character effect, without Rhythm's word bloom. */
  function glyphEffect(char: string, caret: Box, glyph: Box | null): Dot[] {
    const now = performance.now()
    const hue = hueAt()
    const wordEnd = WORD_END.test(char)
    if (effect === 'rhythm') {
      const out = wake(now, caret, hue, tempo)
      if (SENTENCE_END.test(char)) out.push(...sparks(now, caret, hue, 1, true))
      return out
    }
    if (effect === 'bloom') return bloom(now, caret, hue, tempo)
    if (effect === 'sparks') return sparks(now, caret, hue, tempo, wordEnd)
    if (effect === 'resolve') return glyph ? resolve(now, glyph, hue) : []
    return wake(now, caret, hue, tempo)
  }

  function type(char: string) {
    beat(0.09)
    const caret = caretBox()
    if (!caret) return
    spawn(glyphEffect(char, caret, effect === 'resolve' ? glyphBox() : null))
    if (effect !== 'rhythm') return
    const wordEnd = WORD_END.test(char)
    if (!wordEnd && !wordFrom) wordFrom = caret
    if (wordEnd && wordFrom) {
      spawn(bloom(performance.now(), { ...caret, left: (wordFrom.left + caret.left) / 2 }, hueAt(), Math.max(tempo, 0.6)))
      wordFrom = null
    }
  }

  function compose(text: string) {
    const grew = text.length > composing.length
    composing = text
    if (!grew) return
    beat(0.05)
    const caret = caretBox()
    if (!caret) return
    spawn(glyphEffect(text.slice(-1), caret, effect === 'resolve' ? glyphBox() : null), 0, COMPOSING_PEAK, true)
  }

  function commit(text: string) {
    beat(0.09)
    const chars = Array.from(text)
    const glyphs = committedGlyphs(text)
    if (!glyphs) {
      type(chars[chars.length - 1] ?? '')
      return
    }
    const skip = Math.max(0, chars.length - COMMIT_LIMIT)
    for (let index = skip; index < chars.length; index++) {
      const glyph = glyphs[index]
      const caret = { ...glyph, left: glyph.right }
      hueShift += 0.03
      spawn(glyphEffect(chars[index], caret, glyph), (index - skip) * COMMIT_STAGGER)
    }
    if (effect !== 'rhythm') return
    const words = chars.filter((char) => !WORD_END.test(char)).length
    const first = glyphs[skip]
    const end = glyphs[glyphs.length - 1]
    if (words > 0 || wordFrom) {
      const left = ((wordFrom ?? first).left + end.right) / 2
      const delay = (chars.length - skip) * COMMIT_STAGGER
      spawn(bloom(performance.now(), { ...end, left }, hueAt(), Math.max(tempo, 0.6)), delay)
      wordFrom = null
    }
  }

  function allowed(): boolean {
    return !prefersReducedMotion() && useVoiceStore.getState().recording?.threadId !== origin
  }

  const onInput = (event: Event) => {
    const input = event as InputEvent
    if (!allowed()) return
    if (input.inputType === 'insertCompositionText') {
      compose(input.data ?? '')
      return
    }
    if (input.isComposing || input.inputType !== 'insertText' || !input.data) return
    type(input.data.slice(-1))
  }
  const onCompositionStart = () => {
    composing = ''
  }
  const onCompositionEnd = (event: CompositionEvent) => {
    composing = ''
    const text = event.data
    if (!text || !allowed()) return
    // Let the editor settle the committed text and caret before measuring glyphs.
    window.requestAnimationFrame(() => {
      if (!disposed) commit(text)
    })
  }
  card.addEventListener('input', onInput)
  card.addEventListener('compositionstart', onCompositionStart)
  card.addEventListener('compositionend', onCompositionEnd)
  return () => {
    disposed = true
    card.removeEventListener('input', onInput)
    card.removeEventListener('compositionstart', onCompositionStart)
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
