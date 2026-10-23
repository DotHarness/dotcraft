import { memo } from 'react'
import { Clock } from 'lucide-react'
import { useT, useLocale } from '../../contexts/LocaleContext'
import type { AutomationDefinition } from '../../types/automation'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { automationScheduleSummary } from '../../utils/automationScheduleSummary'
import { Button } from '../ui/Button'

export function openAutomationTab(id: string, name: string): void {
  const threadId = useThreadStore.getState().activeThreadId
  if (!threadId) return
  const tabId = useViewerTabStore.getState().openAutomation({ threadId, automationId: id, label: name })
  useUIStore.getState().setActiveViewerTab(tabId)
}

export const AutomationCard = memo(function AutomationCard({ automation }: { automation: AutomationDefinition }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const schedule = automationScheduleSummary(automation.schedule, locale)
  const open = (): void => openAutomationTab(automation.id, automation.name)
  return (
    <div className="dc-automation-card">
      <button type="button" className="dc-automation-card__body" aria-label={`${t('common.open')} ${automation.name} · ${schedule}`} onClick={open}>
        <span className="dc-automation-card__icon" aria-hidden><Clock size={22} strokeWidth={1.8} /></span>
        <span className="dc-automation-card__text">
          <span className="dc-automation-card__title">{automation.name}</span>
          <span className="dc-automation-card__schedule">{schedule}</span>
        </span>
      </button>
      <Button variant="outlineGhost" size="toolbar" onClick={open} style={{ flexShrink: 0 }}>
        {t('common.open')}
      </Button>
    </div>
  )
})
