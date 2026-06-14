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

import { useEffect, useRef, useState } from 'react'
import { useThreadStore } from '../../stores/threadStore'
import { useConversationStore } from '../../stores/conversationStore'
import type { ThreadSummary } from '../../types/thread'
import { isBuilderToolName, parseBuilderToolResult, type BuilderToolResult } from './agentBuilderDraftSync'
import type { SaveTarget } from './agentProfileDraft'

export type BuilderConversationStatus = 'idle' | 'starting' | 'ready' | 'error'

interface Options {
  /** When true, ensure a builder thread exists and is active; when false, tear down and restore. */
  active: boolean
  /** The profile id the conversation edits (a placeholder is fine for a not-yet-created agent). */
  targetId: string
  /** Source the profile will be saved to (user / workspace). */
  targetSource: SaveTarget
  /** Optional opening message auto-sent once when the thread is ready (the auto-propose entry). */
  initialPrompt?: string | null
  /** Called with each builder tool's parsed result, in stream order. */
  onResult: (result: BuilderToolResult) => void
}

interface Result {
  threadId: string | null
  status: BuilderConversationStatus
  error: string | null
}

async function rpc<T>(method: string, params: Record<string, unknown>): Promise<T> {
  return (await window.api.appServer.sendRequest(method, params)) as T
}

export function useAgentBuilderConversation({ active, targetId, targetSource, initialPrompt, onResult }: Options): Result {
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const [threadId, setThreadId] = useState<string | null>(null)
  const [status, setStatus] = useState<BuilderConversationStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const previousActiveThreadIdRef = useRef<string | null>(null)
  const proposedThreadRef = useRef<string | null>(null)
  const onResultRef = useRef(onResult)
  onResultRef.current = onResult
  const initialPromptRef = useRef(initialPrompt)
  initialPromptRef.current = initialPrompt

  // Create + activate the builder thread when the pane opens; restore the prior thread when it closes.
  useEffect(() => {
    if (!active) return undefined
    let cancelled = false

    void (async () => {
      setStatus('starting')
      setError(null)
      try {
        previousActiveThreadIdRef.current = useThreadStore.getState().activeThreadId
        const res = await rpc<{ thread: ThreadSummary }>('thread/start', {
          identity: {
            channelName: 'dotcraft-desktop',
            userId: 'local',
            channelContext: `workspace:${workspacePath}`,
            workspacePath
          },
          historyMode: 'server'
        })
        if (cancelled) return

        const newThreadId = res.thread.id
        // Bind the thread to the target profile so the backend exposes the builder tools + working draft.
        await rpc('thread/config/update', {
          threadId: newThreadId,
          config: {
            agentBuilderTargetId: targetId.trim() || 'draft-agent',
            agentBuilderTargetSource: targetSource
          }
        })
        if (cancelled) return

        // Don't add the builder thread to the local thread list — it's an internal thread (hidden from
        // the sidebar; the backend also excludes it from thread/list). Selecting it triggers App's
        // active-thread effect, which loads it and subscribes for streaming.
        useThreadStore.getState().setActiveThreadId(newThreadId)
        setThreadId(newThreadId)
        setStatus('ready')

        // Auto-propose: send the opening message once so the builder starts proposing immediately.
        const prompt = initialPromptRef.current?.trim()
        if (prompt && proposedThreadRef.current !== newThreadId) {
          proposedThreadRef.current = newThreadId
          await rpc('turn/enqueue', { threadId: newThreadId, text: prompt }).catch(() => undefined)
        }
      } catch (err) {
        if (!cancelled) {
          setStatus('error')
          setError(err instanceof Error ? err.message : String(err))
        }
      }
    })()

    return () => {
      cancelled = true
      const previous = previousActiveThreadIdRef.current
      useThreadStore.getState().setActiveThreadId(previous)
      setThreadId(null)
      setStatus('idle')
      setError(null)
    }
  }, [active, targetId, targetSource, workspacePath])

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

  return { threadId, status, error }
}
