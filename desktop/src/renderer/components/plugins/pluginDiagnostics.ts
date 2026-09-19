import type { useT } from '../../contexts/LocaleContext'
import type { PluginDiagnosticEntry } from '../../stores/pluginStore'

export function filterVisibleDiagnostics(diagnostics: PluginDiagnosticEntry[]): PluginDiagnosticEntry[] {
  return diagnostics.filter((diagnostic) => {
    const severity = diagnostic.severity.toLowerCase()
    return severity === 'warning' || severity === 'error'
  })
}

export function hasErrorDiagnostic(diagnostics: PluginDiagnosticEntry[]): boolean {
  return diagnostics.some((diagnostic) => diagnostic.severity.toLowerCase() === 'error')
}

/**
 * A code and its parameters are the stable contract; the server message is English
 * fallback text for a code this client carries no copy for.
 */
export function describeDiagnostic(
  diagnostic: PluginDiagnosticEntry,
  t: ReturnType<typeof useT>
): string {
  const name = marketplaceOf(diagnostic)
  if (name == null) return diagnostic.message
  if (diagnostic.code === 'PluginRegistrySourceMissing') return t('plugins.diagnostics.sourceMissing', { name })
  if (diagnostic.code === 'PluginRegistrySnapshotMissing') return t('plugins.diagnostics.snapshotMissing', { name })
  return diagnostic.message
}

/** Why a configured marketplace is currently contributing no plugins, by marketplace name. */
export function buildMarketplaceNotices(
  diagnostics: PluginDiagnosticEntry[],
  t: ReturnType<typeof useT>
): Map<string, string> {
  const notices = new Map<string, string>()
  for (const diagnostic of diagnostics) {
    const name = marketplaceOf(diagnostic)
    if (name != null && !notices.has(name)) notices.set(name, describeDiagnostic(diagnostic, t))
  }
  return notices
}

function marketplaceOf(diagnostic: PluginDiagnosticEntry): string | null {
  const name = diagnostic.parameters?.marketplace
  return typeof name === 'string' && name.length > 0 ? name : null
}
