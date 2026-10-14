import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { useEffect, useState, useSyncExternalStore, type JSX } from 'react'
import { stringsFor } from './i18n'
import { getSettings, subscribeSettings } from './settings'
import { SummaryPanel } from './SummaryPanel'
import { isPanelLayout, reportLayout } from './view'

export function SummaryAside({ host, context }: DesktopPluginSurfaceProps<'conversation.aside.trailing'>): JSX.Element | null {
  const settings = useSyncExternalStore(subscribeSettings, getSettings, getSettings)
  const shown = settings?.pinned === true && isPanelLayout(context.layout)
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
      aria-label={stringsFor(host.environment.locale).panelLabel}
      aria-hidden={!shown || undefined}
      inert={!shown}
      data-leaving={!shown || undefined}
      onTransitionEnd={(event) => {
        if (!shown && event.target === event.currentTarget && event.propertyName === 'opacity') {
          setRenderedThreadId(null)
        }
      }}
    >
      <SummaryPanel key={context.threadId} host={host} threadId={context.threadId} workspacePath={context.workspacePath} variant="pinned" />
    </aside>
  )
}
