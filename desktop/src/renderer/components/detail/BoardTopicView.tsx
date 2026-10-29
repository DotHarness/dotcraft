import { useEffect, useRef } from 'react'
import { ChevronLeft } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import { RobotAvatar } from '../agents/RobotAvatar'
import { IconButton } from '../ui/IconButton'
import { SkeletonList } from '../ui/Skeleton'
import { useBoardAgentName, useBoardTopic } from './boardData'
import styles from './BoardTab.module.css'

export interface BoardHighlight {
  messageId: string
  key: number
}

export function BoardTopicView({
  threadId,
  topicId,
  highlight,
  onBack
}: {
  threadId: string
  topicId: string
  highlight: BoardHighlight | null
  onBack?: () => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const read = useBoardTopic(threadId, topicId)
  const agentName = useBoardAgentName(threadId)
  const postsRef = useRef<HTMLOListElement>(null)
  const ready = read.status === 'ready'

  useEffect(() => {
    if (!ready || !highlight) return
    postsRef.current
      ?.querySelector(`[data-message-id="${CSS.escape(highlight.messageId)}"]`)
      ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [ready, highlight])

  const topic = ready ? read.value.topic : null
  const replies = topic?.replyCount ?? 0

  return (
    <div className={styles.topicView}>
      <div className={styles.topicHeader}>
        {onBack && (
          <IconButton
            size={24}
            icon={<ChevronLeft size={16} strokeWidth={1.8} aria-hidden />}
            label={t('boardPanel.back')}
            tooltipLabel={t('boardPanel.back')}
            onClick={onBack}
          />
        )}
        {topic && (
          <>
            <span className={styles.topicChannel}>{topic.channelName}</span>
            <span className={styles.topicReplies}>
              · {replies ? t(`toolCall.board.replies.${replies === 1 ? 'one' : 'other'}`, { count: replies }) : t('toolCall.board.noReplies')}
            </span>
          </>
        )}
      </div>
      <div className={`${styles.scroll} dc-scrollbar-stable`}>
        {read.status === 'loading' && <SkeletonList count={3} ariaLabel={t('boardPanel.loading')} />}
        {read.status === 'error' && <p className={styles.notice}>{t('boardPanel.loadFailed')}</p>}
        {ready && !topic && <p className={styles.notice}>{t('boardPanel.topicUnavailable')}</p>}
        {ready && topic && (
          <ol ref={postsRef} className={styles.posts}>
            {(read.value.posts ?? []).map((post) => {
              const author = agentName(post.author)
              const markKey = highlight && highlight.messageId === post.messageId ? highlight.key : null
              return (
                <li
                  key={markKey == null ? post.messageId : `${post.messageId}:${markKey}`}
                  className={styles.post}
                  data-message-id={post.messageId}
                  data-highlight={markKey != null || undefined}
                >
                  <div className={styles.postHead}>
                    <RobotAvatar name={author} size={20} />
                    <span className={styles.postAuthor}>{author}</span>
                    {(post.notified?.length ?? 0) > 0 && (
                      <span className={styles.mentions}>{post.notified!.map((path) => `@${agentName(path)}`).join(' ')}</span>
                    )}
                    {post.createdAt && <span className={`${styles.time} ${styles.postTime}`}>{formatRelativeTime(post.createdAt, new Date(), locale)}</span>}
                  </div>
                  <p className={`${styles.postText} selectable`}>{post.text}</p>
                </li>
              )
            })}
          </ol>
        )}
        {ready && read.value.hasMore && <p className={styles.notice}>{t('boardPanel.truncated')}</p>}
      </div>
    </div>
  )
}
