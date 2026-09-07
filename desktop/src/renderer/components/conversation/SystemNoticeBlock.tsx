import { Archive, ChevronsDown, GitFork } from 'lucide-react'
import { NoticeDivider } from './NoticeDivider'
import { useT } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'

interface SystemNoticeBlockProps {
  item: ConversationItem
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

