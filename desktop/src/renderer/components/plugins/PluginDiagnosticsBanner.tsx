import type { CSSProperties } from 'react'
import { TriangleAlert } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginDiagnosticEntry } from '../../stores/pluginStore'
import { describeDiagnostic, hasErrorDiagnostic } from './pluginDiagnostics'

// Enough rows to recognise what went wrong, then a count: a reader who needs the
// rest needs the log, not a longer notice.
const VISIBLE_LIMIT = 3

export function PluginDiagnosticsBanner({
  diagnostics,
  column
}: {
  diagnostics: PluginDiagnosticEntry[]
  column: CSSProperties
}): JSX.Element | null {
  const t = useT()
  if (diagnostics.length === 0) return null

  const tone = hasErrorDiagnostic(diagnostics) ? 'error' : 'warning'
  const hidden = diagnostics.length - VISIBLE_LIMIT
  return (
    <div style={{ ...panel(tone), maxWidth: column.maxWidth, margin: '0 auto 24px' }} role="status">
      <TriangleAlert size={20} aria-hidden style={{ color: `var(--${tone})`, flexShrink: 0 }} />
      <div style={diagnosticsBody}>
        <strong style={diagnosticsTitle}>{t('plugins.diagnostics.title')}</strong>
        {diagnostics.slice(0, VISIBLE_LIMIT).map((diagnostic, index) => (
          <div key={`${diagnostic.code}-${index}`} style={diagnosticItem}>
            <span>{describeDiagnostic(diagnostic, t)}</span>
            {diagnostic.path && <span style={diagnosticPath}>{diagnostic.path}</span>}
          </div>
        ))}
        {hidden > 0 && (
          <span style={diagnosticMore}>{t('plugins.diagnostics.more', { count: String(hidden) })}</span>
        )}
      </div>
    </div>
  )
}

function panel(tone: 'warning' | 'error'): CSSProperties {
  return {
    display: 'flex',
    gap: 12,
    padding: 14,
    border: `1px solid color-mix(in srgb, var(--${tone}) 38%, var(--border-default))`,
    borderRadius: 10,
    background: `color-mix(in srgb, var(--${tone}) 8%, transparent)`
  }
}

const diagnosticsBody: CSSProperties = { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 6 }
const diagnosticsTitle: CSSProperties = { fontSize: 13, fontWeight: 600, color: 'var(--text-primary)' }
const diagnosticItem: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 2, fontSize: 12.5, color: 'var(--text-secondary)' }
const diagnosticPath: CSSProperties = { color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const diagnosticMore: CSSProperties = { fontSize: 12.5, color: 'var(--text-tertiary)' }
