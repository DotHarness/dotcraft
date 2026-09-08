import { useUIStore } from '../../stores/uiStore'
import { useEffect, useState } from 'react'
import { useT, useLocale } from '../../contexts/LocaleContext'
import { useAutomationsStore } from '../../stores/automationsStore'
import { openAutomationRun } from '../../stores/automationRunNavigation'
import { Button } from '../ui/Button'
const emptyRuns: never[] = []
export function AutomationRunHistory({ automationId }: { automationId: string }): JSX.Element {
  const t = useT(), locale = useLocale()
  const runs = useAutomationsStore(s => s.runs[automationId] ?? emptyRuns)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { let active = true; void useAutomationsStore.getState().fetchRuns(automationId).catch(e => { if (active) setError(String(e)) }); return () => { active = false } }, [automationId])
  return <section className="dc-automation-section"><h3>{t('automation.history')}</h3>{error ? <p role="alert">{error}</p> : null}
    {runs.length === 0 ? <p className="dc-automation-hint">{t('automation.noRuns')}</p> : runs.map(run => <article className="dc-automation-run" key={run.id}>
      <div className="dc-automation-field"><span>{new Date(run.createdAt).toLocaleString(locale)}</span><span>{t(`automation.run.${run.status}`)}</span></div>
      {run.summary ? <p>{run.summary}</p> : null}{run.error ? <p className="dc-automation-error">{run.error}</p> : null}
      {run.deliveryStatus === 'failed' ? <p className="dc-automation-error">{t('automation.deliveryFailed')}: {run.deliveryError}</p> : null}
      {run.worktree ? <p className="dc-automation-hint">{run.worktree.branchName}</p> : null}
      {run.worktree ? <Button variant="ghost" disabled={!run.threadId || !run.turnId} onClick={() => { openAutomationRun(run); useUIStore.getState().setActiveDetailTab('changes') }}>{t('automation.reviewChanges')}</Button> : null}
      <Button variant="ghost" disabled={!run.threadId || !run.turnId} onClick={() => openAutomationRun(run)}>{t('automation.openRun')}</Button>
    </article>)}
  </section>
}
