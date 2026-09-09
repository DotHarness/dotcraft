import { useEffect, useState } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { AutomationDefinition } from '../../types/automation'
import { automationScheduleSummary } from '../../utils/automationScheduleSummary'
import { automationNextRun } from '../../utils/automationNextRun'

export function AutomationTaskTiming({ automation, running }: { automation: AutomationDefinition; running: boolean }): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const [now, setNow] = useState(Date.now)
  useEffect(() => {
    setNow(Date.now())
    if (automation.status !== 'active' || running || !automation.nextRunAt) return
    const tick = (): void => setNow(Date.now())
    const timer = setInterval(tick, 60000)
    document.addEventListener('visibilitychange', tick)
    return () => { clearInterval(timer); document.removeEventListener('visibilitychange', tick) }
  }, [automation.status, automation.nextRunAt, running])
  const next = automation.status === 'active' && !running ? automationNextRun(automation.nextRunAt, now, locale) : null
  const schedule = automation.status === 'completed' ? t('automation.status.completed')
    : automationScheduleSummary(automation.schedule, locale, { includeTimeZone: false })
  return <small title={next && automation.nextRunAt ? new Date(automation.nextRunAt).toLocaleString(locale) : undefined}>
    {schedule}{running ? ` · ${t('automation.run.running')}` : next ? ` · ${next}` : ''}
  </small>
}
