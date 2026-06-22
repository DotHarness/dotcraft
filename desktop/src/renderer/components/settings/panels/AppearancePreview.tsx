import type { CSSProperties, JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useUIStore } from '../../../stores/uiStore'

type LineKind = 'context' | 'add' | 'remove'
interface SampleLine {
  no: string
  kind: LineKind
  text: string
}

// A small, fixed sample that exercises theme colors, the code font size, and the diff style.
const SAMPLE: SampleLine[] = [
  { no: '1', kind: 'context', text: 'export function applyTheme(mode) {' },
  { no: '2', kind: 'context', text: '  // resolve System -> light | dark' },
  { no: '3', kind: 'remove', text: "  root.setAttribute('data-theme', mode)" },
  { no: '4', kind: 'add', text: "  root.setAttribute('data-theme', resolve(mode))" },
  { no: '5', kind: 'context', text: '}' }
]

/**
 * Live Appearance preview. Reads the applied CSS variables and the diff-markers store so the
 * theme, accent, code font size, and diff style are reflected as the user changes them. Purely
 * presentational — a documented visualization surface (see specs/clients/DESIGN.md).
 */
export function AppearancePreview(): JSX.Element {
  const t = useT()
  const signMode = useUIStore((s) => s.diffMarkers) === 'sign'

  return (
    <section style={containerStyle} aria-label={t('settings.appearance.preview.label')}>
      <div style={toolbarStyle}>{t('settings.appearance.preview.label')}</div>
      <div style={editorStyle}>
        {SAMPLE.map((line) => (
          <div key={line.no} style={rowStyle(line.kind, signMode)}>
            <span style={lineNoStyle}>{line.no}</span>
            <span style={gutterStyle(line.kind)}>
              {line.kind === 'add' ? '+' : line.kind === 'remove' ? '-' : ' '}
            </span>
            <span style={textStyle(line.kind, signMode)}>{line.text}</span>
          </div>
        ))}
      </div>
      <div style={chromeRowStyle}>
        <span style={primaryBtnStyle}>Save</span>
        <span style={secondaryBtnStyle}>Configure</span>
        <span style={linkStyle}>View docs ↗</span>
        <span style={badgeStyle}>NEW</span>
        <span style={selectedRowStyle}>
          <span style={dotStyle} aria-hidden />
          Active thread
        </span>
      </div>
    </section>
  )
}

const containerStyle: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: 12,
  background: 'var(--bg-secondary)',
  overflow: 'hidden',
  marginBottom: 16
}

const toolbarStyle: CSSProperties = {
  padding: '9px 13px',
  borderBottom: '1px solid var(--border-default)',
  fontSize: 11,
  letterSpacing: '0.05em',
  textTransform: 'uppercase',
  color: 'var(--text-dimmed)'
}

const editorStyle: CSSProperties = {
  background: 'var(--code-block-bg)',
  padding: '11px 0',
  fontFamily: 'var(--font-mono)',
  fontSize: 'var(--text-code-size)',
  lineHeight: 1.6
}

function rowStyle(kind: LineKind, signMode: boolean): CSSProperties {
  return {
    display: 'flex',
    background: signMode
      ? 'transparent'
      : kind === 'add'
        ? 'var(--diff-add-bg)'
        : kind === 'remove'
          ? 'var(--diff-remove-bg)'
          : 'transparent',
    whiteSpace: 'pre'
  }
}

const lineNoStyle: CSSProperties = {
  width: 34,
  flexShrink: 0,
  textAlign: 'right',
  paddingRight: 12,
  color: 'var(--text-dimmed)',
  userSelect: 'none'
}

function gutterStyle(kind: LineKind): CSSProperties {
  return {
    width: 16,
    flexShrink: 0,
    textAlign: 'center',
    userSelect: 'none',
    color: kind === 'add' ? 'var(--success)' : kind === 'remove' ? 'var(--error)' : 'var(--text-dimmed)'
  }
}

function textStyle(kind: LineKind, signMode: boolean): CSSProperties {
  return {
    flex: 1,
    paddingRight: 12,
    color: signMode
      ? kind === 'add'
        ? 'var(--success)'
        : kind === 'remove'
          ? 'var(--error)'
          : 'var(--text-primary)'
      : kind === 'remove'
        ? 'var(--text-secondary)'
        : 'var(--text-primary)'
  }
}

const chromeRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  flexWrap: 'wrap',
  padding: '12px 13px'
}

const primaryBtnStyle: CSSProperties = {
  fontSize: 12.5,
  fontWeight: 600,
  padding: '6px 12px',
  borderRadius: 8,
  background: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  border: '1px solid var(--text-primary)'
}

const secondaryBtnStyle: CSSProperties = {
  fontSize: 12.5,
  fontWeight: 500,
  padding: '6px 12px',
  borderRadius: 8,
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  border: '1px solid var(--border-default)'
}

const linkStyle: CSSProperties = {
  fontSize: 12.5,
  fontWeight: 600,
  color: 'var(--accent)'
}

const badgeStyle: CSSProperties = {
  fontSize: 10,
  fontWeight: 700,
  letterSpacing: '0.04em',
  color: 'var(--accent)',
  background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
  padding: '2px 7px',
  borderRadius: 999
}

const selectedRowStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 8,
  fontSize: 13,
  color: 'var(--text-primary)',
  padding: '6px 10px',
  borderRadius: 8,
  background: 'color-mix(in srgb, var(--accent) 12%, var(--bg-secondary))',
  boxShadow: 'inset 2px 0 0 var(--accent)'
}

const dotStyle: CSSProperties = {
  width: 7,
  height: 7,
  borderRadius: 999,
  background: 'var(--accent)'
}
