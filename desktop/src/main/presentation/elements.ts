import { PRESENTATION_MAX_ELEMENTS, type SlideElement, type SlideFrame } from '../../shared/viewer/presentation'
import { emu, fillNode, parseCrop, parseFill, parseLine, parseStyleFill, type FillContext } from './fill'
import { parseGeometry } from './geometry'
import type { PresentationPackage } from './package'
import { parseTable } from './table'
import { fontRefRun, hasText, mergeLevels, parseTextBody, plainText, withRunOverrides, type LevelStyles, type TextContext } from './text'
import { boolAttr, child, numberAttr, path, unwrapAlternate, type XmlNode } from './xml'

export interface Placeholder {
  type: string
  idx?: string
  shape: XmlNode
}

export interface TextStyles {
  title: LevelStyles
  body: LevelStyles
  other: LevelStyles
  plain: LevelStyles
}

export interface ShapeContext extends FillContext, TextContext {
  pkg: PresentationPackage
  partPath: string
  styles: TextStyles
  layout: Placeholder[]
  master: Placeholder[]
  inherited: boolean
  idPrefix: string
}

type Transform = (frame: SlideFrame) => SlideFrame

const TITLE_TYPES = new Set(['title', 'ctrTitle'])
const OTHER_TYPES = new Set(['dt', 'ftr', 'sldNum', 'hdr'])
const TABLE_URI = 'http://schemas.openxmlformats.org/drawingml/2006/table'
const CHART_URI = 'http://schemas.openxmlformats.org/drawingml/2006/chart'

function placeholderOf(shape: XmlNode): { type: string; idx?: string } | undefined {
  const nv = shape.children.find((entry) => entry.name.startsWith('nv'))
  const ph = path(nv, 'nvPr', 'ph')
  if (!ph) return undefined
  return { type: ph.attrs.type ?? 'body', ...(ph.attrs.idx !== undefined ? { idx: ph.attrs.idx } : {}) }
}

export function collectPlaceholders(tree: XmlNode | undefined): Placeholder[] {
  const found: Placeholder[] = []
  for (const shape of tree?.children.flatMap(unwrapAlternate) ?? []) {
    const ph = placeholderOf(shape)
    if (ph) found.push({ ...ph, shape })
  }
  return found
}

const family = (type: string) => (TITLE_TYPES.has(type) ? 'title' : OTHER_TYPES.has(type) ? type : 'body')

function findPlaceholder(list: Placeholder[], ph: { type: string; idx?: string }): Placeholder | undefined {
  if (ph.idx !== undefined) {
    const byIndex = list.find((entry) => entry.idx === ph.idx)
    if (byIndex) return byIndex
  }
  return list.find((entry) => entry.type === ph.type) ?? list.find((entry) => family(entry.type) === family(ph.type))
}

function parseFrame(xfrm: XmlNode | undefined): SlideFrame | undefined {
  const off = child(xfrm, 'off')
  const ext = child(xfrm, 'ext')
  if (!off || !ext) return undefined
  const frame: SlideFrame = { x: emu(numberAttr(off, 'x')), y: emu(numberAttr(off, 'y')), w: emu(numberAttr(ext, 'cx')), h: emu(numberAttr(ext, 'cy')) }
  const rotation = (numberAttr(xfrm, 'rot') ?? 0) / 60000
  if (rotation) frame.rotation = rotation
  if (boolAttr(xfrm, 'flipH')) frame.flipH = true
  if (boolAttr(xfrm, 'flipV')) frame.flipV = true
  return frame
}

function groupTransform(grpSpPr: XmlNode | undefined, outer: Transform): Transform {
  const xfrm = child(grpSpPr, 'xfrm')
  const frame = parseFrame(xfrm)
  const chOff = child(xfrm, 'chOff')
  const chExt = child(xfrm, 'chExt')
  if (!frame || !chOff || !chExt) return outer
  const cx = emu(numberAttr(chOff, 'x'))
  const cy = emu(numberAttr(chOff, 'y'))
  const cw = emu(numberAttr(chExt, 'cx'))
  const ch = emu(numberAttr(chExt, 'cy'))
  const sx = cw ? frame.w / cw : 1
  const sy = ch ? frame.h / ch : 1
  return (inner) =>
    outer({
      ...inner,
      x: frame.x + (inner.x - cx) * sx,
      y: frame.y + (inner.y - cy) * sy,
      w: inner.w * sx,
      h: inner.h * sy,
      ...(inner.rotation || frame.rotation ? { rotation: (inner.rotation ?? 0) + (frame.rotation ?? 0) } : {})
    })
}

function textLevels(context: ShapeContext, ph: { type: string } | undefined, layoutPh?: Placeholder, masterPh?: Placeholder): LevelStyles {
  if (!ph) return context.styles.plain
  const base = TITLE_TYPES.has(ph.type) ? context.styles.title : OTHER_TYPES.has(ph.type) ? context.styles.other : context.styles.body
  const fromMaster = mergeLevels(base, child(child(masterPh?.shape, 'txBody'), 'lstStyle'), context)
  return mergeLevels(fromMaster, child(child(layoutPh?.shape, 'txBody'), 'lstStyle'), context)
}

