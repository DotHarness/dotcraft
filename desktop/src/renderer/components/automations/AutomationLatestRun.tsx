import { useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAutomationsStore } from '../../stores/automationsStore'
export function AutomationLatestRun({ automationId }: { automationId: string }): JSX.Element | null {
  const latest = useAutomationsStore(s => s.runs[automationId]?.[0])
  const t = useT()
  useEffect(() => { void useAutomationsStore.getState().fetchRuns(automationId).catch(() => {}) }, [automationId])
  if (!latest) return null
  return <span className={latest.status === 'failed' ? 'dc-automation-error' : undefined}>{t(`automation.run.${latest.status}`)}{latest.error ? ` · ${latest.error}` : latest.summary ? ` · ${latest.summary}` : ''}</span>
}
