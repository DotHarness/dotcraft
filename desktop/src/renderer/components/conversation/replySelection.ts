const replies = new Map<Node, (range: Range) => void>()
let stopWatching: (() => void) | undefined

export function readSelectionRange(): Range | undefined {
  const selection = window.getSelection()
  if (selection?.rangeCount === 1 && !selection.isCollapsed) return selection.getRangeAt(0)
}

export function sameSelectionRange(a: Range, b: Range): boolean {
  return a.startContainer === b.startContainer && a.startOffset === b.startOffset &&
    a.endContainer === b.endContainer && a.endOffset === b.endOffset
}

export function registerReplySelection(container: HTMLElement, capture: (range: Range) => void): () => void {
  replies.set(container, capture)
  stopWatching ??= watchPointerSelection()
  return () => {
    replies.delete(container)
    if (replies.size === 0) {
      stopWatching?.()
      stopWatching = undefined
    }
  }
}

function watchPointerSelection(): () => void {
  const lifetime = new AbortController()
  const options = { capture: true, signal: lifetime.signal }
  let gesture: { pointerId: number; previous: Range | undefined } | undefined
  let frame: number | undefined

  function reset(): void {
    gesture = undefined
    if (frame !== undefined) window.cancelAnimationFrame(frame)
    frame = undefined
  }

  window.addEventListener('pointerdown', (event) => {
    reset()
    if (event.isPrimary && event.button === 0) {
      gesture = { pointerId: event.pointerId, previous: readSelectionRange()?.cloneRange() }
    }
  }, options)

  window.addEventListener('pointerup', (event) => {
    if (!gesture || gesture.pointerId !== event.pointerId || event.button !== 0) return
    const { previous } = gesture
    gesture = undefined
    frame = window.requestAnimationFrame(() => {
      frame = undefined
      const range = readSelectionRange()
      if (!range || (previous && sameSelectionRange(previous, range))) return
      for (let node: Node | null = range.commonAncestorContainer; node; node = node.parentNode) {
        const capture = replies.get(node)
        if (capture) return capture(range)
      }
    })
  }, options)

  window.addEventListener('pointercancel', reset, options)
  window.addEventListener('dragstart', reset, options)
  window.addEventListener('contextmenu', reset, options)
  window.addEventListener('blur', reset, { signal: lifetime.signal })
  window.addEventListener('keydown', (event) => { if (event.key === 'Escape') reset() }, options)
  return () => {
    lifetime.abort()
    reset()
  }
}
