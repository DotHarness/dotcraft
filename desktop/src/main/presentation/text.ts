import type { TextBody, TextBullet, TextParagraph, TextRun } from '../../shared/viewer/presentation'
import { EMU_PER_PX } from './fill'
import { resolveColor, themeFont, type ColorContext } from './theme'
import { child, children, numberAttr, unwrapAlternate, type XmlNode } from './xml'

const MAX_AUTO_NUMBER = 32767

const PX_PER_POINT = 96 / 72
const LEVELS = 9
const LINK_SCHEMES = /^(https?:|mailto:)/i

interface RunStyle {
  size?: number
  bold?: boolean
  italic?: boolean
  underline?: boolean
  strike?: boolean
  color?: string
  font?: string
  baseline?: number
}

type BulletKind = { kind: 'none' } | { kind: 'char'; char: string } | { kind: 'auto'; scheme: string; startAt: number }

interface ParagraphStyle {
  align?: TextParagraph['align']
  marL?: number
  indent?: number
  bullet?: BulletKind
  bulletColor?: string
  bulletScale?: number
  bulletFont?: string
  lineSpacingPct?: number
  lineSpacingPts?: number
  spaceBefore?: number
  spaceAfter?: number
  run: RunStyle
}

export type LevelStyles = ParagraphStyle[]

export interface TextContext extends ColorContext {
  link: (relationshipId: string | undefined) => string | undefined
}

const ALIGN: Record<string, TextParagraph['align']> = { l: 'left', ctr: 'center', r: 'right', just: 'justify', dist: 'justify' }

const WINGDINGS: Record<string, string> = { '§': '▪', l: '●', n: '■', q: '❑', v: '❖', Ø: '➢', ü: '✓', 'Ÿ': '•', o: '○', p: '□' }

export function emptyLevels(): LevelStyles {
  return Array.from({ length: LEVELS }, () => ({ run: {} }))
}

function defined<T extends object>(value: T): Partial<T> {
  return Object.fromEntries(Object.entries(value).filter(([, entry]) => entry !== undefined)) as Partial<T>
}

function parseRunStyle(node: XmlNode | undefined, context: ColorContext): RunStyle {
  if (!node) return {}
  const underline = node.attrs.u
  const strike = node.attrs.strike
  const latin = child(node, 'latin')?.attrs.typeface ?? child(node, 'ea')?.attrs.typeface
  return defined({
    size: numberAttr(node, 'sz'),
    bold: node.attrs.b === undefined ? undefined : node.attrs.b === '1' || node.attrs.b === 'true',
    italic: node.attrs.i === undefined ? undefined : node.attrs.i === '1' || node.attrs.i === 'true',
    underline: underline === undefined ? undefined : underline !== 'none',
    strike: strike === undefined ? undefined : strike !== 'noStrike',
    baseline: numberAttr(node, 'baseline'),
    color: resolveColor(child(node, 'solidFill'), context),
    font: latin ? themeFont(latin, context.theme) : undefined
  })
}

function spacing(node: XmlNode | undefined): { pct?: number; pts?: number } {
  const pct = numberAttr(child(node, 'spcPct'), 'val')
  const pts = numberAttr(child(node, 'spcPts'), 'val')
  return { pct: pct === undefined ? undefined : pct / 100000, pts: pts === undefined ? undefined : pts / 100 }
}

function parseParagraphStyle(node: XmlNode | undefined, context: ColorContext): Partial<ParagraphStyle> {
  if (!node) return {}
  let bullet: BulletKind | undefined
  if (child(node, 'buNone')) bullet = { kind: 'none' }
  const char = child(node, 'buChar')?.attrs.char
  if (char) bullet = { kind: 'char', char }
  const auto = child(node, 'buAutoNum')
  if (auto) bullet = { kind: 'auto', scheme: auto.attrs.type ?? 'arabicPeriod', startAt: Math.min(Math.max(numberAttr(auto, 'startAt') ?? 1, 1), MAX_AUTO_NUMBER) }
  const line = spacing(child(node, 'lnSpc'))
  const before = spacing(child(node, 'spcBef'))
  const after = spacing(child(node, 'spcAft'))
  const marL = numberAttr(node, 'marL')
  const indent = numberAttr(node, 'indent')
  const bulletSize = numberAttr(child(node, 'buSzPct'), 'val')
  return defined({
    align: ALIGN[node.attrs.algn ?? ''],
    marL: marL === undefined ? undefined : marL / EMU_PER_PX,
    indent: indent === undefined ? undefined : indent / EMU_PER_PX,
    bullet,
    bulletColor: resolveColor(child(node, 'buClr'), context),
    bulletScale: bulletSize === undefined ? undefined : bulletSize / 100000,
    bulletFont: child(node, 'buFont')?.attrs.typeface,
    lineSpacingPct: line.pct,
    lineSpacingPts: line.pts,
    spaceBefore: before.pts,
    spaceAfter: after.pts,
    run: parseRunStyle(child(node, 'defRPr'), context)
  })
}

