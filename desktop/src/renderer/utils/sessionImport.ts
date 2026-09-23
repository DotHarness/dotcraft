import type { ServerCapabilities } from '../stores/connectionStore'
import type { ThreadSummary } from '../types/thread'

const SOURCE_LABELS = new Map<string, string>([
  ['claude-code', 'Claude Code'],
  ['codex', 'ChatGPT'],
  ['cursor', 'Cursor']
])

/** Product names stay untranslated. */
export function importSourceLabel(source: string): string {
  return SOURCE_LABELS.get(source) ?? source
}

export function isSessionImportAvailable(capabilities: ServerCapabilities | null | undefined): boolean {
  return capabilities?.extensions?.sessionImport != null
}

export function isSessionImportThread(thread: Pick<ThreadSummary, 'originChannel'>): boolean {
  return thread.originChannel?.toLowerCase() === 'session-import'
}

export function importedThreadSourceLabel(
  thread: Pick<ThreadSummary, 'originChannel' | 'metadata'>
): string | null {
  const source = thread.metadata?.['dotcraft.import.source']
  return isSessionImportThread(thread) && typeof source === 'string' ? importSourceLabel(source) : null
}
