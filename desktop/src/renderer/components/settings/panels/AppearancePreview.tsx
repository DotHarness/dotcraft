import { useMemo, type CSSProperties, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useUIStore } from '../../../stores/uiStore'
import { LineSpans } from '../../code/CodeSpans'
import {
  fileCacheKey,
  normalizeNewlines,
  splitLines,
  useFileHighlight,
  type HighlightedLine
} from '../../../highlight'

/** Lines the preview presents as changed; the rest are context. */
const CHANGED_LINES = new Set([1, 2, 3])
/** The line whose string literal is repainted in the live accent color. */
const ACCENT_LINE = 2
const BASELINE_ACCENT = '#4566cc'
const BASELINE_CODE_SIZE = 12

function snippet(surface: string, accent: string, codeSize: number): string {
  return [
    'const themePreview: ThemeConfig = {',
    `  surface: "${surface}",`,
    `  accent: "${accent}",`,
    `  codeSize: ${codeSize},`,
    '};'
  ].join('\n')
}

/**
 * The snippet runs through the product's own highlighter rather than carrying a
 * hand-written palette, so what the preview shows is what a real file shows.
 */
export function AppearancePreview({
  accent,
  codeFontSize
}: {
  accent: string
  codeFontSize: number
}): JSX.Element {
  const t = useT()
  const signMode = useUIStore((s) => s.diffMarkers) === 'sign'
  const leftText = useMemo(() => snippet('sidebar', BASELINE_ACCENT, BASELINE_CODE_SIZE), [])
  const rightText = useMemo(
    () => snippet('sidebar-elevated', accent, codeFontSize),
    [accent, codeFontSize]
  )

  return (
    <section style={containerStyle} aria-label={t('settings.appearance.preview.label')}>
      <div className="dc-code" style={editorStyle}>
        <Pane text={leftText} side="left" signMode={signMode} />
        <Pane text={rightText} side="right" signMode={signMode} accent={accent} />
      </div>
    </section>
  )
}

function Pane({
  text,
  side,
  signMode,
  accent
}: {
  text: string
  side: 'left' | 'right'
  signMode: boolean
  accent?: string
}): JSX.Element {
  const request = useMemo(() => ({
    cacheKey: fileCacheKey('appearance-preview.ts', 'typescript', text),
    name: 'appearance-preview.ts',
    lang: 'typescript',
    contents: text
  }), [text])
  const highlighted = useFileHighlight(request)
  const lines = useMemo(() => splitLines(normalizeNewlines(text)), [text])

  return (
    <div style={paneStyle(side)}>
      {lines.map((line, index) => {
        const changed = CHANGED_LINES.has(index)
        return (
          <div key={index} style={lineStyle(changed, side, signMode)}>
            <span style={lineNoStyle} data-line-num>{index + 1}</span>
            {signMode && (
              <span style={signStyle(side)}>{changed ? (side === 'left' ? '-' : '+') : ' '}</span>
            )}
            <span style={codeStyle} data-line={index + 1}>
              <LineSpans
                line={paintAccent(highlighted?.lines[index], index, accent)}
                text={line}
              />
            </span>
          </div>
        )
      })}
    </div>
  )
}

/**
 * Only this one run overrides its syntax color, so the preview can show the chosen
 * accent against the theme; everything else stays as the highlighter produced it.
 */
function paintAccent(
  line: HighlightedLine | undefined,
  index: number,
  accent: string | undefined
): HighlightedLine | undefined {
  if (line === undefined || accent === undefined || index !== ACCENT_LINE) return line
  return line.map((span) => span.text.includes(accent)
    ? { ...span, style: { color: 'var(--accent)' } }
    : span)
}

const containerStyle: CSSProperties = {
  overflow: 'hidden',
  border: '1px solid var(--border-default)',
  borderRadius: 12,
  marginBottom: 16,
  background: 'var(--bg-secondary)'
}

const editorStyle: CSSProperties = {
  display: 'flex',
  background: 'var(--code-block-bg)',
  fontFamily: 'var(--font-mono)',
  fontSize: 'var(--text-code-size)',
  lineHeight: 1.6
}

function paneStyle(side: 'left' | 'right'): CSSProperties {
  return {
    flex: 1,
    minWidth: 0,
    padding: '10px 0',
    borderRight: side === 'left' ? '1px solid var(--border-default)' : undefined,
    overflowX: 'auto'
  }
}

function lineStyle(changed: boolean, side: 'left' | 'right', signMode: boolean): CSSProperties {
  const style: CSSProperties = { display: 'flex', minWidth: 'max-content', whiteSpace: 'pre' }
  if (changed) {
    // The tinted background stays in both modes; color mode adds a left accent bar, while
    // +/- mode drops the bar in favor of the gutter sign rendered in <Pane>.
    style.background = side === 'left' ? 'var(--diff-remove-bg)' : 'var(--diff-add-bg)'
    if (!signMode) {
      style.boxShadow = `inset 2px 0 0 ${side === 'left' ? 'var(--error)' : 'var(--success)'}`
    }
  }
  return style
}

const lineNoStyle: CSSProperties = {
  width: 30,
  flexShrink: 0,
  paddingRight: 10,
  color: 'var(--text-dimmed)',
  textAlign: 'right',
  userSelect: 'none'
}

function signStyle(side: 'left' | 'right'): CSSProperties {
  return {
    width: 14,
    flexShrink: 0,
    color: side === 'left' ? 'var(--error)' : 'var(--success)',
    textAlign: 'center',
    userSelect: 'none'
  }
}

const codeStyle: CSSProperties = { paddingRight: 12 }
