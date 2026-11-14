import { useCallback, useEffect, useRef, useState } from 'react'
import type { TypedAppServerRequestApi } from '../../../shared/appServerBoundary'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { getThreadConnection } from '../../stores/threadConnectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { ConversationItem, ConversationTurn, InputPart } from '../../types/conversation'
import { hasInlineEditContent, inlineEditInput, inlineEditText, replaceInlineEditText } from '../../utils/inlineMessageEdit'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { reloadThreadAfterRollback } from '../../utils/threadRollback'
import { modelSettingsForThread, type TurnModelSettings } from '../../utils/turnModelSettings'
import { isVisibleUserMessage } from '../../utils/visibleUserMessage'
import { describeAppServerError } from '../../utils/appServerErrorText'

export interface InlineEditState {
  threadId: string
  turnId: string
  itemId: string
  draftText: string
  inputParts: InputPart[]
  submitting: boolean
  rollbackPending: boolean
  workspacePath: string
  identityWorkspacePath: string
  modelSettings: TurnModelSettings
  request: TypedAppServerRequestApi['sendRequest']
  isCurrent: () => boolean
}

function matchesEdit(left: InlineEditState | null, right: InlineEditState): boolean {
  return left?.threadId === right.threadId && left.turnId === right.turnId && left.itemId === right.itemId
}

function isLatestEdit(current: InlineEditState): boolean {
  const state = useConversationStore.getState()
  const latestTurn = state.turns.at(-1)
  const latestUser = latestTurn ? [...latestTurn.items].reverse().find(isVisibleUserMessage) : undefined
  return current.isCurrent() && state.turnStatus === 'idle'
    && latestTurn?.id === current.turnId && latestUser?.id === current.itemId
}

export function useInlineMessageEdit() {
  const t = useT()
  const locale = useLocale()
  const activeThreadId = useThreadStore((state) => state.activeThreadId)
  const workspacePath = useConversationStore((state) => state.workspacePath)
  const connectionKey = useConnectionStore((state) => state.connectionKey)
  const connectionId = useConnectionStore((state) => state.connectionId)
  const [editing, setEditing] = useState<InlineEditState | null>(null)
  const inFlight = useRef(false)

  useEffect(() => { setEditing(null) }, [activeThreadId, workspacePath, connectionKey, connectionId])

  const startEdit = useCallback((turn: ConversationTurn, item: ConversationItem) => {
    const thread = useThreadStore.getState().activeThread
    const target = getThreadConnection(turn.threadId)
    const foreground = useConnectionStore.getState()
    const source = target ?? (foreground.connectionKey && foreground.connectionId
      ? { connectionKey: foreground.connectionKey, connectionId: foreground.connectionId }
      : null)
    const request: TypedAppServerRequestApi['sendRequest'] = source
      ? (method, params, timeout, options) => window.api.appServer.sendConnectionRequest(
          source.connectionKey, method, params, timeout, { ...options, connectionId: source.connectionId })
      : (method, params, timeout, options) => window.api.appServer.sendRequest(method, params, timeout, options)
    const inputParts = inlineEditInput(item)
    setEditing({
      threadId: turn.threadId, turnId: turn.id, itemId: item.id,
      draftText: inlineEditText(inputParts), inputParts,
      submitting: false, rollbackPending: true,
      workspacePath: workspacePath || thread?.workspacePath || '',
      identityWorkspacePath: thread?.workspacePath || workspacePath || '',
      modelSettings: modelSettingsForThread(turn.threadId),
      request,
      isCurrent: () => {
        const connection = useConnectionStore.getState()
        return useThreadStore.getState().activeThreadId === turn.threadId
          && useConversationStore.getState().workspacePath === workspacePath
          && (!source || connection.connectionKey === source.connectionKey && connection.connectionId === source.connectionId)
      }
    })
  }, [workspacePath])

  const changeDraft = useCallback((draftText: string) => {
    setEditing((prev) => prev && !prev.submitting ? { ...prev, draftText } : prev)
  }, [])

  const cancelEdit = useCallback(() => {
    setEditing((prev) => prev?.submitting ? prev : null)
  }, [])

  const submitEdit = useCallback(async (): Promise<void> => {
    const current = editing
    if (!current || current.submitting || inFlight.current || !current.isCurrent()) return
    const draftText = current.draftText.trim()
    if (!hasInlineEditContent(draftText, current.inputParts)) return
    inFlight.current = true
    setEditing({ ...current, submitting: true })
    const { inputParts } = current
    let { rollbackPending } = current
    try {
      if (rollbackPending) {
        if (!isLatestEdit(current)) {
          setEditing((prev) => matchesEdit(prev, current) ? null : prev)
          addToast(t('conversation.editStale'), 'warning')
          return
        }
        const result = await current.request('thread/rollback', { threadId: current.threadId, numTurns: 1 })
        rollbackPending = false
        if (result.thread && current.isCurrent()) {
          await reloadThreadAfterRollback(current.threadId,
            (method, params, options) => current.request(method, params, undefined, options), current.isCurrent)
        }
      }
      await startTurnWithOptimisticUI({
        threadId: current.threadId,
        workspacePath: current.workspacePath,
        identityWorkspacePath: current.identityWorkspacePath,
        text: draftText,
        inputParts: replaceInlineEditText(inputParts, draftText, current.threadId),
        request: current.request,
        modelSettings: current.modelSettings,
        onScreen: current.isCurrent(),
        fallbackThreadName: t('toast.imageMessage'),
        renameThreadFromText: false,
        throwOnStartError: true
      })
      setEditing((prev) => matchesEdit(prev, current) ? null : prev)
    } catch (error) {
      console.error('inline edit retry failed:', error)
      setEditing((prev) => matchesEdit(prev, current)
        ? { ...prev!, submitting: false, rollbackPending }
        : prev)
      addToast(describeAppServerError(locale, error), 'error')
    } finally {
      inFlight.current = false
    }
  }, [editing, locale, t])

  return { editing, startEdit, changeDraft, cancelEdit, submitEdit }
}