function shapeElement(node: XmlNode, transform: Transform, context: ShapeContext, base: Omit<SlideElement, 'kind' | 'frame'>): SlideElement | null {
  const ph = placeholderOf(node)
  if (ph && context.inherited) return null
  const layoutPh = ph ? findPlaceholder(context.layout, ph) : undefined
  const masterPh = ph ? findPlaceholder(context.master, layoutPh ?? ph) : undefined
  const spPr = child(node, 'spPr')
  const frame = parseFrame(child(spPr, 'xfrm')) ?? parseFrame(path(layoutPh?.shape, 'spPr', 'xfrm')) ?? parseFrame(path(masterPh?.shape, 'spPr', 'xfrm'))
  if (!frame) return null
  const mapped = transform(frame)
  const style = child(node, 'style')
  const ownFill = parseFill(fillNode(spPr), context)
  const inheritedFill = ownFill === undefined && ph ? parseFill(fillNode(child(layoutPh?.shape, 'spPr')), context) ?? parseFill(fillNode(child(masterPh?.shape, 'spPr')), context) : undefined
  const fill = ownFill === null ? undefined : ownFill ?? inheritedFill ?? parseStyleFill(style, context)
  const line = parseLine(spPr, style, context)
  const levels = withRunOverrides(textLevels(context, ph, layoutPh, masterPh), fontRefRun(style, context))
  const bodyChain = [path(masterPh?.shape, 'txBody', 'bodyPr'), path(layoutPh?.shape, 'txBody', 'bodyPr')]
  const text = parseTextBody(child(node, 'txBody'), levels, bodyChain, context)
  const withText = hasText(text)
  const geometry = parseGeometry(spPr, mapped.w, mapped.h)
  const visible = Boolean(fill || line)
  if (!visible && !withText) return null
  const kind = ph && TITLE_TYPES.has(ph.type) ? 'text' : visible ? 'shape' : 'text'
  return {
    ...base,
    kind,
    frame: mapped,
    ...(ph ? { placeholder: ph.type } : {}),
    ...(visible ? { geometry } : {}),
    ...(fill ? { fill } : {}),
    ...(line ? { line } : {}),
    ...(withText ? { text } : {})
  }
}

function pictureElement(node: XmlNode, transform: Transform, context: ShapeContext, base: Omit<SlideElement, 'kind' | 'frame'>): SlideElement | null {
  const ph = placeholderOf(node)
  if (ph && context.inherited) return null
  const layoutPh = ph ? findPlaceholder(context.layout, ph) : undefined
  const spPr = child(node, 'spPr')
  const frame = parseFrame(child(spPr, 'xfrm')) ?? parseFrame(path(layoutPh?.shape, 'spPr', 'xfrm'))
  if (!frame) return null
  const mapped = transform(frame)
  const blipFill = child(node, 'blipFill')
  const image = context.image(child(blipFill, 'blip')?.attrs['r:embed'])
  const crop = parseCrop(blipFill)
  const line = parseLine(spPr, child(node, 'style'), context)
  const geometry = parseGeometry(spPr, mapped.w, mapped.h)
  return {
    ...base,
    kind: 'picture',
    frame: mapped,
    ...(ph ? { placeholder: ph.type } : {}),
    ...(geometry.kind !== 'rect' ? { geometry } : {}),
    ...(image ? { image } : {}),
    ...(crop ? { crop } : {}),
    ...(line ? { line } : {})
  }
}

function chartTitle(context: ShapeContext, relationshipId: string | undefined): string | undefined {
  const rel = relationshipId ? context.pkg.relationships(context.partPath).get(relationshipId) : undefined
  if (!rel || rel.external) return undefined
  const title = path(context.pkg.root(rel.target), 'chart', 'title')
  const text = plainText(path(title, 'tx', 'rich')).trim()
  return text || undefined
}

function frameElement(node: XmlNode, transform: Transform, context: ShapeContext, base: Omit<SlideElement, 'kind' | 'frame'>): SlideElement | null {
  const frame = parseFrame(child(node, 'xfrm'))
  if (!frame) return null
  const mapped = transform(frame)
  const data = path(node, 'graphic', 'graphicData')
  const uri = data?.attrs.uri
  if (uri === TABLE_URI) {
    const table = parseTable(child(data, 'tbl'), context)
    if (table) return { ...base, kind: 'table', frame: mapped, table }
  }
  if (uri === CHART_URI) {
    const title = chartTitle(context, child(data, 'chart')?.attrs['r:id'])
    return { ...base, kind: 'chart', frame: mapped, ...(title ? { chartTitle: title } : {}) }
  }
  return { ...base, kind: 'unsupported', frame: mapped }
}

export function collectElements(tree: XmlNode | undefined, context: ShapeContext, output: SlideElement[]): void {
  const used = new Set(output.map((element) => element.id))
  const visit = (node: XmlNode, current: Transform) => {
    if (output.length >= PRESENTATION_MAX_ELEMENTS) return
    if (node.name === 'grpSp') {
      const next = groupTransform(child(node, 'grpSpPr'), current)
      for (const entry of node.children.flatMap(unwrapAlternate)) visit(entry, next)
      return
    }
    const build = node.name === 'sp' || node.name === 'cxnSp' ? shapeElement : node.name === 'pic' ? pictureElement : node.name === 'graphicFrame' ? frameElement : null
    if (!build) return
    const cNvPr = node.children.find((entry) => entry.name.startsWith('nv'))?.children.find((entry) => entry.name === 'cNvPr')
    const raw = `${context.idPrefix}${cNvPr?.attrs.id ?? output.length}`
    let id = raw
    for (let suffix = 2; used.has(id); suffix++) id = `${raw}-${suffix}`
    const base = { id, name: cNvPr?.attrs.name ?? '', ...(context.inherited ? { inherited: true } : {}) }
    try {
      const element = build(node, current, context, base)
      if (!element) return
      used.add(id)
      output.push(element)
    } catch {
      const frame = parseFrame(child(child(node, 'spPr'), 'xfrm') ?? child(node, 'xfrm'))
      if (!frame) return
      used.add(id)
      output.push({ ...base, kind: 'unsupported', frame: current(frame) })
    }
  }
  for (const node of tree?.children.flatMap(unwrapAlternate) ?? []) visit(node, (frame) => frame)
}
