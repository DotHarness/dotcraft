import {
  PRESENTATION_MAX_SLIDES,
  PRESENTATION_MAX_TOTAL_IMAGE_BYTES,
  type PresentationSlide,
  type ReadPresentationResult,
  type SlideElement,
  type SlideFill
} from '../../shared/viewer/presentation'
import { collectElements, collectPlaceholders, type Placeholder, type ShapeContext, type TextStyles } from './elements'
import { emu, fillNode, parseFill, type FillContext } from './fill'
import { imageType, PresentationPackage } from './package'
import { emptyLevels, mergeLevels, plainText, withRunDefaults } from './text'
import { parseColorMap, parseTheme, resolveColor, schemeColor, type ColorContext, type Theme } from './theme'
import { child, children, numberAttr, path, type XmlNode } from './xml'

const DEFAULT_WIDTH = 12192000
const DEFAULT_HEIGHT = 6858000

class ImageStore {
  readonly images: Record<string, string> = {}
  private total = 0

  constructor(private readonly pkg: PresentationPackage) {}

  fromRelationship(partPath: string, relationshipId: string | undefined): string | undefined {
    const rel = relationshipId ? this.pkg.relationships(partPath).get(relationshipId) : undefined
    if (!rel || rel.external) return undefined
    if (this.images[rel.target]) return rel.target
    const type = imageType(rel.target)
    const bytes = this.pkg.bytes(rel.target)
    if (!type || !bytes || this.total + bytes.length > PRESENTATION_MAX_TOTAL_IMAGE_BYTES) return undefined
    this.total += bytes.length
    this.images[rel.target] = `data:${type};base64,${Buffer.from(bytes).toString('base64')}`
    return rel.target
  }
}

interface MasterInfo {
  path: string
  theme: Theme
  map: Record<string, string>
  placeholders: Placeholder[]
  styles: TextStyles
  background?: SlideFill | null
  elements: SlideElement[]
}

interface LayoutInfo {
  path: string
  master: MasterInfo
  map: Record<string, string>
  placeholders: Placeholder[]
  showMasterShapes: boolean
  background?: SlideFill | null
  elements: SlideElement[]
}

interface Deck {
  pkg: PresentationPackage
  images: ImageStore
  defaultText: XmlNode | undefined
  masters: Map<string, MasterInfo>
  layouts: Map<string, LayoutInfo>
}

function colorMapOverride(root: XmlNode | undefined, fallback: Record<string, string>): Record<string, string> {
  const override = path(root, 'clrMapOvr', 'overrideClrMapping')
  return override ? parseColorMap(override) : fallback
}

function partContext(deck: Deck, partPath: string, color: ColorContext, rest: Omit<ShapeContext, keyof FillContext | 'pkg' | 'partPath' | 'link'>): ShapeContext {
  return {
    ...color,
    ...rest,
    pkg: deck.pkg,
    partPath,
    image: (id) => deck.images.fromRelationship(partPath, id),
    link: (id) => {
      const rel = id ? deck.pkg.relationships(partPath).get(id) : undefined
      return rel?.external ? rel.target : undefined
    }
  }
}

/** `null` means the part declares a background that draws nothing; `undefined` defers to the parent part. */
function parseBackground(root: XmlNode | undefined, context: FillContext): SlideFill | null | undefined {
  const bg = path(root, 'cSld', 'bg')
  if (!bg) return undefined
  const bgPr = child(bg, 'bgPr')
  if (bgPr) return parseFill(fillNode(bgPr), context) ?? null
  const color = resolveColor(child(bg, 'bgRef'), context)
  return color ? { color } : null
}

function masterInfo(deck: Deck, masterPath: string): MasterInfo {
  const cached = deck.masters.get(masterPath)
  if (cached) return cached
  const root = deck.pkg.root(masterPath)
  const theme = parseTheme(deck.pkg.root(deck.pkg.related(masterPath, '/theme') ?? ''))
  const map = parseColorMap(child(root, 'clrMap'))
  const color: ColorContext = { theme, map }
  const base = withRunDefaults(emptyLevels(), {
    size: 1800,
    ...(schemeColor('tx1', color) ? { color: schemeColor('tx1', color) } : {}),
    ...(theme.minorFont ? { font: theme.minorFont } : {})
  })
  const txStyles = child(root, 'txStyles')
  const styles: TextStyles = {
    title: mergeLevels(base, child(txStyles, 'titleStyle'), color),
    body: mergeLevels(base, child(txStyles, 'bodyStyle'), color),
    other: mergeLevels(base, child(txStyles, 'otherStyle'), color),
    plain: mergeLevels(base, deck.defaultText, color)
  }
  const tree = path(root, 'cSld', 'spTree')
  const info: MasterInfo = { path: masterPath, theme, map, placeholders: collectPlaceholders(tree), styles, elements: [] }
  const context = partContext(deck, masterPath, color, { styles, layout: [], master: [], inherited: true, idPrefix: `m${deck.masters.size}-` })
  info.background = parseBackground(root, context)
  collectElements(tree, context, info.elements)
  deck.masters.set(masterPath, info)
  return info
}

