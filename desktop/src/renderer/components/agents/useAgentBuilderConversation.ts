/**
 * Agent Builder — conversational session lifecycle.
 *
 * Drives the hidden "builder thread" that backs the conversational profile-builder
 * (specs/agents/agent-profiles.md §12A). Because the renderer's conversation view and Composer are
 * singletons bound to the one active thread, the builder thread becomes the app's active thread while
 * the chat pane is open; the previous active thread is restored on close. The hook also listens to the
 * raw notification stream and surfaces each builder tool's result (the fine-grained change descriptor)
 * so the editor can apply it to the live draft and highlight the field — see agentBuilderDraftSync.ts.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useThreadStore } from '../../stores/threadStore'
import { useConversationStore } from '../../stores/conversationStore'
import type { ThreadSummary } from '../../types/thread'
import type { InputPart } from '../../types/conversation'
import { isBuilderToolName, parseBuilderToolResult, type BuilderToolResult } from './agentBuilderDraftSync'

export type BuilderConversationStatus = 'idle' | 'starting' | 'ready' | 'error'

interface Options {
  /** When false, tear down and restore the previously active thread if a builder thread was started. */
  active: boolean
  /** Called with each builder tool's parsed result, in stream order. */
  onResult: (result: BuilderToolResult) => void
}

interface StartBuilderConversationOptions {
  /** The profile id the conversation edits (a placeholder is fine for a not-yet-created agent). */
  targetId: string
  /** Source used to seed the profile being edited (builtIn / plugin / user / workspace). */
  targetSource: string
  /** Current editor markdown to seed into the server draft before the first builder turn. */
  initialDraftMarkdown: string
  /** Opening user input for the first builder turn. */
  inputParts: InputPart[]
  /** Optional pre-thread model/reasoning config selected in the detached composer. */
  config?: Record<string, unknown>
}

interface Result {
  threadId: string | null
  status: BuilderConversationStatus
  error: string | null
  start: (options: StartBuilderConversationOptions) => Promise<void>
  syncDraft: (rawContent: string) => Promise<void>
}

async function rpc<T>(method: string, params: Record<string, unknown>): Promise<T> {
  return (await window.api.appServer.sendRequest(method, params)) as T
}

export function useAgentBuilderConversation({
  active,
  onResult
}: Options): Result {
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const [threadId, setThreadId] = useState<string | null>(null)
  const [status, setStatus] = useState<BuilderConversationStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const previousActiveThreadIdRef = useRef<string | null>(null)
  const threadIdRef = useRef<string | null>(null)
  const startPromiseRef = useRef<Promise<void> | null>(null)
  const startTokenRef = useRef(0)
  const onResultRef = useRef(onResult)
  onResultRef.current = onResult

  const setCurrentThreadId = useCallback((nextThreadId: string | null): void => {
    threadIdRef.current = nextThreadId
    setThreadId(nextThreadId)
  }, [])

  useEffect(() => {
    if (active) return undefined

    startTokenRef.current += 1
    if (threadIdRef.current) {
      useThreadStore.getState().setActiveThreadId(previousActiveThreadIdRef.current)
      useThreadStore.getState().setActiveThread(null)
    }
    setCurrentThreadId(null)
    setStatus('idle')
    setError(null)
    startPromiseRef.current = null
    return undefined
  }, [active, setCurrentThreadId])

  useEffect(() => () => {
    if (threadIdRef.current) {
      useThreadStore.getState().setActiveThreadId(previousActiveThreadIdRef.current)
      useThreadStore.getState().setActiveThread(null)
    }
  }, [])

  const start = useCallback(async ({
    targetId,
    targetSource,
    initialDraftMarkdown,
    inputParts,
    config = {}
  }: StartBuilderConversationOptions): Promise<void> => {
    if (threadIdRef.current) return
    if (startPromiseRef.current) {
      await startPromiseRef.current
      return
    }

    const token = ++startTokenRef.current
    const promise = (async () => {
      setStatus('starting')
      setError(null)
      try {
        previousActiveThreadIdRef.current = useThreadStore.getState().activeThreadId
        const startConfig: Record<string, unknown> = {
          ...config,
          agentBuilderTargetId: targetId.trim() || 'draft-agent',
          agentBuilderTargetSource: targetSource.trim() || 'workspace'
        }
        const res = await rpc<{ thread: ThreadSummary }>('thread/start', {
          identity: {
            channelName: 'dotcraft-desktop',
            userId: 'local',
            channelContext: `workspace:${workspacePath}`,
            workspacePath
          },
          config: startConfig,
          historyMode: 'server'
        })
        if (token !== startTokenRef.current) return

        const newThreadId = res.thread.id
        await rpc('agent/profiles/builderDraft/update', {
          threadId: newThreadId,
          rawContent: initialDraftMarkdown
        })
        if (token !== startTokenRef.current) return

        // Don't add the builder thread to the local thread list — it's an internal thread (hidden from
        // the sidebar; the backend also excludes it from thread/list). Selecting it triggers App's
        // active-thread effect, which loads it and subscribes for streaming.
        useThreadStore.getState().setActiveThreadId(newThreadId)
        useThreadStore.getState().setActiveThread(null)
        setCurrentThreadId(newThreadId)

        await rpc('turn/start', {
          threadId: newThreadId,
          input: inputParts,
          identity: {
            channelName: 'dotcraft-desktop',
            userId: 'local',
            channelContext: `workspace:${workspacePath}`,
            workspacePath
          }
        })
        if (token !== startTokenRef.current) return

        setStatus('ready')
      } catch (err) {
        if (threadIdRef.current) {
          useThreadStore.getState().setActiveThreadId(previousActiveThreadIdRef.current)
          useThreadStore.getState().setActiveThread(null)
          setCurrentThreadId(null)
        }
        setStatus('error')
        setError(err instanceof Error ? err.message : String(err))
        throw err
      }
    })()

    startPromiseRef.current = promise
    try {
      await promise
    } finally {
      if (startPromiseRef.current === promise) {
        startPromiseRef.current = null
      }
    }
  }, [setCurrentThreadId, workspacePath])

  const syncDraft = useCallback(async (rawContent: string): Promise<void> => {
    if (!threadId) return
    await rpc('agent/profiles/builderDraft/update', {
      threadId,
      rawContent
    })
  }, [threadId])

  // Surface builder tool results off the notification stream (item/completed for a builder tool call).
  useEffect(() => {
    if (!threadId) return undefined
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      if (payload.method !== 'item/completed') return
      const p = (payload.params ?? {}) as Record<string, unknown>
      if (p.threadId !== threadId) return
      const item = p.item as Record<string, unknown> | undefined
      if (!item || item.type !== 'toolCall') return
      if (!isBuilderToolName(item.toolName as string | undefined)) return
      const parsed = parseBuilderToolResult(item.result)
      if (parsed) onResultRef.current(parsed)
    })
    return () => {
      unsubscribe()
    }
  }, [threadId])

  return { threadId, status, error, start, syncDraft }
}
