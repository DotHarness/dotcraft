import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { Check, Copy } from 'lucide-react'
import type { MermaidConfig, RenderResult } from 'mermaid'
import { THEME_CHANGED_EVENT, resolveThemeMode, type ThemeMode } from '../../../shared/theme'
import { useT } from '../../contexts/LocaleContext'
import { IconButton } from '../ui/IconButton'
import { sanitizeMermaidSvg } from './mermaidSanitize'

interface MermaidDiagramProps {
  source: string
  fallback: ReactNode
}

type MermaidRenderState =
  | { status: 'loading' }
  | { status: 'ready'; svg: string }
  | { status: 'error'; error: string }

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

let nextMermaidId = 0

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

export function MermaidDiagram({ source, fallback }: MermaidDiagramProps): JSX.Element {
  const t = useT()
  const themeMode = useDocumentThemeMode()
  const idRef = useRef<string>(`dc-mermaid-${nextMermaidId++}`)
  const copyResetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [renderState, setRenderState] = useState<MermaidRenderState>({ status: 'loading' })
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    let cancelled = false
    const normalizedSource = normalizeMermaidSource(source)

    if (!normalizedSource) {
      setRenderState({ status: 'error', error: 'Empty Mermaid diagram.' })
      return
    }

    setRenderState({ status: 'loading' })
    void renderMermaid({
      id: idRef.current,
      source: normalizedSource,
      themeMode
    }).then((svg) => {
      if (!cancelled) setRenderState({ status: 'ready', svg })
    }).catch((error: unknown) => {
      if (!cancelled) {
        setRenderState({
          status: 'error',
          error: error instanceof Error ? error.message : String(error)
        })
      }
    })

    return () => {
      cancelled = true
    }
  }, [source, themeMode])

  useEffect(() => {
    return () => {
      if (copyResetTimerRef.current != null) clearTimeout(copyResetTimerRef.current)
    }
  }, [])

  async function handleCopySource(): Promise<void> {
    try {
      await navigator.clipboard.writeText(source)
      setCopied(true)
      if (copyResetTimerRef.current != null) clearTimeout(copyResetTimerRef.current)
      copyResetTimerRef.current = setTimeout(() => {
        setCopied(false)
        copyResetTimerRef.current = null
      }, 1500)
    } catch {
      // Ignore clipboard failures in read-only rendered markdown.
    }
  }

  return (
    <div style={diagramFrameStyle}>
      <div style={toolbarStyle}>
          <IconButton
            size={28}
            bordered
            label={copied ? t('markdown.mermaid.copiedSource') : t('markdown.mermaid.copySource')}
            tooltipLabel={copied ? t('markdown.mermaid.copiedSource') : t('markdown.mermaid.copySource')}
            tooltipPlacement="top"
            onClick={() => { void handleCopySource() }}
            style={{ borderRadius: 7, color: copied ? 'var(--success)' : undefined }}
            icon={copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
          />
      </div>

      {renderState.status === 'loading' ? (
        <div role="status" style={messageStyle}>
          {t('markdown.mermaid.loading')}
        </div>
      ) : renderState.status === 'ready' ? (
        <div
          className="dc-mermaid-diagram"
          data-testid="mermaid-diagram"
          role="img"
          aria-label={t('markdown.mermaid.diagramAria')}
          style={diagramViewportStyle}
          dangerouslySetInnerHTML={{ __html: renderState.svg }}
        />
      ) : (
        <>
          <div role="status" title={renderState.error} style={errorMessageStyle}>
            {t('markdown.mermaid.renderFailed')}
          </div>
          {fallback}
        </>
      )}
    </div>
  )
}

async function renderMermaid({
  id,
  source,
  themeMode
}: {
  id: string
  source: string
  themeMode: ThemeMode
}): Promise<string> {
  const mermaid = await loadMermaid()
  mermaid.initialize(buildMermaidConfig(themeMode))
  const renderHost = createMermaidRenderHost()
  try {
    const result = await mermaid.render(id, source, renderHost)
    return sanitizeMermaidSvg(result.svg)
  } finally {
    renderHost.remove()
  }
}

async function loadMermaid(): Promise<MermaidApi> {
  const module = await import('mermaid')
  return module.default
}

function normalizeMermaidSource(source: string): string {
  const normalized = source.trim().replace(/<br\s*\/?>/gi, '<br/>')
  return normalizeFlowchartLabels(normalized)
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

function useDocumentThemeMode(): ThemeMode {
  const [mode, setMode] = useState(() =>
    resolveThemeMode(document.documentElement.getAttribute('data-theme'))
  )

  useEffect(() => {
    const sync = (): void => {
      setMode(resolveThemeMode(document.documentElement.getAttribute('data-theme')))
    }
    window.addEventListener(THEME_CHANGED_EVENT, sync)

    const observer = new MutationObserver(sync)
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme']
    })

    sync()
    return () => {
      window.removeEventListener(THEME_CHANGED_EVENT, sync)
      observer.disconnect()
    }
  }, [])

  return mode
}

const diagramFrameStyle: CSSProperties = {
  position: 'relative',
  margin: '8px 0 10px',
  border: '1px solid var(--border-default)',
  borderRadius: '8px',
  background: 'var(--bg-secondary)',
  overflow: 'hidden'
}

const toolbarStyle: CSSProperties = {
  position: 'absolute',
  top: '6px',
  right: '8px',
  zIndex: 1
}

const diagramViewportStyle: CSSProperties = {
  overflow: 'auto',
  padding: '36px 16px 16px',
  minHeight: '72px'
}

const messageStyle: CSSProperties = {
  minHeight: '72px',
  padding: '36px 16px 16px',
  color: 'var(--text-secondary)',
  fontSize: '13px'
}

const errorMessageStyle: CSSProperties = {
  padding: '36px 16px 0',
  color: 'var(--warning)',
  fontSize: '12px'
}
