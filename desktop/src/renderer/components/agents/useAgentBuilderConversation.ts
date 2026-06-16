/**
 * Agent Builder — conversational session lifecycle.
 *
 * Drives the hidden "builder thread" that backs the conversational profile-builder
 * (specs/agents/agent-profiles.md §12A). Because the renderer's conversation view and Composer are
 * singletons bound to the one active thread, the builder thread becomes the app's active thread while
 * the chat pane is open; the previous active thread is restored on close. The hook also listens to the
 * raw notification stream and surfaces each builder tool's result (the fine-grained change descriptor)
 * so the editor can apply it to the live draft and mark the field — see agentBuilderDraftSync.ts.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useThreadStore } from '../../stores/threadStore'
import { useConversationStore } from '../../stores/conversationStore'
import type { ThreadSummary } from '../../types/thread'
import type { InputPart } from '../../types/conversation'
import {
  builderFieldForToolName,
  parseBuilderToolResult,
  type BuilderField,
  type BuilderToolResult
} from './agentBuilderDraftSync'

export type BuilderConversationStatus = 'idle' | 'starting' | 'ready' | 'error'

interface Options {
  /** When false, tear down and restore the previously active thread if a builder thread was started. */
  active: boolean
  /** Called with each builder tool's parsed result, in stream order. */
  onResult: (result: BuilderToolResult) => void
  /** Called when a builder tool starts or finishes editing a field. */
  onEditingField?: (field: BuilderField | null) => void
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

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : null
}

function stringValue(...values: unknown[]): string | null {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value
  }
  return null
}

function latestPendingField(fieldsByCallId: Map<string, BuilderField>): BuilderField | null {
  let latest: BuilderField | null = null
  for (const field of fieldsByCallId.values()) latest = field
  return latest
}

function notificationThreadId(params: Record<string, unknown>): string | null {
  const turn = asRecord(params.turn)
  return stringValue(params.threadId, turn?.threadId)
}

function builderToolCallField(params: Record<string, unknown>): { field: BuilderField; callId: string | null } | null {
  const item = asRecord(params.item)
  const itemPayload = asRecord(item?.payload)
  const paramsPayload = asRecord(params.payload)
  const itemType = stringValue(params.type, item?.type)
  if (itemType && itemType !== 'toolCall') return null

  const toolName = stringValue(
    params.toolName,
    params.functionName,
    params.name,
    paramsPayload?.toolName,
    paramsPayload?.functionName,
    paramsPayload?.name,
    item?.toolName,
    item?.functionName,
    item?.name,
    itemPayload?.toolName,
    itemPayload?.functionName,
    itemPayload?.name
  )
  const field = builderFieldForToolName(toolName)
  if (!field) return null

  const callId = stringValue(
    params.callId,
    params.toolCallId,
    paramsPayload?.callId,
    paramsPayload?.toolCallId,
    item?.callId,
    item?.toolCallId,
    itemPayload?.callId,
    itemPayload?.toolCallId
  )
  return { field, callId }
}

function restorePreviousActiveThreadIfCurrentBuilder(builderThreadId: string | null, previousThreadId: string | null): void {
  if (!builderThreadId) return
  const threadStore = useThreadStore.getState()
  if (threadStore.activeThreadId !== builderThreadId) return

  threadStore.setActiveThreadId(previousThreadId)
  threadStore.setActiveThread(null)
}