function mergeParagraph(base: ParagraphStyle, next: Partial<ParagraphStyle>): ParagraphStyle {
  const { run, ...rest } = next
  return { ...base, ...rest, run: { ...base.run, ...run } }
}

export function mergeLevels(base: LevelStyles, list: XmlNode | undefined, context: ColorContext): LevelStyles {
  if (!list) return base
  const shared = parseParagraphStyle(child(list, 'defPPr'), context)
  return base.map((style, level) => {
    const own = child(list, `lvl${level + 1}pPr`)
    return mergeParagraph(mergeParagraph(style, shared), parseParagraphStyle(own, context))
  })
}

export function withRunDefaults(levels: LevelStyles, run: RunStyle): LevelStyles {
  return levels.map((style) => ({ ...style, run: { ...run, ...style.run } }))
}

export function withRunOverrides(levels: LevelStyles, run: RunStyle): LevelStyles {
  return Object.keys(run).length === 0 ? levels : levels.map((style) => ({ ...style, run: { ...style.run, ...run } }))
}

export function fontRefRun(style: XmlNode | undefined, context: ColorContext): RunStyle {
  const ref = child(style, 'fontRef')
  if (!ref) return {}
  return defined({
    color: resolveColor(ref, context),
    font: ref.attrs.idx === 'major' ? context.theme.majorFont : ref.attrs.idx === 'minor' ? context.theme.minorFont : undefined
  })
}

function roman(value: number): string {
  const numerals: Array<[number, string]> = [[1000, 'm'], [900, 'cm'], [500, 'd'], [400, 'cd'], [100, 'c'], [90, 'xc'], [50, 'l'], [40, 'xl'], [10, 'x'], [9, 'ix'], [5, 'v'], [4, 'iv'], [1, 'i']]
  let rest = value
  let text = ''
  for (const [amount, numeral] of numerals) {
    while (rest >= amount) {
      text += numeral
      rest -= amount
    }
  }
  return text
}

function alpha(value: number): string {
  let text = ''
  let rest = value
  while (rest > 0) {
    text = String.fromCharCode(97 + ((rest - 1) % 26)) + text
    rest = Math.floor((rest - 1) / 26)
  }
  return text
}

function autoNumber(scheme: string, value: number): string {
  const lower = scheme.toLowerCase()
  const lettered = Number.isFinite(value) && value >= 1 && value <= MAX_AUTO_NUMBER
  let digits = String(value)
  if (lettered && lower.startsWith('roman')) digits = roman(value)
  else if (lettered && lower.startsWith('alpha')) digits = alpha(value)
  if (lower.includes('uc')) digits = digits.toUpperCase()
  if (lower.endsWith('parenboth')) return `(${digits})`
  if (lower.endsWith('parenr')) return `${digits})`
  if (lower.endsWith('plain')) return digits
  return `${digits}.`
}

function toRun(text: string, style: RunStyle, fontScale: number, link?: string): TextRun {
  const run: TextRun = { text }
  if (style.bold) run.bold = true
  if (style.italic) run.italic = true
  if (style.underline) run.underline = true
  if (style.strike) run.strike = true
  run.size = ((style.size ?? 1800) / 100) * PX_PER_POINT * fontScale
  if (style.color) run.color = style.color
  if (style.font) run.font = style.font
  if (style.baseline) run.baseline = style.baseline / 100000
  if (link) run.link = link
  return run
}

function bulletFor(style: ParagraphStyle, runs: TextRun[], counter: number): TextBullet | undefined {
  const bullet = style.bullet
  if (!bullet || bullet.kind === 'none' || runs.every((run) => !run.text.trim())) return undefined
  const font = style.bulletFont && !style.bulletFont.startsWith('+') ? style.bulletFont : undefined
  const wingdings = font?.toLowerCase().startsWith('wingdings') ?? false
  const text = bullet.kind === 'char' ? (wingdings ? WINGDINGS[bullet.char] ?? '•' : bullet.char) : autoNumber(bullet.scheme, counter)
  const result: TextBullet = { text }
  const color = style.bulletColor ?? runs[0]?.color
  if (color) result.color = color
  if (style.bulletScale) result.scale = style.bulletScale
  if (font && !wingdings && !font.toLowerCase().startsWith('symbol')) result.font = font
  return result
}

