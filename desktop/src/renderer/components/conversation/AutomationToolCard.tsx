import { useState } from 'react'
import { CalendarClock, Clock } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import type { AutomationDefinition, AutomationRun } from '../../types/automation'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { automationScheduleSummary } from '../../utils/automationScheduleSummary'
import { ToolDisclosure } from './ToolDisclosure'
const operations = new Set(['create','update','pause','resume','delete','run','read','list','report'])
interface AutomationResult { operation: string; automation?: AutomationDefinition; run?: AutomationRun; automations?: AutomationDefinition[] }
function isDefinition(value: unknown): value is AutomationDefinition {
  if (!value || typeof value !== 'object') return false
  const a = value as Partial<AutomationDefinition>
  return typeof a.id === 'string' && typeof a.name === 'string' && typeof a.prompt === 'string' && !!a.schedule && typeof a.schedule.kind === 'string'
}
export function parseAutomationResult(result: unknown): AutomationResult | null {
  try {
    const value = typeof result === 'string' ? JSON.parse(result) : result
    if (!value || typeof value !== 'object' || !operations.has(value.operation)) return null
    if (value.automation != null && !isDefinition(value.automation)) return null
    if (value.automations != null && (!Array.isArray(value.automations) || !value.automations.every(isDefinition))) return null
    if (value.run != null && (typeof value.run.id !== 'string' || typeof value.run.automationId !== 'string' || typeof value.run.status !== 'string')) return null
    return value
  } catch { return null }
}
export function AutomationToolCard({ item, locale }: { item: ConversationItem; locale: AppLocale }): JSX.Element | null {
  const [expanded, setExpanded] = useState(false)
  const parsed = parseAutomationResult(item.result)
  if (!parsed) return null
  const { automation, run, operation, automations } = parsed
  if (automation && (operation === 'create' || operation === 'update')) return <AutomationCard automation={automation} locale={locale} />
  const id = automation?.id ?? run?.automationId
  const label = translate(locale, `automation.operation.${operation}`)
  const summary = automation ? automationScheduleSummary(automation.schedule, locale) : null
  return <ToolDisclosure expanded={expanded} onToggle={() => setExpanded(value => !value)} expandable={!!automation || !!run || !!automations?.length} title={<>{label}{id ? <> <AutomationReference id={id} name={automation?.name ?? translate(locale, 'automation.title')} disabled={operation === 'delete'} /></> : null}{automations ? <span> · {automations.length}</span> : null}</>}>
    <div className="dc-automation-tool-details">
      {automation ? <><span>{summary}</span>{automation.nextRunAt ? <span>{translate(locale, 'automation.nextRun')}: <span className="dc-automation-tool-details__value">{new Date(automation.nextRunAt).toLocaleString(locale)}</span></span> : null}<span>{translate(locale, `automation.status.${automation.status}`)} · {translate(locale, automation.executionMode === 'thread' ? 'automation.thread' : 'automation.independent')}</span><span>{translate(locale, 'automation.notifications')}: {translate(locale, `automation.notify.${automation.notificationPolicy}`)}</span></> : null}
      {run ? <span>{translate(locale, `automation.run.${run.status}`)}</span> : null}
      {automations?.map(a => <div key={a.id}><AutomationReference id={a.id} name={a.name} /> · {automationScheduleSummary(a.schedule, locale)}</div>)}
    </div>
  </ToolDisclosure>
}
function AutomationCard({ automation, locale }: { automation: AutomationDefinition; locale: AppLocale }): JSX.Element {
  const schedule = automationScheduleSummary(automation.schedule, locale)
  return <button type="button" className="dc-automation-card" aria-label={`${translate(locale, 'common.open')} ${automation.name} · ${schedule}`} onClick={() => openAutomation(automation.id, automation.name)}>
    <span className="dc-automation-card__icon" aria-hidden><Clock size={20} strokeWidth={1.8} /></span>
    <span className="dc-automation-card__text">
      <span className="dc-automation-card__title">{automation.name}</span>
      <span className="dc-automation-card__schedule">{schedule}</span>
    </span>
    <span className="dc-automation-card__open" aria-hidden>{translate(locale, 'common.open')}</span>
  </button>
}
function openAutomation(id: string, name: string): void {
  const threadId = useThreadStore.getState().activeThreadId
  if (!threadId) return
  const tabId = useViewerTabStore.getState().openAutomation({ threadId, automationId: id, label: name })
  useUIStore.getState().setActiveViewerTab(tabId)
}
function AutomationReference({ id, name, disabled = false }: { id: string; name: string; disabled?: boolean }): JSX.Element {
  return <button type="button" className="dc-ref dc-ref-automation" disabled={disabled} aria-label={name} onClick={e => { e.preventDefault(); e.stopPropagation(); openAutomation(id, name) }}><CalendarClock size={12} strokeWidth={2.25} aria-hidden /><span>{name}</span></button>
}
