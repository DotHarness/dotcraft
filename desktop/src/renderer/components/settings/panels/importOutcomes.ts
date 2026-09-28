import type { ImportCompletedNotification, ImportOutcome } from '@dotcraft/sdk/contracts'
import { usePluginStore } from '../../../stores/pluginStore'
import { useSkillsStore } from '../../../stores/skillsStore'
import { useThreadStore } from '../../../stores/threadStore'
import { useUIStore } from '../../../stores/uiStore'
import { importSourceLabel } from '../../../utils/sessionImport'
import type { StatusTone } from '../../ui/StatusIndicator'
import { SETUP_CATEGORIES } from './importPresentation'

type Translate = (key: string, vars?: Record<string, string | number>) => string

const CATEGORY_ORDER: readonly string[] = [...SETUP_CATEGORIES, 'sessions']
const IMPORTED = new Set(['imported', 'appended', 'attention'])
const FAILED = new Set(['failed', 'unsupported'])
const OPENABLE = new Set([...IMPORTED, 'existing', 'skipped'])
/** Outcomes the server records for the pass itself rather than for an item. */
const PASS_ENTRIES = new Set(['pass', 'history'])
/** Batches that finish within a minute of the newest one read as the latest import. */
const LATEST_WINDOW_MS = 60_000
const REASON_CODES = new Set([
  'import_source_changed',
  'import_source_invalid',
  'import_hooks_need_trust',
  'import_environment_missing',
  'import_command_semantics_unsupported',
  'import_plugin_review',
  'import_plugin_content_missing',
  'import_plugin_no_supported_content',
  'import_mcp_policy_unsupported',
  'import_mcp_transport_unsupported',
  'import_mcp_variable_unsupported',
  'import_write_failed',
  'import_history_write_failed',
  'import_pass_failed',
  'import_failed',
  'import_thread_exists',
  'import_append_refused'
])
const ACTIONS: Record<string, string> = {
  skills: 'settings.import.action.openSkills',
  plugins: 'settings.import.action.openPlugin',
  commands: 'settings.import.action.useCommand',
  mcp: 'settings.import.action.openMcp',
  hooks: 'settings.import.action.openHooks',
  instructions: 'settings.import.action.openInstructions'
}

export interface OutcomeTally {
  imported: number
  failed: number
  review: number
}

export function tally(outcomes: ImportOutcome[]): OutcomeTally {
  return {
    imported: outcomes.filter(o => IMPORTED.has(o.status)).length,
    failed: outcomes.filter(o => FAILED.has(o.status)).length,
    review: outcomes.filter(o => o.status === 'attention').length
  }
}

export function splitBatch(batch: ImportCompletedNotification): { items: ImportOutcome[]; passErrors: ImportOutcome[] } {
  return {
    items: batch.outcomes.filter(o => !PASS_ENTRIES.has(o.sourceId)),
    passErrors: batch.outcomes.filter(o => PASS_ENTRIES.has(o.sourceId))
  }
}

export function batchSources(items: ImportOutcome[]): string[] {
  return [...new Set(items.map(o => o.source).filter(Boolean))]
}

export function batchTitle(batch: ImportCompletedNotification, sources: string[], locale: string, t: Translate): string {
  if (sources.length === 0) return t('settings.import.history.untitled')
  const source = new Intl.ListFormat(locale, { type: 'conjunction' }).format(sources.map(importSourceLabel))
  return t(batch.trigger === 'sync' ? 'settings.import.history.syncedFrom' : 'settings.import.history.importedFrom', { source })
}

export function tallySummary({ imported, failed, review }: OutcomeTally, t: Translate): string {
  if (imported === 0 && failed === 0) return t('settings.import.history.nothingNew')
  const count = (key: string, value: number): string =>
    t(`settings.import.history.${key}.${value === 1 ? 'one' : 'other'}`, { count: value })
  return [
    count('imported', imported),
    failed > 0 ? count('failed', failed) : null,
    review > 0 ? count('review', review) : null
  ].filter((part): part is string => part !== null).join(' · ')
}

export function tallyTone({ imported, failed, review }: OutcomeTally): StatusTone {
  if (failed > 0) return 'error'
  if (review > 0) return 'warning'
  return imported > 0 ? 'success' : 'neutral'
}

export function groupByCategory(items: ImportOutcome[]): { category: string; outcomes: ImportOutcome[] }[] {
  return CATEGORY_ORDER
    .map(category => ({ category, outcomes: items.filter(o => o.category === category) }))
    .filter(group => group.outcomes.length > 0)
}

export function latestBatchIds(batches: ImportCompletedNotification[]): Set<string> {
  const newest = Math.max(...batches.map(b => Date.parse(b.completedAt)))
  return new Set(batches.filter(b => newest - Date.parse(b.completedAt) <= LATEST_WINDOW_MS).map(b => b.importId))
}

export function reasonText(outcome: ImportOutcome, t: Translate): string | null {
  const code = outcome.errorCode
  if (!code) return null
  if (REASON_CODES.has(code)) return t(`settings.import.reason.${code}`)
  return code.startsWith('import_') ? t('settings.import.reason.unsupported') : outcome.error ?? t('settings.import.reason.unsupported')
}

export function outcomeDetail(outcome: ImportOutcome, t: Translate): { tone: StatusTone | null; text: string } | null {
  const tone: StatusTone | null = FAILED.has(outcome.status) ? 'error'
    : outcome.status === 'attention' ? 'warning'
      : outcome.status === 'imported' ? null : 'neutral'
  const text = reasonText(outcome, t)
    ?? (outcome.status === 'imported' ? null : t(`settings.import.status.${outcome.status}`))
  return text ? { tone, text } : null
}

export function outcomeActionKey(outcome: ImportOutcome): string | null {
  if (!OPENABLE.has(outcome.status)) return null
  if (outcome.category === 'sessions') return outcome.threadId ? 'settings.import.action.openChat' : null
  return ACTIONS[outcome.category] ?? null
}

export function openOutcome(outcome: ImportOutcome): void {
  const ui = useUIStore.getState()
  if (outcome.threadId) {
    useThreadStore.getState().setActiveThreadId(outcome.threadId)
    ui.setActiveMainView('conversation')
  } else if (outcome.category === 'skills' || outcome.category === 'plugins') {
    ui.setPluginCatalogSurface(outcome.category === 'plugins' ? 'plugins' : 'skills')
    ui.setActiveMainView('skills')
    if (outcome.category === 'plugins') {
      const pluginId = outcome.targetPath.split(/[\\/]/).at(-1) ?? ''
      void usePluginStore.getState().selectPlugin(pluginId)
    } else if (outcome.title) {
      void useSkillsStore.getState().selectSkill(outcome.title)
    }
  } else if (outcome.category === 'commands') {
    ui.setActiveMainView('conversation')
    ui.setComposerPrefill(`/${outcome.title ?? ''} `)
  } else {
    ui.setActiveSettingsTab(outcome.category === 'mcp' ? 'mcp' : outcome.category === 'hooks' ? 'hooks' : 'personalization')
  }
}
