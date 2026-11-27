import { useResponseSelectionStore } from './responseSelectionStore'

export const REPLY_SELECTION_TARGET_ATTR = 'data-reply-selection-target'
export const REPLY_SELECTION_OVERLAY_ATTR = 'data-response-selection-overlay'

const TARGET_SELECTOR = `[${REPLY_SELECTION_TARGET_ATTR}]`
const OVERLAY_SELECTOR = `[${REPLY_SELECTION_OVERLAY_ATTR}]`
const COMPOSER_SELECTOR = '[data-composer-root]'
const OVERFLOW_CLIP_VALUES = new Set(['auto', 'clip', 'hidden', 'overlay', 'scroll'])

export interface ResolvedReplySelection {
  target: HTMLElement
  element: HTMLElement
  range: Range
  text: string
}

interface Bounds {
  left: number
  top: number
  right: number
  bottom: number
}

const replies = new Map<HTMLElement, (selection: ResolvedReplySelection) => void>()
let stopWatching: (() => void) | undefined

function readSelectionRange(): Range | undefined {
  const selection = window.getSelection()
  if (selection && selection.rangeCount > 0 && !selection.isCollapsed) return selection.getRangeAt(0)
}

function sameSelectionRange(a: Range, b: Range): boolean {
  return a.startContainer === b.startContainer && a.startOffset === b.startOffset &&
    a.endContainer === b.endContainer && a.endOffset === b.endOffset
}

function toElement(node: Node | null | undefined): Element | null {
  return node instanceof Element ? node : node?.parentElement ?? null
}

function resolveEndpointTarget(node: Node | null, offset: number): { element: HTMLElement; target: HTMLElement } | null {
  const direct = toElement(node)
  const directTarget = direct?.closest(TARGET_SELECTOR)
  if (direct instanceof HTMLElement && directTarget instanceof HTMLElement) {
    return { element: direct, target: directTarget }
  }
  if (!(node instanceof Element)) return null
  for (const child of [node.childNodes[offset], node.childNodes[offset - 1]]) {
    const childEl = toElement(child)
    const childTarget = childEl?.closest(TARGET_SELECTOR)
    if (childEl instanceof HTMLElement && childTarget instanceof HTMLElement) {
      return { element: childEl, target: childTarget }
    }
  }
  return null
}

function clampRangeToTarget(range: Range, target: HTMLElement): Range {
  const full = target.ownerDocument.createRange()
  full.selectNodeContents(target)
  const clamped = range.cloneRange()
  if (!target.contains(clamped.startContainer) || clamped.compareBoundaryPoints(Range.START_TO_START, full) < 0) {
    clamped.setStart(full.startContainer, full.startOffset)
  }
  if (!target.contains(clamped.endContainer) || clamped.compareBoundaryPoints(Range.END_TO_END, full) > 0) {
    clamped.setEnd(full.endContainer, full.endOffset)
  }
  return clamped
}

function resolveReplySelection(): ResolvedReplySelection | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null

  const anchor = resolveEndpointTarget(selection.anchorNode, selection.anchorOffset)
  const focus = resolveEndpointTarget(selection.focusNode, selection.focusOffset)
  if (anchor && focus && anchor.target !== focus.target) return null

  const picked = anchor ?? focus
  if (!picked || picked.element.closest('[contenteditable="true"]')) return null

  const range = clampRangeToTarget(selection.getRangeAt(0), picked.target)
  const text = range.toString().trim()
  if (!text) return null
  return { target: picked.target, element: picked.element, range, text }
}

function intersectRect(a: DOMRect | Bounds, b: Bounds): DOMRect | null {
  const left = Math.max(a.left, b.left)
  const top = Math.max(a.top, b.top)
  const right = Math.min(a.right, b.right)
  const bottom = Math.min(a.bottom, b.bottom)
  return left >= right || top >= bottom ? null : new DOMRect(left, top, right - left, bottom - top)
}

function firstVisibleRect(rects: readonly DOMRect[], bounds: Bounds): DOMRect | null {
  for (const rect of rects) {
    const visible = intersectRect(rect, bounds)
    if (visible) return visible
  }
  return null
}

function hasNonZeroSize(rect: DOMRect): boolean {
  return rect.width > 0 && rect.height > 0
}

function findBottomBoundaryTop(element: HTMLElement): number | undefined {
  let ancestor: HTMLElement | null = element.parentElement
  while (ancestor && ancestor !== document.body && ancestor !== document.documentElement) {
    const composer = ancestor.querySelector(COMPOSER_SELECTOR)
    if (composer instanceof HTMLElement) {
      const top = composer.getBoundingClientRect().top
      if (top > 0) return top
      return undefined
    }
    ancestor = ancestor.parentElement
  }
  return undefined
}

