import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { useEffect, useSyncExternalStore, type JSX } from 'react'
import { stringsFor } from './i18n'
import { getSettings, subscribeSettings } from './settings'
import { SummaryPanel } from './SummaryPanel'
import { isPanelLayout, reportLayout } from './view'

export function SummaryAside({ host, context }: DesktopPluginSurfaceProps<'conversation.aside.trailing'>): JSX.Element | null {
  const settings = useSyncExternalStore(subscribeSettings, getSettings, getSettings)
  const shown = settings?.pinned === true && isPanelLayout(context.layout)
  const { pin } = context

  useEffect(() => {
    reportLayout(context.layout)
  }, [context.layout])

  useEffect(() => () => reportLayout(null), [])

  useEffect(() => {
    if (!shown) return
    return pin()
  }, [shown, pin])

  if (!shown) return null

  return (
    <aside className="conversation-summary-pinned" aria-label={stringsFor(host.environment.locale).panelLabel}>
      <SummaryPanel key={context.threadId} host={host} threadId={context.threadId} workspacePath={context.workspacePath} variant="pinned" />
    </aside>
  )
}