export function parseTextBody(
  txBody: XmlNode | undefined,
  levels: LevelStyles,
  bodyPrChain: Array<XmlNode | undefined>,
  context: TextContext
): TextBody | undefined {
  if (!txBody) return undefined
  const styles = mergeLevels(levels, child(txBody, 'lstStyle'), context)
  const attrs: Record<string, string> = {}
  let autofit: XmlNode | undefined
  for (const bodyPr of [...bodyPrChain, child(txBody, 'bodyPr')]) {
    if (!bodyPr) continue
    Object.assign(attrs, bodyPr.attrs)
    const fit = child(bodyPr, 'normAutofit') ?? child(bodyPr, 'noAutofit') ?? child(bodyPr, 'spAutoFit')
    if (fit) autofit = fit
  }
  const fontScale = autofit?.name === 'normAutofit' ? (numberAttr(autofit, 'fontScale') ?? 100000) / 100000 : 1
  const spacingScale = autofit?.name === 'normAutofit' ? 1 - (numberAttr(autofit, 'lnSpcReduction') ?? 0) / 100000 : 1
  const counters = new Array<number>(LEVELS).fill(0)
  const paragraphs: TextParagraph[] = []
  for (const paragraph of children(txBody, 'p')) {
    const pPr = child(paragraph, 'pPr')
    const level = Math.min(LEVELS - 1, Math.max(0, numberAttr(pPr, 'lvl') ?? 0))
    const style = mergeParagraph(styles[level]!, parseParagraphStyle(pPr, context))
    const runs: TextRun[] = []
    for (const node of paragraph.children.flatMap(unwrapAlternate)) {
      if (node.name === 'r' || node.name === 'fld') {
        const rPr = child(node, 'rPr')
        const runStyle = { ...style.run, ...parseRunStyle(rPr, context) }
        const target = context.link(child(rPr, 'hlinkClick')?.attrs['r:id'])
        const text = child(node, 't')?.text ?? ''
        if (text) runs.push(toRun(text, runStyle, fontScale, target && LINK_SCHEMES.test(target) ? target : undefined))
      } else if (node.name === 'br') {
        runs.push(toRun('\n', { ...style.run, ...parseRunStyle(child(node, 'rPr'), context) }, fontScale))
      }
    }
    for (let deeper = level + 1; deeper < LEVELS; deeper++) counters[deeper] = 0
    let counter = 0
    if (style.bullet?.kind === 'auto') {
      counters[level] = counters[level] === 0 ? style.bullet.startAt : counters[level]! + 1
      counter = counters[level]!
    } else if (runs.some((run) => run.text.trim())) {
      counters[level] = 0
    }
    const result: TextParagraph = { runs, level }
    if (style.align) result.align = style.align
    const bullet = bulletFor(style, runs, counter)
    if (bullet) result.bullet = bullet
    if (style.marL) result.marginLeft = style.marL
    if (style.indent) result.indent = style.indent
    if (style.lineSpacingPct !== undefined) result.lineSpacing = style.lineSpacingPct * spacingScale
    if (style.lineSpacingPts !== undefined) result.lineSpacingPx = style.lineSpacingPts * PX_PER_POINT * spacingScale
    if (style.spaceBefore) result.spaceBefore = style.spaceBefore * PX_PER_POINT
    if (style.spaceAfter) result.spaceAfter = style.spaceAfter * PX_PER_POINT
    if (runs.length === 0) {
      const end = { ...style.run, ...parseRunStyle(child(paragraph, 'endParaRPr'), context) }
      result.emptySize = ((end.size ?? 1800) / 100) * PX_PER_POINT * fontScale
    }
    paragraphs.push(result)
  }
  const inset = (name: string, fallback: number) => {
    const value = attrs[name] ? Number(attrs[name]) : NaN
    return (Number.isFinite(value) ? value : fallback) / EMU_PER_PX
  }
  const anchor = attrs.anchor === 'ctr' ? 'middle' : attrs.anchor === 'b' ? 'bottom' : 'top'
  return {
    paragraphs,
    anchor,
    insets: { left: inset('lIns', 91440), top: inset('tIns', 45720), right: inset('rIns', 91440), bottom: inset('bIns', 45720) },
    wrap: attrs.wrap !== 'none'
  }
}

export function hasText(body: TextBody | undefined): boolean {
  return Boolean(body?.paragraphs.some((paragraph) => paragraph.runs.some((run) => run.text.trim())))
}

export function plainText(txBody: XmlNode | undefined): string {
  return children(txBody, 'p')
    .map((paragraph) =>
      paragraph.children
        .flatMap(unwrapAlternate)
        .map((node) => (node.name === 'br' ? '\n' : node.name === 'r' || node.name === 'fld' ? child(node, 't')?.text ?? '' : ''))
        .join('')
    )
    .join('\n')
}
