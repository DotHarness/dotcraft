import type { ReactNode } from 'react'
import { useLocale } from '../../contexts/LocaleContext'
import { translate, type AppLocale } from '../../../shared/locales'
import { firstLine, type BoardCall, type BoardPost, type BoardTopic, type BoardChannel } from '../../utils/boardToolDisplay'
import { BoardAgentName, type BoardAgentContext } from './BoardAgentName'
import styles from './BoardToolCard.module.css'

export function hasBoardToolDetail(call: BoardCall): boolean {
  if (call.phase !== 'completed') return false
  switch (call.operation) {
    case 'readPost': return call.post != null
    case 'readThread':
    case 'searchPosts': return call.posts.length > 0
    case 'listThreads': return call.topics.length > 0
    case 'getChannels': return call.channels.length > 0
    default: return false
  }
}

export function BoardToolDetail({ call, context }: { call: BoardCall; context: BoardAgentContext }): JSX.Element | null {
  switch (call.operation) {
    case 'readPost':
      return call.post ? <PostFeed posts={[call.post]} context={context} /> : null
    case 'readThread':
      return <PostFeed posts={call.posts} context={context} threaded />
    case 'searchPosts':
      return <PostFeed posts={call.posts} context={context} highlight={call.query} showChannel />
    case 'listThreads':
      return <TopicList topics={call.topics} hasMore={call.hasMore} context={context} />
    case 'getChannels':
      return <ChannelList channels={call.channels} />
    default:
      return null
  }
}

function PostFeed({
  posts,
  context,
  threaded = false,
  highlight,
  showChannel = false
}: {
  posts: BoardPost[]
  context: BoardAgentContext
  threaded?: boolean
  highlight?: string | null
  showChannel?: boolean
}): JSX.Element {
  const locale = useLocale()
  return (
    <div className={`dc-tool-panel-surface ${styles.feed}`} data-padded="true">
      {posts.map((post) => (
        <article key={post.messageId} className={styles.entry} data-reply={threaded && !post.topic ? 'true' : undefined}>
          <header className={styles.entryHeader}>
            <BoardAgentName path={post.authorPath} context={context} mark />
            {showChannel && post.channel && <span className={styles.meta}>#{post.channel}</span>}
            {post.createdAt && (
              <time className={styles.meta} dateTime={post.createdAt}>{formatTime(post.createdAt, locale)}</time>
            )}
          </header>
          <p className={`${styles.text} selectable`}>{highlightMatches(post.text, highlight)}</p>
          {post.hiddenChars > 0 && (
            <p className={styles.footnote}>
              {translate(locale, `toolCall.board.moreChars.${post.hiddenChars === 1 ? 'one' : 'other'}`, {
                count: post.hiddenChars.toLocaleString(locale)
              })}
            </p>
          )}
        </article>
      ))}
    </div>
  )
}

function TopicList({ topics, hasMore, context }: { topics: BoardTopic[]; hasMore: boolean; context: BoardAgentContext }): JSX.Element {
  const locale = useLocale()
  return (
    <div className="dc-tool-panel-surface" data-padded="true">
      <ul className={styles.list}>
        {topics.map((topic) => (
          <li key={topic.threadId}>
            <span className={styles.listTitle}>{firstLine(topic.root.text, 96)}</span>
            <span className={styles.meta}>
              <BoardAgentName path={topic.root.authorPath} context={context} />
              {' · '}
              {topic.replyCount
                ? translate(locale, `toolCall.board.replies.${topic.replyCount === 1 ? 'one' : 'other'}`, { count: topic.replyCount })
                : translate(locale, 'toolCall.board.noReplies')}
            </span>
          </li>
        ))}
      </ul>
      {hasMore && <p className={styles.footnote}>{translate(locale, 'toolCall.board.moreTopics')}</p>}
    </div>
  )
}

function ChannelList({ channels }: { channels: BoardChannel[] }): JSX.Element {
  const locale = useLocale()
  return (
    <div className="dc-tool-panel-surface" data-padded="true">
      <ul className={styles.list}>
        {channels.map((channel) => (
          <li key={channel.name}>
            <span className={styles.listTitle}>#{channel.name}</span>
            <span className={styles.meta}>
              {channel.description ? `${channel.description} · ` : ''}
              {channel.messageCount
                ? translate(locale, `toolCall.board.postCount.${channel.messageCount === 1 ? 'one' : 'other'}`, { count: channel.messageCount })
                : translate(locale, 'toolCall.board.noPostsYet')}
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function formatTime(iso: string, locale: AppLocale): string {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })
}

function highlightMatches(text: string, query: string | null | undefined): ReactNode {
  if (!query) return text
  const lower = text.toLocaleLowerCase()
  const needle = query.toLocaleLowerCase()
  const parts: ReactNode[] = []
  let from = 0
  for (let index = lower.indexOf(needle); index >= 0 && needle; index = lower.indexOf(needle, from)) {
    parts.push(text.slice(from, index), <mark key={index} className={styles.match}>{text.slice(index, index + needle.length)}</mark>)
    from = index + needle.length
  }
  parts.push(text.slice(from))
  return parts
}
