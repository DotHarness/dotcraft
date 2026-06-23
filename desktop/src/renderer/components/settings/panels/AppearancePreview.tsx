import type { CSSProperties, JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useUIStore } from '../../../stores/uiStore'

interface Segment {
  text: string
  /** highlight.js token class, styled by the active hljs stylesheet (theme-aware). */
  cls?: string
  /** Render this segment in the live accent color (used for the accent value). */
  accent?: boolean
}
interface CodeLine {
  no: number
  changed: boolean
  segs: Segment[]
}

const HEADER: Segment[] = [
  { text: 'const ', cls: 'hljs-keyword' },
  { text: 'themePreview' },
  { text: ': ' },
  { text: 'ThemeConfig', cls: 'hljs-title' },
  { text: ' = {' }
]
const CLOSE: Segment[] = [{ text: '};' }]

function attr(name: string, value: Segment): Segment[] {
  return [{ text: '  ' }, { text: name, cls: 'hljs-attr' }, { text: ': ' }, value, { text: ',' }]
}

// The "before" pane is fixed; the "after" pane reflects the current accent + code font size,
// so the diff demonstrates the live theme, accent, and code size against a baseline.
const LEFT: CodeLine[] = [
  { no: 1, changed: false, segs: HEADER },
  { no: 2, changed: true, segs: attr('surface', { text: '"sidebar"', cls: 'hljs-string' }) },
  { no: 3, changed: true, segs: attr('accent', { text: '"#4566cc"', cls: 'hljs-string' }) },
  { no: 4, changed: true, segs: attr('codeSize', { text: '12', cls: 'hljs-number' }) },
  { no: 5, changed: false, segs: CLOSE }
]

function rightLines(accent: string, codeFontSize: number): CodeLine[] {
  return [
    { no: 1, changed: false, segs: HEADER },
    { no: 2, changed: true, segs: attr('surface', { text: '"sidebar-elevated"', cls: 'hljs-string' }) },
    { no: 3, changed: true, segs: attr('accent', { text: `"${accent}"`, cls: 'hljs-string', accent: true }) },
    { no: 4, changed: true, segs: attr('codeSize', { text: String(codeFontSize), cls: 'hljs-number' }) },
    { no: 5, changed: false, segs: CLOSE }
  ]
}

/**
 * Live Appearance preview rendered as a split (before/after) code diff, mirroring the editor's
 * theme: it reflects the applied theme colors, accent, and code font size, and follows the diff
 * style (tinted lines vs +/- markers). Purely presentational — a documented visualization
 * surface (see specs/clients/DESIGN.md).
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
  const right = rightLines(accent, codeFontSize)

  return (
    <section style={containerStyle} aria-label={t('settings.appearance.preview.label')}>
      <div className="hljs" style={editorStyle}>
        <Pane lines={LEFT} side="left" signMode={signMode} />
        <Pane lines={right} side="right" signMode={signMode} />
      </div>
    </section>
  )
}

function Pane({
  lines,
  side,
  signMode
}: {
  lines: CodeLine[]
  side: 'left' | 'right'
  signMode: boolean
}): JSX.Element {
  return (
    <div style={paneStyle(side)}>
      {lines.map((line) => (
        <div key={line.no} style={lineStyle(line.changed, side, signMode)}>
          <span style={lineNoStyle}>{line.no}</span>
          {signMode && (
            <span style={signStyle(side)}>{line.changed ? (side === 'left' ? '-' : '+') : ' '}</span>
          )}
          <span style={codeStyle}>
            {line.segs.map((seg, i) => (
              <span key={i} className={seg.cls} style={seg.accent ? accentSegStyle : undefined}>
                {seg.text}
              </span>
            ))}
          </span>
        </div>
      ))}
    </div>
  )
}

const containerStyle: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: 12,
  background: 'var(--bg-secondary)',
  overflow: 'hidden',
  marginBottom: 16
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
    overflowX: 'auto',
    padding: '10px 0',
    borderRight: side === 'left' ? '1px solid var(--border-default)' : undefined
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
  textAlign: 'right',
  paddingRight: 10,
  color: 'var(--text-dimmed)',
  userSelect: 'none'
}

function signStyle(side: 'left' | 'right'): CSSProperties {
  return {
    width: 14,
    flexShrink: 0,
    textAlign: 'center',
    userSelect: 'none',
    color: side === 'left' ? 'var(--error)' : 'var(--success)'
  }
}

const codeStyle: CSSProperties = { paddingRight: 12 }

const accentSegStyle: CSSProperties = { color: 'var(--accent)' }
