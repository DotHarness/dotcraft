import type { SessionThread, ThreadResumeCurrentState } from '@dotcraft/sdk/contracts'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import { contextUsageOf } from './contextUsage'
import { historyFromTurns, restoreEchoes, type ChatHistory } from './history'
import { OPENING_TURNS_PAGE, readTurnPage, turnPage } from './projectHistory'
import type { ChatDetail } from './state'

export async function restoreChat(
  client: DotCraftWireClient,
  threadId: string,
  known: boolean,
  current: () => boolean,
  preview: (history: ChatHistory) => void,
  profileName: (id: string) => Promise<string>,
  readPreview: <T>(read: () => Promise<T>) => Promise<T>,
  boundary: () => void,
): Promise<{ thread: SessionThread; detail: ChatDetail; currentState?: ThreadResumeCurrentState }> {
  const header = client.request('thread/read', { threadId })
  void header.catch(() => undefined)
  const page = readPreview(() => readTurnPage(client, threadId, null))
  let previewing = true
  void page.then((value) => { if (previewing && current()) preview(historyFromTurns(value.turns, value.nextCursor)) }, () => undefined)
  // Unknown targets may be archived children; a query must establish that before activation.
  const read = known ? null : await header
  const restored = read?.thread.status === 'archived'
    ? { thread: read.thread, currentState: undefined, initialTurnsPage: undefined }
    : await client.request('thread/resume', { threadId, initialTurnsPage: OPENING_TURNS_PAGE }, undefined, { onResponse: boundary })
  previewing = false
  if (restored.currentState && !restored.initialTurnsPage) throw new Error('Resume did not return the requested initial Turns page.')
  const selected = restored.currentState ? turnPage(restored.initialTurnsPage!) : await page
  const thread = restored.thread
  let history = historyFromTurns(selected.turns, selected.nextCursor)
  const state = restored.currentState
  if (state) history = { ...history, items: history.items.map((item) => {
    if (item.turnId !== state.turnId) return item
    const argumentsPreview = state.toolArguments?.[item.id]
    const progress = state.toolProgress?.[item.id]
    return argumentsPreview === undefined && progress === undefined ? item : {
      ...item, payload: { ...item.payload, ...(argumentsPreview === undefined ? {} : { argumentsPreview }), ...(progress ? { progress } : {}) },
    }
  }) }
  if (restored.currentState?.turnId && restored.currentState.diff) {
    history = { ...history, diffs: { [restored.currentState.turnId]: restored.currentState.diff } }
  }
  const id = thread.configuration?.agentProfileId
  const name = id ? await profileName(id).catch(() => id) : null
  return {
    thread,
    currentState: restored.currentState,
    detail: {
      loading: true, history,
      currentPlanTurnId: restored.currentState?.planTurnId ?? null,
      currentPlan: thread.plan?.content ?? null,
      profileName: name,
      config: thread.configuration ?? null,
      workspacePath: thread.workspacePath || null,
      effectiveWorkspacePath: thread.effectiveWorkspacePath || null,
      context: contextUsageOf(thread.contextUsage),
    },
  }
}

export function withEchoes(detail: ChatDetail, previous?: ChatDetail): ChatDetail {
  return { ...detail, history: restoreEchoes(detail.history, previous?.history.echoes ?? []) }
}
