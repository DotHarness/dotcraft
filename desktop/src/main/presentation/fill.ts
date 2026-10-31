import type { ImageCrop, SlideFill, SlideLine } from '../../shared/viewer/presentation'
import type { ColorContext } from './theme'
import { resolveColor } from './theme'
import { child, children, numberAttr, type XmlNode } from './xml'

export const EMU_PER_PX = 9525
const LINE_WIDTHS = [0, 9525, 12700, 19050]

export const emu = (value: number | undefined) => (value === undefined ? 0 : value / EMU_PER_PX)

export interface FillContext extends ColorContext {
  image: (relationshipId: string | undefined) => string | undefined
}

const FILL_NAMES = new Set(['noFill', 'solidFill', 'gradFill', 'blipFill', 'pattFill', 'grpFill'])

export function fillNode(parent: XmlNode | undefined): XmlNode | undefined {
  return parent?.children.find((entry) => FILL_NAMES.has(entry.name))
}

/** Returns `null` for an explicit `noFill` and `undefined` when the node sets no fill. */
export function parseFill(node: XmlNode | undefined, context: FillContext): SlideFill | null | undefined {
  if (!node) return undefined
  switch (node.name) {
    case 'noFill':
      return null
    case 'solidFill': {
      const color = resolveColor(node, context)
      return color ? { color } : undefined
    }
    case 'gradFill': {
      const stops = children(child(node, 'gsLst'), 'gs')
        .map((stop) => ({ offset: (numberAttr(stop, 'pos') ?? 0) / 100000, color: resolveColor(stop, context) }))
        .filter((stop): stop is { offset: number; color: string } => Boolean(stop.color))
        .sort((left, right) => left.offset - right.offset)
      if (stops.length === 0) return undefined
      if (stops.length === 1) return { color: stops[0]!.color }
      const angle = (numberAttr(child(node, 'lin'), 'ang') ?? 0) / 60000
      return { gradient: { angle, stops } }
    }
    case 'blipFill': {
      const image = context.image(child(node, 'blip')?.attrs['r:embed'])
      return image ? { image } : undefined
    }
    case 'pattFill': {
      const color = resolveColor(child(node, 'fgClr'), context)
      return color ? { color } : undefined
    }
    default:
      return undefined
  }
}

export function parseStyleFill(style: XmlNode | undefined, context: FillContext): SlideFill | undefined {
  const ref = child(style, 'fillRef')
  if (!ref || (numberAttr(ref, 'idx') ?? 0) === 0) return undefined
  const color = resolveColor(ref, context)
  return color ? { color } : undefined
}

export function parseLine(spPr: XmlNode | undefined, style: XmlNode | undefined, context: FillContext): SlideLine | undefined {
  const ln = child(spPr, 'ln')
  const ref = child(style, 'lnRef')
  const refIndex = numberAttr(ref, 'idx') ?? 0
  const fill = fillNode(ln)
  if (fill?.name === 'noFill') return undefined
  const color = fill ? resolveColor(fill, context) : refIndex > 0 ? resolveColor(ref, context) : undefined
  if (!color) return undefined
  const width = numberAttr(ln, 'w') ?? LINE_WIDTHS[Math.min(refIndex, 3)] ?? 9525
  const dash = child(ln, 'prstDash')?.attrs.val
  return { color, width: Math.max(0.5, width / EMU_PER_PX), ...(dash && dash !== 'solid' ? { dash: true } : {}) }
}

export function parseCrop(blipFill: XmlNode | undefined): ImageCrop | undefined {
  const rect = child(blipFill, 'srcRect')
  if (!rect) return undefined
  const side = (name: string) => (numberAttr(rect, name) ?? 0) / 100000
  const crop = { left: side('l'), top: side('t'), right: side('r'), bottom: side('b') }
  if (crop.left + crop.right >= 0.99 || crop.top + crop.bottom >= 0.99) return undefined
  return crop.left || crop.top || crop.right || crop.bottom ? crop : undefined
}
