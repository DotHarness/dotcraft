export type PetPillAnchor = 'below' | 'above'

export interface PetPillPlacement {
  left: number
  top: number
  width: number
  maxHeight: number
  anchor: PetPillAnchor
}

const GAP = 12
const MARGIN = 12
const MAX_WIDTH = 315
const MIN_WIDTH = 120
const MIN_HEIGHT = 80
/** Extra room required before a pill that flipped above the pet moves back below it. */
const FLIP_BACK = 24

export function placePetPill(input: {
  position: { x: number; y: number; size: number }
  viewport: { width: number; height: number }
  height: number
  previous?: PetPillAnchor
}): PetPillPlacement {
  const { position, viewport, height, previous } = input
  const width = Math.max(MIN_WIDTH, Math.min(MAX_WIDTH, viewport.width - 2 * MARGIN))
  const roomBelow = viewport.height - (position.y + position.size) - GAP - MARGIN
  const roomAbove = position.y - GAP - MARGIN
  const anchor: PetPillAnchor = previous === 'above'
    ? (roomBelow >= height + FLIP_BACK || roomAbove < height ? 'below' : 'above')
    : (height > roomBelow && roomAbove >= height ? 'above' : 'below')
  const top = anchor === 'below' ? position.y + position.size + GAP : Math.max(MARGIN, position.y - GAP - height)
  const maxHeight = Math.max(MIN_HEIGHT, anchor === 'below' ? roomBelow : roomAbove)
  const left = Math.max(MARGIN, Math.min(position.x + position.size / 2 - width / 2, viewport.width - width - MARGIN))
  return { left, top, width, maxHeight, anchor }
}
