import type { ShapeGeometry } from '../../shared/viewer/presentation'
import { child, children, numberAttr, type XmlNode } from './xml'

const RECT_LIKE = new Set(['rect', 'flowChartProcess', 'flowChartAlternateProcess', 'snipRect', 'frame', 'plaque'])
const ELLIPSE_LIKE = new Set(['ellipse', 'flowChartConnector', 'donut', 'pie', 'chord'])

function adjust(geometry: XmlNode, name: string, fallback: number): number {
  const guide = children(child(geometry, 'avLst'), 'gd').find((entry) => entry.attrs.name === name)
  const match = /^val\s+(-?\d+)/.exec(guide?.attrs.fmla ?? '')
  return (match ? Number(match[1]) : fallback) / 100000
}

function presetPoints(prst: string, geometry: XmlNode, width: number, height: number): Array<[number, number]> | undefined {
  const short = Math.min(width, height)
  const across = (fraction: number) => (width > 0 ? (fraction * short) / width : 0)
  switch (prst) {
    case 'triangle': {
      const apex = adjust(geometry, 'adj', 50000)
      return [[apex, 0], [1, 1], [0, 1]]
    }
    case 'rtTriangle':
      return [[0, 0], [1, 1], [0, 1]]
    case 'diamond':
      return [[0.5, 0], [1, 0.5], [0.5, 1], [0, 0.5]]
    case 'parallelogram': {
      const offset = across(adjust(geometry, 'adj', 25000))
      return [[offset, 0], [1, 0], [1 - offset, 1], [0, 1]]
    }
    case 'trapezoid': {
      const offset = across(adjust(geometry, 'adj', 25000))
      return [[offset, 0], [1 - offset, 0], [1, 1], [0, 1]]
    }
    case 'hexagon': {
      const offset = across(adjust(geometry, 'adj', 25000))
      return [[offset, 0], [1 - offset, 0], [1, 0.5], [1 - offset, 1], [offset, 1], [0, 0.5]]
    }
    case 'homePlate': {
      const offset = across(adjust(geometry, 'adj', 50000))
      return [[0, 0], [1 - offset, 0], [1, 0.5], [1 - offset, 1], [0, 1]]
    }
    case 'chevron': {
      const offset = across(adjust(geometry, 'adj', 50000))
      return [[0, 0], [1 - offset, 0], [1, 0.5], [1 - offset, 1], [0, 1], [offset, 0.5]]
    }
    case 'rightArrow':
    case 'leftArrow': {
      const shaft = adjust(geometry, 'adj1', 50000) / 2
      const head = 1 - across(adjust(geometry, 'adj2', 50000))
      const points: Array<[number, number]> = [[0, 0.5 - shaft], [head, 0.5 - shaft], [head, 0], [1, 0.5], [head, 1], [head, 0.5 + shaft], [0, 0.5 + shaft]]
      return prst === 'leftArrow' ? points.map(([x, y]) => [1 - x, y]) : points
    }
    default:
      return undefined
  }
}

const round = (value: number) => Math.round(value * 100) / 100

function customPath(geometry: XmlNode, width: number, height: number): string | undefined {
  const parts: string[] = []
  for (const path of children(child(geometry, 'pathLst'), 'path')) {
    const pathWidth = numberAttr(path, 'w') || width
    const pathHeight = numberAttr(path, 'h') || height
    const sx = pathWidth ? width / pathWidth : 1
    const sy = pathHeight ? height / pathHeight : 1
    let x = 0
    let y = 0
    const point = (node: XmlNode | undefined): [number, number] | undefined => {
      const px = Number(node?.attrs.x)
      const py = Number(node?.attrs.y)
      return Number.isFinite(px) && Number.isFinite(py) ? [px * sx, py * sy] : undefined
    }
    for (const command of path.children) {
      const points = children(command, 'pt').map(point)
      if (points.some((entry) => !entry)) return undefined
      const list = points as Array<[number, number]>
      const last = list[list.length - 1]
      switch (command.name) {
        case 'moveTo':
          parts.push(`M${round(list[0]![0])} ${round(list[0]![1])}`)
          break
        case 'lnTo':
          parts.push(`L${round(list[0]![0])} ${round(list[0]![1])}`)
          break
        case 'cubicBezTo':
          parts.push(`C${list.map(([px, py]) => `${round(px)} ${round(py)}`).join(' ')}`)
          break
        case 'quadBezTo':
          parts.push(`Q${list.map(([px, py]) => `${round(px)} ${round(py)}`).join(' ')}`)
          break
        case 'arcTo': {
          const rx = (numberAttr(command, 'wR') ?? 0) * sx
          const ry = (numberAttr(command, 'hR') ?? 0) * sy
          const start = ((numberAttr(command, 'stAng') ?? 0) / 60000) * (Math.PI / 180)
          const sweep = ((numberAttr(command, 'swAng') ?? 0) / 60000) * (Math.PI / 180)
          const cx = x - rx * Math.cos(start)
          const cy = y - ry * Math.sin(start)
          const ex = cx + rx * Math.cos(start + sweep)
          const ey = cy + ry * Math.sin(start + sweep)
          parts.push(`A${round(rx)} ${round(ry)} 0 ${Math.abs(sweep) > Math.PI ? 1 : 0} ${sweep > 0 ? 1 : 0} ${round(ex)} ${round(ey)}`)
          x = ex
          y = ey
          continue
        }
        case 'close':
          parts.push('Z')
          break
        default:
          return undefined
      }
      if (last) [x, y] = last
    }
  }
  return parts.length ? parts.join(' ') : undefined
}

export function parseGeometry(spPr: XmlNode | undefined, width: number, height: number): ShapeGeometry {
  const preset = child(spPr, 'prstGeom')
  if (preset) {
    const prst = preset.attrs.prst ?? 'rect'
    if (RECT_LIKE.has(prst)) return { kind: 'rect' }
    if (prst === 'roundRect' || prst === 'flowChartTerminator') {
      const fraction = prst === 'flowChartTerminator' ? 0.5 : Math.min(0.5, adjust(preset, 'adj', 16667))
      return { kind: 'roundRect', radius: fraction * Math.min(width, height) }
    }
    if (ELLIPSE_LIKE.has(prst)) return { kind: 'ellipse' }
    if (prst === 'line' || prst.includes('Connector')) return { kind: 'line' }
    const points = presetPoints(prst, preset, width, height)
    return points ? { kind: 'polygon', points } : { kind: 'rect' }
  }
  const custom = child(spPr, 'custGeom')
  const d = custom ? customPath(custom, width, height) : undefined
  return d ? { kind: 'path', d, width, height } : { kind: 'rect' }
}
