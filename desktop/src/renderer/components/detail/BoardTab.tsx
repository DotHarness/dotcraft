import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { MessagesSquare } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useBoardStore } from '../../stores/boardStore'
import { useThreadStore } from '../../stores/threadStore'
import { firstLine } from '../../utils/boardToolDisplay'
import { formatRelativeTime } from '../../utils/relativeTime'
import { RobotAvatar } from '../agents/RobotAvatar'
import { SkeletonList } from '../ui/Skeleton'
import { BoardTopicView, type BoardHighlight } from './BoardTopicView'
import { useBoardAgentName, useBoardTopics, type BoardTopic } from './boardData'
import styles from './BoardTab.module.css'

const SPLIT_MIN_WIDTH = 600

export function BoardTab(): JSX.Element {
  const threadId = useThreadStore((state) => state.activeThreadId)
  if (!threadId) return <BoardEmpty />
  return <Board key={threadId} threadId={threadId} />
}

function Board({ threadId }: { threadId: string }): JSX.Element {
  const t = useT()
  const topics = useBoardTopics(threadId)
  const focus = useBoardStore((state) => (state.focus?.threadId === threadId ? state.focus : null))
  const [channel, setChannel] = useState<string | null>(null)
  const [openTopicId, setOpenTopicId] = useState<string | null>(null)
  const [highlight, setHighlight] = useState<BoardHighlight | null>(null)
  const { ref, width } = useWidth<HTMLDivElement>()
  const split = width >= SPLIT_MIN_WIDTH

  useEffect(() => {
    if (!focus) return
    setChannel(null)
    setOpenTopicId(focus.topicId)
    setHighlight(focus.messageId ? { messageId: focus.messageId, key: focus.key } : null)
    useBoardStore.getState().clearFocus(focus.key)
  }, [focus])

  const all = topics.status === 'ready' ? topics.value.data ?? [] : []
  const visible = useMemo(() => (channel ? all.filter((topic) => topic.channelName === channel) : all), [all, channel])
  const selectedId = openTopicId ?? (split ? visible[0]?.topicId ?? null : null)

  let body: JSX.Element
  if (topics.status === 'loading' && !openTopicId) {
    body = <div className={styles.scroll}><SkeletonList count={4} ariaLabel={t('boardPanel.loading')} /></div>
  } else if (topics.status === 'error' && !openTopicId) {
    body = <p className={styles.notice}>{t('boardPanel.loadFailed')}</p>
  } else if (all.length === 0 && !openTopicId) {
    body = <BoardEmpty />
  } else {
    const list = (
      <div className={styles.listPane}>
        <div className={styles.filters} role="toolbar" aria-label={t('boardPanel.channelsAria')}>
          <FilterChip label={t('boardPanel.all')} count={all.length} active={channel === null} onClick={() => setChannel(null)} />
          {(topics.status === 'ready' ? topics.value.channels ?? [] : []).map((entry) => (
            <FilterChip
              key={entry.name}
              label={entry.name ?? ''}
              count={entry.topicCount ?? 0}
              active={channel === entry.name}
              onClick={() => setChannel(entry.name ?? null)}
            />
          ))}
        </div>
        <div className={`${styles.scroll} dc-scrollbar-stable`}>
          <div className={styles.rows}>
            {visible.map((topic) => (
              <TopicRow
                key={topic.topicId}
                threadId={threadId}
                topic={topic}
                compact={split}
                showChannel={channel === null}
                selected={split && topic.topicId === selectedId}
                onOpen={(id) => {
                  setOpenTopicId(id)
                  setHighlight(null)
                }}
              />
            ))}
          </div>
        </div>
      </div>
    )
    const topic = selectedId
      ? (
        <BoardTopicView
          key={selectedId}
          threadId={threadId}
          topicId={selectedId}
          highlight={highlight}
          onBack={split ? undefined : () => setOpenTopicId(null)}
        />
      )
      : null
    body = split
      ? <>{list}<div className={styles.topicPane}>{topic}</div></>
      : topic ?? list
  }

  return <div ref={ref} className={styles.body} data-split={split || undefined}>{body}</div>
}

function TopicRow({
  threadId,
  topic,
  compact,
  showChannel,
  selected,
  onOpen
}: {
  threadId: string
  topic: BoardTopic
  compact: boolean
  showChannel: boolean
  selected: boolean
  onOpen: (topicId: string) => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const agentName = useBoardAgentName(threadId)
  const author = agentName(topic.rootPost?.author)
  const replies = topic.replyCount ?? 0

  return (
    <button
      type="button"
      className={styles.topic}
      data-compact={compact || undefined}
      aria-current={selected || undefined}
      onClick={() => topic.topicId && onOpen(topic.topicId)}
    >
      {!compact && <RobotAvatar name={author} size={32} />}
      <span className={styles.topicBody}>
        <span className={styles.topicLine}>
          <span className={styles.topicTitle}>{firstLine(topic.rootPost?.text ?? '', 200)}</span>
          {topic.lastActivityAt && <span className={styles.time}>{formatRelativeTime(topic.lastActivityAt, new Date(), locale)}</span>}
        </span>
        <span className={styles.topicMeta}>
          {showChannel && <><span className={styles.channel}>{topic.channelName}</span><span aria-hidden>·</span></>}
          <span>{author}</span>
          <span aria-hidden>·</span>
          <span>{replies ? t(`toolCall.board.replies.${replies === 1 ? 'one' : 'other'}`, { count: replies }) : t('toolCall.board.noReplies')}</span>
        </span>
      </span>
    </button>
  )
}

function FilterChip({ label, count, active, onClick }: {
  label: string
  count: number
  active: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <button type="button" className={styles.chip} aria-pressed={active} onClick={onClick}>
      {label}
      <span className={styles.count}>{count}</span>
    </button>
  )
}

function BoardEmpty(): JSX.Element {
  const t = useT()
  return (
    <div className={styles.empty}>
      <MessagesSquare size={20} strokeWidth={1.6} aria-hidden />
      <p className={styles.emptyTitle}>{t('boardPanel.empty')}</p>
      <p>{t('boardPanel.emptyHint')}</p>
    </div>
  )
}

function useWidth<T extends HTMLElement>(): { ref: React.RefObject<T | null>; width: number } {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    setWidth(element.clientWidth)
    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  return { ref, width }
}
