import { useCallback, useEffect, useState } from 'react'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import { useBoardStore } from '../../stores/boardStore'
import { useThreadStore } from '../../stores/threadStore'
import { boardAgentName, isRootAgentPath } from '../../utils/boardToolDisplay'
import { findSubAgentChild } from '../../utils/subAgentIdentity'

type BoardTopicsResult = ClientRequestMethods['subagent/board/topics/list']['result']
type BoardTopicReadResult = ClientRequestMethods['subagent/board/topic/read']['result']
export type BoardTopic = NonNullable<BoardTopicsResult['data']>[number]

type Loaded<T> = { status: 'loading' } | { status: 'error' } | { status: 'ready'; value: T }

const TOPIC_LIMIT = 100

function useBoardRequest<T>(threadId: string, request: () => Promise<T>): Loaded<T> {
  const rootThreadId = useThreadStore((state) =>
    state.activeThread?.id === threadId ? state.activeThread.source?.subAgent?.rootThreadId ?? threadId : threadId)
  const revision = useBoardStore((state) => state.revisionByRoot[rootThreadId] ?? 0)
  const [state, setState] = useState<Loaded<T>>({ status: 'loading' })
  useEffect(() => {
    let cancelled = false
    request().then(
      (value) => { if (!cancelled) setState({ status: 'ready', value }) },
      () => { if (!cancelled) setState((current) => (current.status === 'ready' ? current : { status: 'error' })) }
    )
    return () => { cancelled = true }
  }, [request, revision])
  return state
}

function listTopics(threadId: string, cursor?: string | null): Promise<BoardTopicsResult> {
  return window.api.appServer.sendRequest('subagent/board/topics/list', {
    threadId,
    limit: TOPIC_LIMIT,
    ...(cursor ? { cursor } : {})
  }) as Promise<BoardTopicsResult>
}

export function useBoardTopics(threadId: string): Loaded<BoardTopicsResult> {
  const request = useCallback(async () => {
    const first = await listTopics(threadId)
    const data = [...(first.data ?? [])]
    let cursor = first.nextCursor
    while (cursor) {
      const page = await listTopics(threadId, cursor)
      data.push(...(page.data ?? []))
      cursor = page.nextCursor
    }
    return { ...first, data, nextCursor: null }
  }, [threadId])
  return useBoardRequest(threadId, request)
}

export function useBoardTopic(threadId: string, topicId: string): Loaded<BoardTopicReadResult> {
  const request = useCallback(
    () => window.api.appServer.sendRequest('subagent/board/topic/read', { threadId, topicId }) as Promise<BoardTopicReadResult>,
    [threadId, topicId]
  )
  return useBoardRequest(threadId, request)
}

export function useBoardAgentName(threadId: string): (path: string | undefined) => string {
  const t = useT()
  const locale = useLocale()
  const { lookup } = useSubAgentLookup(threadId)
  return (path) => {
    if (!path || isRootAgentPath(path)) return t('boardPanel.mainAgent')
    return findSubAgentChild(lookup, null, path, 'tree')?.nickname ?? boardAgentName(path, locale)
  }
}
