import type { MermaidConfig, RenderResult } from 'mermaid'
import type { ThemeMode } from '../../../shared/theme'
import { sanitizeMermaidSvg } from './mermaidSanitize'

type MermaidApi = {
  initialize: (config: MermaidConfig) => void
  render: (id: string, text: string, svgContainingElement?: Element) => Promise<RenderResult>
}

type ColorStyleProperty = 'backgroundColor' | 'color'

type MermaidThemeColorFallbacks = {
  textPrimary: string
  textSecondary: string
  bgPrimary: string
  bgSecondary: string
  bgTertiary: string
  borderDefault: string
  borderActive: string
}

const MAX_CACHED_DIAGRAMS = 50
const mermaidSvgCache = new Map<string, string>()
let mermaidRenderQueue: Promise<unknown> = Promise.resolve()

const CSS_COLOR_NUMBER = '-?(?:\\d+(?:\\.\\d+)?|\\.\\d+)(?:e-?\\d+)?'
const CSS_COLOR_CUSTOM_SYNTAX_RE = /\b(?:color-mix|var)\(/i
const HEX_COLOR_RE = /^#[0-9a-f]{3,8}$/i
const RGB_COLOR_RE = new RegExp(`^rgba?\\(\\s*${CSS_COLOR_NUMBER}%?\\s*(?:,|\\s)\\s*${CSS_COLOR_NUMBER}%?\\s*(?:,|\\s)\\s*${CSS_COLOR_NUMBER}%?(?:\\s*(?:,|/)\\s*\\+?${CSS_COLOR_NUMBER}%?)?\\s*\\)$`, 'i')
const HSL_COLOR_RE = new RegExp(`^hsla?\\(\\s*${CSS_COLOR_NUMBER}(?:deg|grad|rad|turn)?\\s*(?:,|\\s)\\s*${CSS_COLOR_NUMBER}%\\s*(?:,|\\s)\\s*${CSS_COLOR_NUMBER}%(?:\\s*(?:,|/)\\s*\\+?${CSS_COLOR_NUMBER}%?)?\\s*\\)$`, 'i')
const SRGB_COLOR_FUNCTION_RE = /^color\(\s*srgb\s+([+-]?(?:\d+(?:\.\d+)?|\.\d+)%?)\s+([+-]?(?:\d+(?:\.\d+)?|\.\d+)%?)\s+([+-]?(?:\d+(?:\.\d+)?|\.\d+)%?)(?:\s*\/\s*([+-]?(?:\d+(?:\.\d+)?|\.\d+)%?))?\s*\)$/i
const MERMAID_SAFE_COLOR_KEYWORDS = new Set(['black', 'transparent', 'white'])
const FLOWCHART_HEADER_RE = /^\s*(?:flowchart|graph)\b/i
const FLOWCHART_NODE_ID_CHAR_RE = /[A-Za-z0-9_-]/
const FLOWCHART_UNSAFE_LABEL_RE = /<br\s*\/?>|[()[\]{}'",:<>?=+]/i

const MERMAID_THEME_COLOR_FALLBACKS: Record<ThemeMode, MermaidThemeColorFallbacks> = {
  system: {
    textPrimary: '#1f2933',
    textSecondary: '#52606d',
    bgPrimary: '#ffffff',
    bgSecondary: '#f5f7fa',
    bgTertiary: '#e9eef3',
    borderDefault: '#cbd2d9',
    borderActive: '#9aa5b1'
  },
  dark: {
    textPrimary: '#f5f5f5',
    textSecondary: '#b9c0c9',
    bgPrimary: '#202020',
    bgSecondary: '#272727',
    bgTertiary: '#303030',
    borderDefault: '#3a3a3a',
    borderActive: '#555555'
  },
  light: {
    textPrimary: '#1f2933',
    textSecondary: '#596675',
    bgPrimary: '#f7f8fa',
    bgSecondary: '#ffffff',
    bgTertiary: '#eef1f4',
    borderDefault: '#d7dce2',
    borderActive: '#b8c0ca'
  }
}

export function normalizeMermaidSource(source: string): string {
  const normalized = source.trim().replace(/<br\s*\/?>/gi, '<br/>')
  return normalizeFlowchartLabels(normalized)
}

export function buildMermaidCacheKey(normalizedSource: string, themeMode: ThemeMode): string {
  return `${themeMode}\0${normalizedSource}`
}

export function getCachedMermaidSvg(normalizedSource: string, themeMode: ThemeMode): string | undefined {
  if (!normalizedSource) return undefined
  const key = buildMermaidCacheKey(normalizedSource, themeMode)
  const cached = mermaidSvgCache.get(key)
  if (cached !== undefined) {
    mermaidSvgCache.delete(key)
    mermaidSvgCache.set(key, cached)
  }
  return cached
}

export function clearMermaidSvgCache(): void {
  mermaidSvgCache.clear()
}

function storeCachedMermaidSvg(key: string, svg: string): void {
  mermaidSvgCache.delete(key)
  mermaidSvgCache.set(key, svg)
  if (mermaidSvgCache.size > MAX_CACHED_DIAGRAMS) {
    const oldest = mermaidSvgCache.keys().next().value
    if (oldest !== undefined) mermaidSvgCache.delete(oldest)
  }
}

function enqueueMermaidRender<T>(task: () => Promise<T>): Promise<T> {
  const next = mermaidRenderQueue.then(task, task)
  mermaidRenderQueue = next.then(() => undefined, () => undefined)
  return next
}

export async function renderMermaidSvg({
  id,
  source,
  themeMode
}: {
  id: string
  source: string
  themeMode: ThemeMode
}): Promise<string> {
  const normalized = normalizeMermaidSource(source)
  const cacheKey = buildMermaidCacheKey(normalized, themeMode)
  const cached = getCachedMermaidSvg(normalized, themeMode)
  if (cached !== undefined) return cached

  return enqueueMermaidRender(async () => {
    const queuedHit = getCachedMermaidSvg(normalized, themeMode)
    if (queuedHit !== undefined) return queuedHit

    const mermaid = await loadMermaid()
    mermaid.initialize(buildMermaidConfig(themeMode))
    const renderHost = createMermaidRenderHost()
    try {
      const result = await mermaid.render(id, normalized, renderHost)
      const svg = sanitizeMermaidSvg(result.svg)
      if (normalized) storeCachedMermaidSvg(cacheKey, svg)
      return svg
    } finally {
      renderHost.remove()
    }
  })
}

export function getMermaidBackgroundColor(themeMode: ThemeMode): string {
  return cssColorVar(
    document.documentElement,
    '--bg-primary',
    MERMAID_THEME_COLOR_FALLBACKS[themeMode].bgPrimary,
    'backgroundColor'
  )
}

async function loadMermaid(): Promise<MermaidApi> {
  const module = await import('mermaid')
  return module.default
}

function normalizeFlowchartLabels(source: string): string {
  const lines = source.split('\n')
  const firstContentLine = lines.find((line) => line.trim().length > 0)
  if (firstContentLine == null || !FLOWCHART_HEADER_RE.test(firstContentLine)) return source

  return lines.map((line) => normalizeFlowchartLabelsInLine(line)).join('\n')
}

function normalizeFlowchartLabelsInLine(line: string): string {
  let result = ''
  let cursor = 0

  while (cursor < line.length) {
    const shape = findNextFlowchartNodeShape(line, cursor)
    if (shape == null) {
      result += line.slice(cursor)
      break
    }

    const labelStart = shape.openIndex + shape.open.length
    const labelEnd = findFlowchartLabelEnd(line, labelStart, shape.close)
    if (labelEnd == null) {
      result += line.slice(cursor)
      break
    }

    const label = line.slice(labelStart, labelEnd)
    result += line.slice(cursor, labelStart)
    result += shouldQuoteFlowchartLabel(label) ? quoteFlowchartLabel(label) : label
    result += shape.close
    cursor = labelEnd + shape.close.length
  }

  return result
}

function findNextFlowchartNodeShape(
  line: string,
  start: number
): { openIndex: number; open: string; close: string } | null {
  for (let index = start; index < line.length; index++) {
    const char = line[index]
    if (char !== '[' && char !== '{') continue

    const idEnd = skipWhitespaceLeft(line, index - 1)
    const idStart = scanFlowchartNodeIdStart(line, idEnd)
    if (idStart > idEnd) continue

    const open = line.startsWith(char + char, index) ? char + char : char
    return {
      openIndex: index,
      open,
      close: char === '[' ? ']'.repeat(open.length) : '}'.repeat(open.length)
    }
  }

  return null
}

function skipWhitespaceLeft(line: string, index: number): number {
  while (index >= 0 && /\s/.test(line[index])) index--
  return index
}

function scanFlowchartNodeIdStart(line: string, idEnd: number): number {
  let index = idEnd
  while (index >= 0 && FLOWCHART_NODE_ID_CHAR_RE.test(line[index])) index--
  return index + 1
}

function findFlowchartLabelEnd(line: string, labelStart: number, close: string): number | null {
  for (let index = labelStart; index < line.length; index++) {
    if (line.startsWith(close, index) && isFlowchartLabelBoundary(line, index + close.length)) {
      return index
    }
  }

  return null
}

function isFlowchartLabelBoundary(line: string, afterClose: number): boolean {
  const rest = line.slice(afterClose).trimStart()
  return (
    rest.length === 0 ||
    rest.startsWith('--') ||
    rest.startsWith('-.') ||
    rest.startsWith('==') ||
    rest.startsWith('~~~') ||
    rest.startsWith(':::') ||
    rest.startsWith('@') ||
    rest.startsWith('&') ||
    rest.startsWith(';') ||
    rest.startsWith(',')
  )
}

function shouldQuoteFlowchartLabel(label: string): boolean {
  const trimmed = label.trim()
  return trimmed.length > 0 && !trimmed.startsWith('"') && FLOWCHART_UNSAFE_LABEL_RE.test(label)
}

function quoteFlowchartLabel(label: string): string {
  return `"${label.replace(/"/g, '&quot;')}"`
}

function createMermaidRenderHost(): HTMLDivElement {
  const host = document.createElement('div')
  host.style.position = 'absolute'
  host.style.left = '-10000px'
  host.style.top = '0'
  host.style.width = '1000px'
  host.style.height = '1px'
  host.style.overflow = 'hidden'
  host.style.visibility = 'hidden'
  host.style.pointerEvents = 'none'
  const parent = document.body ?? document.documentElement
  parent.appendChild(host)
  return host
}

function buildMermaidConfig(themeMode: ThemeMode): MermaidConfig {
  const root = document.documentElement
  const colors = MERMAID_THEME_COLOR_FALLBACKS[themeMode]
  const fontFamily = cssVar(root, '--font-body', 'system-ui, sans-serif')
  const textPrimary = cssColorVar(root, '--text-primary', colors.textPrimary)
  const textSecondary = cssColorVar(root, '--text-secondary', colors.textSecondary)
  const bgPrimary = cssColorVar(root, '--bg-primary', colors.bgPrimary, 'backgroundColor')
  const bgSecondary = cssColorVar(root, '--bg-secondary', colors.bgSecondary, 'backgroundColor')
  const bgTertiary = cssColorVar(root, '--bg-tertiary', colors.bgTertiary, 'backgroundColor')
  const borderDefault = cssColorVar(root, '--border-default', colors.borderDefault)
  const borderActive = cssColorVar(root, '--border-active', colors.borderActive)

  return {
    startOnLoad: false,
    securityLevel: 'antiscript',
    suppressErrorRendering: true,
    theme: 'base',
    htmlLabels: true,
    fontFamily,
    flowchart: {
      useMaxWidth: false
    },
    themeVariables: {
      darkMode: themeMode === 'dark',
      background: 'transparent',
      fontFamily,
      fontSize: '13px',
      primaryColor: bgSecondary,
      primaryTextColor: textPrimary,
      primaryBorderColor: borderActive,
      secondaryColor: bgTertiary,
      tertiaryColor: bgPrimary,
      lineColor: textSecondary,
      textColor: textPrimary,
      mainBkg: bgSecondary,
      nodeBorder: borderActive,
      clusterBkg: bgPrimary,
      clusterBorder: borderDefault,
      edgeLabelBackground: bgPrimary,
      titleColor: textPrimary,
      labelTextColor: textPrimary
    }
  }
}

function cssVar(root: HTMLElement, name: string, fallback: string): string {
  const value = window.getComputedStyle(root).getPropertyValue(name).trim()
  return value || fallback
}

function cssColorVar(
  root: HTMLElement,
  name: string,
  fallback: string,
  property: ColorStyleProperty = 'color'
): string {
  return normalizeMermaidColor(cssVar(root, name, fallback), fallback, property)
}

function normalizeMermaidColor(value: string, fallback: string, property: ColorStyleProperty): string {
  const trimmed = value.trim()
  if (isMermaidSafeColor(trimmed)) return trimmed

  const resolved = resolveCssColor(trimmed, property)
  if (resolved != null) {
    const normalized = normalizeResolvedCssColor(resolved)
    if (normalized != null && isMermaidSafeColor(normalized)) return normalized
  }

  return isMermaidSafeColor(fallback) ? fallback : '#000000'
}

function resolveCssColor(value: string, property: ColorStyleProperty): string | null {
  if (!value) return null

  const probe = document.createElement('span')
  const host = document.body ?? document.documentElement
  probe.style.position = 'absolute'
  probe.style.visibility = 'hidden'
  probe.style.pointerEvents = 'none'

  if (property === 'backgroundColor') {
    probe.style.backgroundColor = value
  } else {
    probe.style.color = value
  }

  host.appendChild(probe)
  try {
    const resolved = window.getComputedStyle(probe)[property].trim()
    return resolved || null
  } finally {
    probe.remove()
  }
}

function normalizeResolvedCssColor(value: string): string | null {
  const trimmed = value.trim()
  if (isMermaidSafeColor(trimmed)) return trimmed
  return srgbColorFunctionToRgba(trimmed)
}

function isMermaidSafeColor(value: string): boolean {
  if (!value || CSS_COLOR_CUSTOM_SYNTAX_RE.test(value)) return false

  return (
    HEX_COLOR_RE.test(value) ||
    RGB_COLOR_RE.test(value) ||
    HSL_COLOR_RE.test(value) ||
    MERMAID_SAFE_COLOR_KEYWORDS.has(value.toLowerCase())
  )
}

function srgbColorFunctionToRgba(value: string): string | null {
  const match = value.match(SRGB_COLOR_FUNCTION_RE)
  if (match == null) return null

  const [, red, green, blue, alpha] = match
  const r = parseSrgbChannel(red)
  const g = parseSrgbChannel(green)
  const b = parseSrgbChannel(blue)
  const a = alpha != null ? parseAlphaChannel(alpha) : 1

  return a < 1
    ? `rgba(${r}, ${g}, ${b}, ${roundAlpha(a)})`
    : `rgb(${r}, ${g}, ${b})`
}

function parseSrgbChannel(value: string): number {
  const numeric = parseFloat(value)
  const scaled = value.endsWith('%') ? numeric * 2.55 : numeric * 255
  return Math.max(0, Math.min(255, Math.round(scaled)))
}

function parseAlphaChannel(value: string): number {
  const numeric = parseFloat(value)
  const scaled = value.endsWith('%') ? numeric / 100 : numeric
  return Math.max(0, Math.min(1, scaled))
}

function roundAlpha(value: number): number {
  return Math.round(value * 1000) / 1000
}