export function useAgentBuilderConversation({
  active,
  onResult,
  onEditingField
}: Options): Result {
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const [threadId, setThreadId] = useState<string | null>(null)
  const [status, setStatus] = useState<BuilderConversationStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const previousActiveThreadIdRef = useRef<string | null>(null)
  const threadIdRef = useRef<string | null>(null)
  const startPromiseRef = useRef<Promise<void> | null>(null)
  const startTokenRef = useRef(0)
  const pendingBuilderToolFieldsRef = useRef<Map<string, BuilderField>>(new Map())
  const onResultRef = useRef(onResult)
  onResultRef.current = onResult
  const onEditingFieldRef = useRef(onEditingField)
  onEditingFieldRef.current = onEditingField

  const setCurrentThreadId = useCallback((nextThreadId: string | null): void => {
    threadIdRef.current = nextThreadId
    setThreadId(nextThreadId)
  }, [])

  useEffect(() => {
    if (active) return undefined

    startTokenRef.current += 1
    if (threadIdRef.current) {
      restorePreviousActiveThreadIfCurrentBuilder(threadIdRef.current, previousActiveThreadIdRef.current)
    }
    setCurrentThreadId(null)
    setStatus('idle')
    setError(null)
    startPromiseRef.current = null
    pendingBuilderToolFieldsRef.current.clear()
    onEditingFieldRef.current?.(null)
    return undefined
  }, [active, setCurrentThreadId])

  useEffect(() => () => {
    if (threadIdRef.current) {
      restorePreviousActiveThreadIfCurrentBuilder(threadIdRef.current, previousActiveThreadIdRef.current)
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

        await rpc('thread/subscribe', {
          threadId: newThreadId
        })
        if (token !== startTokenRef.current) return

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
          restorePreviousActiveThreadIfCurrentBuilder(threadIdRef.current, previousActiveThreadIdRef.current)
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

  // Surface builder tool results off the notification stream. AppServer emits ordinary tools as a
  // completed toolCall followed by a separate toolResult carrying the function return value.
  useEffect(() => {
    if (!active) return undefined
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      const p = (payload.params ?? {}) as Record<string, unknown>
      if (notificationThreadId(p) !== threadIdRef.current) return

      if (
        payload.method === 'turn/completed'
        || payload.method === 'turn/failed'
        || payload.method === 'turn/cancelled'
      ) {
        pendingBuilderToolFieldsRef.current.clear()
        onEditingFieldRef.current?.(null)
        return
      }

      if (payload.method === 'item/started') {
        const started = builderToolCallField(p)
        if (!started) return
        if (started.callId) pendingBuilderToolFieldsRef.current.set(started.callId, started.field)
        onEditingFieldRef.current?.(started.field)
        return
      }

      if (payload.method === 'item/toolCall/argumentsDelta') {
        const callId = stringValue(p.callId, p.toolCallId)
        const field = builderFieldForToolName(stringValue(p.toolName, p.functionName, p.name))
          ?? (callId ? pendingBuilderToolFieldsRef.current.get(callId) ?? null : null)
        if (!field) return
        if (callId) pendingBuilderToolFieldsRef.current.set(callId, field)
        onEditingFieldRef.current?.(field)
        return
      }

      if (payload.method !== 'item/completed') return
      const item = p.item as Record<string, unknown> | undefined
      if (!item) return
      const itemPayload = asRecord(item.payload) ?? {}
      const itemType = stringValue(item.type)

      if (itemType === 'toolCall') {
        const toolName = stringValue(item.toolName, itemPayload.toolName, item.functionName, itemPayload.functionName, item.name)
        const field = builderFieldForToolName(toolName)
        if (!field) return

        const callId = stringValue(item.toolCallId, itemPayload.callId, item.callId)
        if (callId) pendingBuilderToolFieldsRef.current.set(callId, field)
        onEditingFieldRef.current?.(field)

        const inlineResult = item.result ?? itemPayload.result
        const parsed = parseBuilderToolResult(inlineResult)
        if (parsed) {
          onResultRef.current(parsed)
          if (callId) pendingBuilderToolFieldsRef.current.delete(callId)
          onEditingFieldRef.current?.(latestPendingField(pendingBuilderToolFieldsRef.current))
        }
        return
      }

      if (itemType !== 'toolResult') return
      const callId = stringValue(item.toolCallId, itemPayload.callId, item.callId)
      if (!callId || !pendingBuilderToolFieldsRef.current.has(callId)) return

      const parsed = parseBuilderToolResult(item.result ?? itemPayload.result ?? item.text)
      pendingBuilderToolFieldsRef.current.delete(callId)
      if (parsed) onResultRef.current(parsed)
      onEditingFieldRef.current?.(latestPendingField(pendingBuilderToolFieldsRef.current))
    })
    return () => {
      pendingBuilderToolFieldsRef.current.clear()
      onEditingFieldRef.current?.(null)
      unsubscribe()
    }
  }, [active])

  return { threadId, status, error, start, syncDraft }
}
