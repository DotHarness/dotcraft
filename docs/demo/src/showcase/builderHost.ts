import { parseProfile } from '@renderer/components/agents/agentProfileDraft'
import { useConversationStore } from '@renderer/stores/conversationStore'
import { useThreadStore } from '@renderer/stores/threadStore'
import type { Thread } from '@renderer/types/thread'
import { translate } from '../../../../desktop/src/shared/locales'
import { demoLocale, DEMO_WORKSPACE_PATH } from '../mockApi'
import { followUpScript, mcpCatalog, skillCatalog, starterScript, toolCatalog, type BuilderEdit, type BuilderScript, type StarterKey } from './content/agentBuilder'
import { inputText, noteInsteadOfNextToast, notify, onRequest } from './host'
import { noteCopy } from './notes'
import { lang } from './params'

const STARTERS: Record<StarterKey, string> = {
  review: 'agentBuilder.chat.createReviewPrompt',
  docs: 'agentBuilder.chat.createDocsPrompt',
  triage: 'agentBuilder.chat.createTriagePrompt'
}

let threadId = ''
let threadCount = 0
let turnCount = 0
let draftMarkdown = ''
const savedAgents = new Set<string>()

function at(ms: number, run: () => void): void {
  setTimeout(run, ms)
}

function now(): string {
  return new Date().toISOString()
}

function matchStarter(text: string): StarterKey | null {
  const entry = Object.entries(STARTERS).find(([, key]) => translate(demoLocale, key) === text)
  return entry ? (entry[0] as StarterKey) : null
}

function streamMessage(turnId: string, start: number, id: string, text: string, phase: 'commentary' | 'final'): number {
  let createdAt = ''
  const words = text.split(lang === 'zh' ? /(?<=[，。、「」])/ : /(?<= )/)
  const conversation = useConversationStore.getState
  at(start, () => {
    createdAt = now()
    conversation().onItemStarted({ threadId, turnId, item: { id, type: 'agentMessage', phase, createdAt } })
  })
  words.forEach((word, index) => at(start + 60 + index * 45, () => conversation().onAgentMessageDelta(word)))
  const end = start + 120 + words.length * 45
  at(end, () => conversation().onItemCompleted({
    threadId, turnId, item: { id, type: 'agentMessage', phase, text, createdAt, completedAt: now() }
  }))
  return end
}

function toolItem(edit: BuilderEdit, id: string, createdAt: string, completed: boolean): Record<string, unknown> {
  return {
    id,
    type: 'toolCall',
    toolName: edit.tool,
    toolCallId: `${id}-call`,
    source: { kind: 'CoreNative', sourceId: 'agent-profile-builder' },
    presentation: { presentationId: 'core.agent-builder', options: { field: edit.field } },
    createdAt,
    ...(completed
      ? { arguments: edit.args, result: JSON.stringify({ ok: true, field: edit.field, change: edit.change }), success: true, executionStatus: 'completed', completedAt: now() }
      : { executionStatus: 'inProgress' })
  }
}

function play(turnId: string, script: BuilderScript, firstRequest: string | null): void {
  const turn = { id: turnId, threadId, status: 'running', startedAt: now(), items: [] }
  at(0, () => {
    if (firstRequest !== null) {
      useThreadStore.getState().setActiveThread({
        id: threadId, displayName: null, status: 'active', originChannel: 'dotcraft-desktop', createdAt: turn.startedAt,
        lastActiveAt: turn.startedAt, workspacePath: DEMO_WORKSPACE_PATH, userId: 'local', metadata: {}, turns: []
      } as unknown as Thread)
    }
    useConversationStore.getState().onTurnStarted(turn)
    if (firstRequest !== null) {
      useConversationStore.getState().onItemStarted({
        threadId, turnId, item: { id: `${turnId}-request`, type: 'userMessage', text: firstRequest, createdAt: turn.startedAt }
      })
    }
  })
  let t = streamMessage(turnId, 150, `${turnId}-opening`, script.opening, 'commentary') + 250
  script.edits.forEach((edit, index) => {
    const id = `${turnId}-edit-${index}`
    let createdAt = ''
    at(t, () => {
      createdAt = now()
      const started = { threadId, turnId, item: toolItem(edit, id, createdAt, false) }
      useConversationStore.getState().onItemStarted(started)
      notify('item/started', started)
    })
    const duration = edit.field === 'instructions' ? 900 : 520
    at(t + duration, () => {
      const completed = { threadId, turnId, item: toolItem(edit, id, createdAt, true) }
      useConversationStore.getState().onItemCompleted(completed)
      notify('item/completed', completed)
    })
    t += duration + 180
  })
  t = streamMessage(turnId, t, `${turnId}-closing`, script.closing, 'final') + 150
  at(t, () => {
    const completed = { ...turn, status: 'completed', completedAt: now() }
    useConversationStore.getState().onTurnCompleted(completed)
    notify('turn/completed', { threadId, turn: completed })
  })
}

export function installBuilderHost(): void {
  onRequest('agent/profiles/list', () => ({ profiles: [] }))
  onRequest('tool/list', () => ({ tools: toolCatalog }))
  onRequest('skills/list', () => ({ skills: skillCatalog }))
  onRequest('mcp/list', () => ({ servers: mcpCatalog }))
  onRequest('thread/start', () => {
    threadId = `showcase-builder-thread-${++threadCount}`
    turnCount = 0
    return { thread: { id: threadId, workspacePath: DEMO_WORKSPACE_PATH } }
  })
  onRequest('thread/subscribe', () => ({}))
  onRequest('thread/read', (params) => ({ thread: { id: params.threadId, turns: [] } }))
  onRequest('agent/profiles/builderDraft/update', (params) => {
    draftMarkdown = String(params.rawContent ?? '')
    return { success: true }
  })
  onRequest('agent/profiles/upsert', (params) => {
    const name = String(params.id ?? '')
    if (!savedAgents.has(name)) noteInsteadOfNextToast(noteCopy.saveAgent(name))
    savedAgents.add(name)
    return { profile: { id: name, name, source: params.source ?? 'workspace', valid: true, readOnly: false, rawContent: params.rawContent ?? '', updatedAt: now() } }
  })
  onRequest('turn/start', (params) => {
    const request = inputText(params.input)
    const turnId = `${threadId}-turn-${++turnCount}`
    const first = turnCount === 1
    const script = first
      ? starterScript(matchStarter(request), lang, request)
      : followUpScript(lang, request, parseProfile(draftMarkdown).roleInstructions)
    play(turnId, script, first ? request : null)
    return { turn: { id: turnId, threadId, status: 'running' } }
  })
}
