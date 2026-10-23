import type { DesktopPluginConversationAsideContext } from '@dotcraft/plugin'
import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { SummaryPanel } from './SummaryPanel'
import { desktopSummaryServices, type SummaryServices } from './summaryServices'
import { isPanelLayout, reportLayout, useSummaryView } from './view'

export function SummaryAside({
  context,
  services = desktopSummaryServices
}: {
  context: DesktopPluginConversationAsideContext
  services?: SummaryServices
}): JSX.Element | null {
  const t = useT()
  const { pinned } = useSummaryView()
  const shown = pinned && isPanelLayout(context.layout)
  const [renderedThreadId, setRenderedThreadId] = useState(shown ? context.threadId : null)
  if (shown && renderedThreadId !== context.threadId) setRenderedThreadId(context.threadId)
  const { pin } = context

  useEffect(() => {
    reportLayout(context.layout)
  }, [context.layout])

  useEffect(() => () => reportLayout(null), [])

  useEffect(() => {
    if (!shown) return
    return pin()
  }, [shown, pin])

  useEffect(() => {
    if (shown || renderedThreadId === null) return
    const timer = setTimeout(() => setRenderedThreadId(null), 240)
    return () => clearTimeout(timer)
  }, [shown, renderedThreadId])

  if (renderedThreadId !== context.threadId) return null

  return (
    <aside
      className="conversation-summary-pinned"
      aria-label={t('summary.panelLabel')}
      aria-hidden={!shown || undefined}
      inert={!shown}
      data-leaving={!shown || undefined}
      onTransitionEnd={(event) => {
        if (!shown && event.target === event.currentTarget && event.propertyName === 'opacity') {
          setRenderedThreadId(null)
        }
      }}
    >
      <SummaryPanel key={context.threadId} services={services} threadId={context.threadId} workspacePath={context.workspacePath} variant="pinned" />
    </aside>
  )
}