function getVisibleSelectionBounds(element: HTMLElement): Bounds | null {
  const bottomBoundaryTop = findBottomBoundaryTop(element)
  let bounds: Bounds = {
    left: 0,
    top: 0,
    right: window.innerWidth,
    bottom: bottomBoundaryTop == null ? window.innerHeight : Math.min(window.innerHeight, bottomBoundaryTop),
  }
  let current: HTMLElement | null = element
  while (current && current !== document.body && current !== document.documentElement) {
    const style = window.getComputedStyle(current)
    const clipsX = (style.overflowX || style.overflow).split(/\s+/).some((value) => OVERFLOW_CLIP_VALUES.has(value))
    const clipsY = (style.overflowY || style.overflow).split(/\s+/).some((value) => OVERFLOW_CLIP_VALUES.has(value))
    if (clipsX || clipsY) {
      const rect = current.getBoundingClientRect()
      if (hasNonZeroSize(rect)) {
        bounds = {
          left: clipsX ? Math.max(bounds.left, rect.left) : bounds.left,
          top: clipsY ? Math.max(bounds.top, rect.top) : bounds.top,
          right: clipsX ? Math.min(bounds.right, rect.right) : bounds.right,
          bottom: clipsY ? Math.min(bounds.bottom, rect.bottom) : bounds.bottom,
        }
        if (bounds.right <= bounds.left || bounds.bottom <= bounds.top) return null
      }
    }
    current = current.parentElement
  }
  return bounds.bottom <= bounds.top ? null : bounds
}

export function getSelectionAnchorRect(range: Range, element: HTMLElement, target: HTMLElement): DOMRect | null {
  const bounds = getVisibleSelectionBounds(element)
  if (!bounds) return null

  const clientRects = Array.from(range.getClientRects())
  const elementRect = element.getBoundingClientRect()
  const hasElementRect = hasNonZeroSize(elementRect)
  const visibleClient = firstVisibleRect(clientRects, bounds)
  if (visibleClient && (clientRects.length > 1 || !hasElementRect || intersectRect(visibleClient, elementRect) != null)) {
    return visibleClient
  }

  const bounding = range.getBoundingClientRect()
  const visibleBounding = intersectRect(bounding, bounds)
  if (visibleBounding && (!hasElementRect || intersectRect(visibleBounding, elementRect) != null)) {
    return visibleBounding
  }

  if (clientRects.some(hasNonZeroSize) || hasNonZeroSize(bounding)) return null

  return firstVisibleRect(Array.from(element.getClientRects()), bounds) ??
    intersectRect(elementRect, bounds) ??
    firstVisibleRect(Array.from(target.getClientRects()), bounds) ??
    intersectRect(target.getBoundingClientRect(), bounds)
}

export function registerReplySelection(
  container: HTMLElement,
  capture: (selection: ResolvedReplySelection) => void,
): () => void {
  replies.set(container, capture)
  stopWatching ??= watchReplySelection()
  return () => {
    replies.delete(container)
    if (replies.size === 0) {
      stopWatching?.()
      stopWatching = undefined
    }
  }
}

function isMacCtrlClick(event: PointerEvent): boolean {
  return event.button === 0 && event.ctrlKey && /mac/i.test(navigator.platform)
}

function watchReplySelection(): () => void {
  const lifetime = new AbortController()
  const captureOptions = { capture: true, signal: lifetime.signal }
  const signalOptions = { signal: lifetime.signal }
  let pointerDown = false
  let previousRange: Range | undefined
  let frame: number | undefined

  function cancelFrame(): void {
    if (frame !== undefined) window.cancelAnimationFrame(frame)
    frame = undefined
  }

  function evaluate(checkUnchanged: boolean): void {
    cancelFrame()
    const snapshotPrevious = checkUnchanged ? previousRange : undefined
    previousRange = undefined
    frame = window.requestAnimationFrame(() => {
      frame = undefined
      const state = useResponseSelectionStore.getState()
      if (state.active?.editing) return

      const rawRange = readSelectionRange()
      if (!rawRange) {
        if (state.active) state.dismiss()
        return
      }
      if (snapshotPrevious && sameSelectionRange(snapshotPrevious, rawRange)) return

      const resolved = resolveReplySelection()
      if (!resolved) {
        if (state.active) state.dismiss()
        return
      }
      if (
        state.active &&
        state.active.container === resolved.target &&
        state.active.text === resolved.text &&
        sameSelectionRange(state.active.range, resolved.range)
      ) {
        return
      }
      const capture = replies.get(resolved.target)
      if (capture) capture(resolved)
      else if (state.active) state.dismiss()
    })
  }

  window.addEventListener('pointerdown', (event) => {
    if (event.button === 2 || isMacCtrlClick(event)) return
    const target = event.target instanceof Element ? event.target : null
    if (target?.closest(OVERLAY_SELECTOR)) return
    pointerDown = true
    previousRange = event.isPrimary && event.button === 0 ? readSelectionRange()?.cloneRange() : undefined
    cancelFrame()
  }, captureOptions)

  window.addEventListener('pointerup', (event) => {
    if (!pointerDown) return
    pointerDown = false
    evaluate(event.button === 0)
  }, captureOptions)

  window.addEventListener('pointercancel', () => {
    if (!pointerDown) return
    pointerDown = false
    previousRange = undefined
    evaluate(false)
  }, captureOptions)

  document.addEventListener('selectionchange', () => {
    if (pointerDown) {
      cancelFrame()
      return
    }
    evaluate(false)
  }, signalOptions)

  window.addEventListener('dblclick', () => {
    if (!pointerDown) evaluate(false)
  }, signalOptions)

  window.addEventListener('keyup', (event) => {
    if (event.key === 'Escape') {
      cancelFrame()
      return
    }
    if (!pointerDown) evaluate(false)
  }, signalOptions)

  window.addEventListener('contextmenu', () => {
    pointerDown = false
    previousRange = undefined
    cancelFrame()
  }, captureOptions)

  window.addEventListener('blur', () => {
    pointerDown = false
    previousRange = undefined
    cancelFrame()
  }, signalOptions)

  return () => {
    lifetime.abort()
    cancelFrame()
  }
}
