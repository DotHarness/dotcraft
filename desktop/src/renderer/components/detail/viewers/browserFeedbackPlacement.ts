const EDITOR_GAP = 25
const EDITOR_MARGIN = 16
export const EDITOR_WIDTH = 294
/* The editor is anchored by its resting single-line box and grows downward from there,
   so a comment that wraps never moves the card the user is typing in. */
export const EDITOR_HEIGHT = 44

interface Rect {
  x: number
  y: number
  width: number
  height: number
}

interface Size {
  width: number
  height: number
}

interface EditorPlacement {
  left: number
  top: number
}

export function placeCommentEditor(anchor: Rect, viewport: Size, editor: Size): EditorPlacement {
  const maxLeft = viewport.width - EDITOR_MARGIN - editor.width
  const maxTop = viewport.height - EDITOR_MARGIN - editor.height
  const clampX = (x: number) => Math.max(EDITOR_MARGIN, Math.min(maxLeft, x))
  const clampY = (y: number) => Math.max(EDITOR_MARGIN, Math.min(maxTop, y))
  const candidates: EditorPlacement[] = [
    { left: anchor.x + anchor.width + EDITOR_GAP, top: clampY(anchor.y) },
    { left: anchor.x - EDITOR_GAP - editor.width, top: clampY(anchor.y) },
    { left: clampX(anchor.x), top: anchor.y + anchor.height + EDITOR_GAP },
    { left: clampX(anchor.x), top: anchor.y - EDITOR_GAP - editor.height }
  ]
  const fits = candidates.find(
    (candidate) =>
      candidate.left >= EDITOR_MARGIN &&
      candidate.left <= maxLeft &&
      candidate.top >= EDITOR_MARGIN &&
      candidate.top <= maxTop
  )
  if (fits) return fits
  return { left: clampX(candidates[0].left), top: clampY(candidates[0].top) }
}
