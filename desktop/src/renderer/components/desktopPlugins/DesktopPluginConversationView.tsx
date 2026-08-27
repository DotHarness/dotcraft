import { MessageSquare } from 'lucide-react'

import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  resolveDesktopPluginLabel,
  selectDesktopPluginConversationView,
  useDesktopPluginRegistry,
  type ActiveDesktopPluginConversationView
} from '../../plugins/desktopPluginRegistry'
import { resolveDesktopPluginIcon } from './DesktopPluginIcon'
import { DesktopPluginContributionBoundary } from './DesktopPluginContributionBoundary'
import styles from './DesktopPluginContributions.module.css'

export function DesktopPluginConversationTabs({ threadId }: { threadId: string }): JSX.Element | null {
  const locale = useLocale()
  const t = useT()
  const views = useDesktopPluginRegistry((state) => state.conversationViews)
  const selectedKey = useDesktopPluginRegistry((state) => state.conversationSelections.get(threadId) ?? null)
  if (views.length === 0) return null

  return (
    <div className={styles.conversationTabs} role="tablist" aria-label={t('desktopPlugins.conversationViews')}>
      <button
        type="button"
        role="tab"
        aria-selected={selectedKey == null}
        className={styles.conversationTab}
        onClick={() => selectDesktopPluginConversationView(threadId, null)}
      >
        <MessageSquare size={14} aria-hidden />
        {t('desktopPlugins.chat')}
      </button>
      {views.map((view) => {
        const Icon = resolveDesktopPluginIcon(view.icon)
        return (
          <button
            key={view.contributionKey}
            type="button"
            role="tab"
            aria-selected={selectedKey === view.contributionKey}
            className={styles.conversationTab}
            onClick={() => selectDesktopPluginConversationView(threadId, view.contributionKey)}
          >
            <Icon size={14} aria-hidden />
            {resolveDesktopPluginLabel(view.label, locale)}
          </button>
        )
      })}
    </div>
  )
}

export function DesktopPluginConversationViewOutlet({
  contribution,
  threadId
}: {
  contribution: ActiveDesktopPluginConversationView
  threadId: string
}): JSX.Element {
  const Component = contribution.component
  return (
    <div className={styles.conversationBody} role="tabpanel">
      <DesktopPluginContributionBoundary
        key={`${contribution.contributionKey}:${contribution.revision}`}
        identity={contribution.contributionKey}
      >
        <Component host={contribution.host} contributionId={contribution.id} threadId={threadId} />
      </DesktopPluginContributionBoundary>
    </div>
  )
}
