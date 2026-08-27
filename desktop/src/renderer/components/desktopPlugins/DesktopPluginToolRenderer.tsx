import type { DesktopPluginToolPresentationModel } from '@dotcraft/plugin'
import type { ReactNode } from 'react'

import type { ActiveDesktopPluginToolRenderer } from '../../plugins/desktopPluginRegistry'
import type { ConversationItem } from '../../types/conversation'
import { DesktopPluginContributionBoundary } from './DesktopPluginContributionBoundary'
import styles from './DesktopPluginContributions.module.css'

export function DesktopPluginToolRendererOutlet({
  contribution,
  item,
  threadId,
  turnId,
  running,
  fallback
}: {
  contribution: ActiveDesktopPluginToolRenderer
  item: ConversationItem
  threadId: string | null
  turnId: string
  running: boolean
  fallback?: ReactNode
}): JSX.Element {
  const Component = contribution.component
  const presentation: DesktopPluginToolPresentationModel = {
    id: item.id,
    threadId,
    turnId,
    presentationId: item.presentation!.presentationId,
    options: item.presentation?.options ?? {},
    toolName: item.toolName ?? 'tool',
    status: running ? 'running' : 'completed',
    arguments: item.arguments,
    result: item.result ?? item.errorMessage ?? item.resultPreview,
    success: item.success,
    createdAt: item.createdAt,
    completedAt: item.completedAt
  }
  return (
    <div className={styles.toolRenderer} data-desktop-plugin-tool-renderer={contribution.contributionKey}>
      <DesktopPluginContributionBoundary
        key={`${contribution.contributionKey}:${contribution.revision}`}
        identity={contribution.contributionKey}
        fallback={fallback}
      >
        <Component
          host={contribution.host}
          contributionId={contribution.id}
          presentation={presentation}
        />
      </DesktopPluginContributionBoundary>
    </div>
  )
}