function layoutInfo(deck: Deck, layoutPath: string): LayoutInfo {
  const cached = deck.layouts.get(layoutPath)
  if (cached) return cached
  const root = deck.pkg.root(layoutPath)
  const master = masterInfo(deck, deck.pkg.related(layoutPath, '/slideMaster') ?? '')
  const map = colorMapOverride(root, master.map)
  const tree = path(root, 'cSld', 'spTree')
  const info: LayoutInfo = {
    path: layoutPath,
    master,
    map,
    placeholders: collectPlaceholders(tree),
    showMasterShapes: root?.attrs.showMasterSp !== '0',
    elements: []
  }
  const context = partContext(deck, layoutPath, { theme: master.theme, map }, {
    styles: master.styles,
    layout: [],
    master: [],
    inherited: true,
    idPrefix: `l${deck.layouts.size}-`
  })
  info.background = parseBackground(root, context)
  collectElements(tree, context, info.elements)
  deck.layouts.set(layoutPath, info)
  return info
}

function notesText(deck: Deck, slidePath: string): string {
  const notesPath = deck.pkg.related(slidePath, '/notesSlide')
  const tree = path(deck.pkg.root(notesPath ?? ''), 'cSld', 'spTree')
  const body = tree?.children.find((shape) => path(shape, 'nvSpPr', 'nvPr', 'ph')?.attrs.type === 'body')
  return plainText(child(body, 'txBody')).trim()
}

function buildSlide(deck: Deck, slidePath: string, number: number): PresentationSlide {
  const root = deck.pkg.root(slidePath)
  const hidden = root?.attrs.show === '0'
  try {
    if (!root || root.name !== 'sld') throw new Error('Missing slide part')
    const layout = layoutInfo(deck, deck.pkg.related(slidePath, '/slideLayout') ?? '')
    const master = layout.master
    const color: ColorContext = { theme: master.theme, map: colorMapOverride(root, layout.map) }
    const context = partContext(deck, slidePath, color, {
      styles: master.styles,
      layout: layout.placeholders,
      master: master.placeholders,
      inherited: false,
      idPrefix: ''
    })
    const elements: SlideElement[] = []
    if (root.attrs.showMasterSp !== '0') {
      if (layout.showMasterShapes) elements.push(...master.elements)
      elements.push(...layout.elements)
    }
    collectElements(path(root, 'cSld', 'spTree'), context, elements)
    const own = parseBackground(root, context)
    const background = own !== undefined ? own : layout.background !== undefined ? layout.background : master.background
    return {
      number,
      hidden,
      notes: notesText(deck, slidePath),
      elements,
      ...(background ? { background } : {})
    }
  } catch {
    return { number, hidden, notes: '', elements: [], failed: true }
  }
}

export function buildPresentation(buffer: Uint8Array): ReadPresentationResult {
  const pkg = new PresentationPackage(buffer)
  const presentationPath = pkg.related('', '/officeDocument') ?? 'ppt/presentation.xml'
  const presentation = pkg.root(presentationPath)
  if (!presentation || presentation.name !== 'presentation') throw new Error('Not a presentation')
  const size = child(presentation, 'sldSz')
  const rels = pkg.relationships(presentationPath)
  const slidePaths = children(child(presentation, 'sldIdLst'), 'sldId')
    .map((entry) => rels.get(entry.attrs['r:id'] ?? ''))
    .filter((rel): rel is NonNullable<typeof rel> => Boolean(rel && !rel.external))
    .map((rel) => rel.target)
  const deck: Deck = {
    pkg,
    images: new ImageStore(pkg),
    defaultText: child(presentation, 'defaultTextStyle'),
    masters: new Map(),
    layouts: new Map()
  }
  const slides = slidePaths.slice(0, PRESENTATION_MAX_SLIDES).map((slidePath, index) => buildSlide(deck, slidePath, index + 1))
  return {
    width: emu(numberAttr(size, 'cx') ?? DEFAULT_WIDTH),
    height: emu(numberAttr(size, 'cy') ?? DEFAULT_HEIGHT),
    slides,
    slideCount: slidePaths.length,
    images: deck.images.images
  }
}
