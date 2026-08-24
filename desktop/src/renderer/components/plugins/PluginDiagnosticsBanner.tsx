import type { CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginDiagnosticEntry } from '../../stores/pluginStore'

export function filterVisibleDiagnostics(diagnostics: PluginDiagnosticEntry[]): PluginDiagnosticEntry[] {
  return diagnostics.filter((diagnostic) => {
    const severity = diagnostic.severity.toLowerCase()
    return severity === 'warning' || severity === 'error'
  })
}

export function PluginDiagnosticsBanner({ diagnostics }: { diagnostics: PluginDiagnosticEntry[] }): JSX.Element | null {
  const t = useT()
  if (diagnostics.length === 0) return null
  return (
    <div style={diagnosticsPanel} role="status">
      <strong style={diagnosticsTitle}>{t('plugins.diagnostics.title')}</strong>
      <div style={diagnosticsList}>
        {diagnostics.slice(0, 5).map((diagnostic, index) => (
          <div key={`${diagnostic.code}-${diagnostic.path ?? index}`} style={diagnosticItem}>
            <span style={diagnosticCode}>{diagnostic.code}</span>
            <span style={diagnosticMessage}>{diagnostic.message}</span>
            {diagnostic.path && <span style={diagnosticPath}>{diagnostic.path}</span>}
          </div>
        ))}
        {diagnostics.length > 5 && (
          <div style={diagnosticMore}>{t('plugins.diagnostics.more', { count: String(diagnostics.length - 5) })}</div>
        )}
      </div>
    </div>
  )
}

const diagnosticsPanel: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', padding: '12px 14px', margin: '0 0 24px' }
const diagnosticsTitle: CSSProperties = { display: 'block', fontSize: 13, marginBottom: 8, color: 'var(--text-primary)' }
const diagnosticsList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 7 }
const diagnosticItem: CSSProperties = { display: 'grid', gridTemplateColumns: 'minmax(120px, max-content) minmax(0, 1fr)', columnGap: 10, rowGap: 3, alignItems: 'baseline', fontSize: 12 }
const diagnosticCode: CSSProperties = { color: 'var(--warning, #A16207)', fontFamily: 'var(--font-mono)' }
const diagnosticMessage: CSSProperties = { color: 'var(--text-secondary)', minWidth: 0 }
const diagnosticPath: CSSProperties = { gridColumn: '1 / -1', color: 'var(--text-tertiary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const diagnosticMore: CSSProperties = { color: 'var(--text-tertiary)', fontSize: 12 }
