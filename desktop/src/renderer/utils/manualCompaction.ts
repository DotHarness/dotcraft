import { useConversationStore } from '../stores/conversationStore'
import { restartThreadHistory } from '../stores/threadHistoryStore'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import { wireTurnToConversationTurn } from '../types/conversation'
import type { ContextUsageSnapshotWire } from '../types/thread'
import { readThreadHistoryHead } from './threadHistory'

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

const MANUAL_COMPACTION_TIMEOUT_MS = 5 * 60 * 1000

let compacting = false

function isRequestTimeoutError(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err)
  const normalized = message.toLowerCase()
  return normalized.includes('timed out') || normalized.includes('timeout')
}

export async function compactThreadContext(threadId: string, t: TranslateFn): Promise<boolean> {
  if (compacting) return false
  compacting = true
  addToast(t('composer.compact.started'), 'info')
  try {
    const result = (await window.api.appServer.sendRequest(
      'thread/compact/start',
      { threadId },
      MANUAL_COMPACTION_TIMEOUT_MS
    )) as {
      outcome?: string
      message?: string
      contextUsage?: ContextUsageSnapshotWire | null
    }
    if (result.contextUsage) {
      useConversationStore.getState().setContextUsage(result.contextUsage)
    }
    const outcome = String(result.outcome ?? '').toLowerCase()
    if (outcome === 'micro' || outcome === 'partial') {
      await refreshThreadAfterManualCompact(threadId)
      addToast(t('composer.compact.succeeded'), 'success')
    } else if (outcome === 'skipped') {
      addToast(t('composer.compact.skipped'), 'info')
    } else {
      addToast(t('composer.compact.failed', { error: result.message || outcome || 'unknown' }), 'error')
    }
    return true
  } catch (err) {
    if (isRequestTimeoutError(err) && useConversationStore.getState().maintenanceKind === 'compacting') {
      addToast(t('composer.compact.stillRunning'), 'info')
      return true
    }
    addToast(t('composer.compact.failed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    return false
  } finally {
    compacting = false
  }
}

async function refreshThreadAfterManualCompact(threadId: string): Promise<void> {
  try {
    const response = await readThreadHistoryHead(
      (method, params) => window.api.appServer.sendRequest(method, params),
      threadId
    )
    const refreshed = response.thread
    if (!refreshed || useThreadStore.getState().activeThreadId !== threadId) return

    useThreadStore.getState().setActiveThread(refreshed)
    restartThreadHistory(threadId, refreshed.turns ?? [], response.turnCursor)
    useConversationStore.getState().setTurns(
      (refreshed.turns ?? []).map((turn) =>
        wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)
      )
    )
    useConversationStore.getState().setQueuedInputs(refreshed.queuedInputs ?? [])
    if ('contextUsage' in refreshed) {
      useConversationStore.getState().setContextUsage(refreshed.contextUsage ?? null)
    }
  } catch (err) {
    console.warn('thread/read after manual compaction failed:', err)
  }
}
