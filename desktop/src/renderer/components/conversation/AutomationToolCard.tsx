import { useState } from 'react'
import { CalendarClock } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import type { AutomationDefinition, AutomationRun } from '../../types/automation'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useUIStore } from '../../stores/uiStore'
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
function AutomationReference({ id, name, disabled = false }: { id: string; name: string; disabled?: boolean }): JSX.Element {
  return <button type="button" className="dc-ref dc-ref-automation" disabled={disabled} aria-label={name} onClick={e => { e.preventDefault(); e.stopPropagation(); useAutomationsStore.getState().selectAutomation(id); useUIStore.getState().setActiveMainView('automations') }}><CalendarClock size={12} strokeWidth={2.25} aria-hidden /><span>{name}</span></button>
}
