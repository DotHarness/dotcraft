import { Archive, ChevronsDown, GitFork, Monitor, PlugZap, Unplug } from 'lucide-react'
import { NoticeDivider } from './NoticeDivider'
import { useT } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'

interface SystemNoticeBlockProps {
  item: ConversationItem
}

/**
 * Only the user asking for This PC reads as a move; a lost lease reads as a loss,
 * which is why the agent's own disconnect shares the loss wording.
 */
function remoteRouteDivider(
  reason: string | undefined,
  initiator: string | undefined
): { key: string; icon: typeof PlugZap } | null {
  if (reason === 'connected') return { key: 'systemNotice.remoteRoute.connected', icon: PlugZap }
  if (reason === 'disconnected' && initiator === 'client') {
    return { key: 'systemNotice.remoteRoute.local', icon: Monitor }
  }
  if (reason === 'disconnected' || reason === 'leaseLost') {
    return { key: 'systemNotice.remoteRoute.disconnected', icon: Unplug }
  }
  return null
}

/**
 * Inline divider for persisted maintenance events. Unknown notice kinds render
 * nothing, since the wire protocol reserves `kind` as an open string.
 */
export function SystemNoticeBlock({ item }: SystemNoticeBlockProps): JSX.Element | null {
  const t = useT()
  const notice = item.systemNotice
  if (!notice) return null

  if (notice.kind === 'forked') {
    return (
      <NoticeDivider
        ariaLabel={t('systemNotice.forked.title')}
        icon={<GitFork size={12} aria-hidden />}
        title={t('systemNotice.forked.title')}
      />
    )
  }

  if (notice.kind === 'memoryConsolidated') {
    return (
      <NoticeDivider
        ariaLabel={t('systemNotice.memoryConsolidated.title')}
        icon={<Archive size={12} aria-hidden />}
        title={t('systemNotice.memoryConsolidated.updated')}
      />
    )
  }

  if (notice.kind === 'remoteRoute') {
    const route = remoteRouteDivider(notice.reason, notice.initiator)
    if (!route) return null
    const title = t(route.key, { host: notice.hostName ?? notice.hostId ?? '' })
    return <NoticeDivider ariaLabel={title} icon={<route.icon size={12} aria-hidden />} title={title} />
  }

  if (notice.kind !== 'compacted' || notice.mode === 'micro') return null

  const titleKey =
    notice.trigger === 'auto'
      ? 'systemNotice.compacted.auto'
      : notice.trigger === 'manual'
        ? 'systemNotice.compacted.manual'
        : 'systemNotice.compacted.reactive'

  return (
    <NoticeDivider
      ariaLabel={t(titleKey)}
      icon={<ChevronsDown size={12} aria-hidden />}
      title={t(titleKey)}
    />
  )
}

