import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { buildTranscript, createTranscriptProjector, type TranscriptEntry } from './transcript'
import { createChangesProjector } from './turnChanges'

function item(id: string, type: string, payload: Record<string, unknown>, turnId = 't1'): HistoryItem {
  return { id, turnId, type, payload, status: 'completed', createdAt: '2026-10-25T10:00:00Z', completedAt: '2026-10-25T10:00:01Z', partial: false }
}

function history(items: HistoryItem[], status = 'running'): ChatHistory {
  return { ...emptyHistory(), items, turns: [{ id: 't1', status, error: null }] }
}

function read(id: string) {
  return [
    item(id, 'toolCall', { callId: id, toolName: 'ReadFile', arguments: { path: id }, source: { kind: 'CoreNative' }, presentation: { presentationId: 'core.read-file', options: { operation: 'read' } } }),
    item(`${id}-result`, 'toolResult', { callId: id, result: 'read output' }),
  ]
}

function flatten(entries: TranscriptEntry[]): TranscriptEntry[] {
  return entries.flatMap((entry) => entry.kind === 'toolGroup' || entry.kind === 'activity' ? [entry, ...flatten(entry.children)] : [entry])
}

describe('thread transcript projection', () => {
  it('keeps completed turns and unchanged tool groups stable during reasoning updates', () => {
    const projector = createTranscriptProjector()
    const previous = item('previous', 'agentMessage', { text: 'Previous answer' }, 'old')
    const reasoning = { ...item('reasoning', 'reasoningContent', { text: '**Inspecting**\n' }), status: 'started', completedAt: null }
    const current = history([previous, item('input', 'userMessage', { text: 'Check it' }), ...read('a'), ...read('b'), reasoning])
    current.turns.unshift({ id: 'old', status: 'completed', error: null })
    const initial = projector.project(current)
    const updated = { ...current, items: current.items.map((entry) => entry === reasoning ? { ...reasoning, payload: { text: '**Inspecting**\nMore details' } } : entry) }
    const next = projector.project(updated)
    expect(next).toBe(initial)

    const renamed = { ...updated, items: updated.items.map((entry) => entry.id === 'reasoning' ? { ...entry, payload: { text: '**Checking a new file**\n' } } : entry) }
    const renamedOutput = projector.project(renamed)
    expect(renamedOutput.at(-1)).toMatchObject({ kind: 'thinking', heading: 'Checking a new file' })
    expect(renamedOutput[0]).toBe(initial[0])
    expect(renamedOutput.find((entry) => entry.kind === 'toolGroup')).toBe(initial.find((entry) => entry.kind === 'toolGroup'))
    expect(renamedOutput).toEqual(buildTranscript(renamed))
  })

  it('invalidates tool projections for late results, command execution and terminal corrections', () => {
    const projector = createTranscriptProjector()
    const call = item('call', 'toolCall', { callId: 'call', toolName: 'Exec', arguments: { command: 'npm run dev' }, source: { kind: 'CoreNative' }, presentation: { presentationId: 'core.shell' } })
    const initial = history([call])
    expect(flatten(projector.project(initial)).find((entry) => entry.kind === 'tool')).toMatchObject({ live: true })
    const completed = { ...initial, turns: [{ id: 't1', status: 'completed', error: null }], items: [call, item('result', 'toolResult', { callId: 'call', result: 'started', success: true }), item('execution', 'commandExecution', { callId: 'call', status: 'inProgress', sessionId: 'term_1' })] }
    expect(flatten(projector.project(completed)).find((entry) => entry.kind === 'tool')).toMatchObject({ verb: 'backgroundStarted', live: false, details: [{ output: 'started' }] })
    const corrected = { ...completed, items: completed.items.map((entry) => entry.id === 'execution' ? { ...entry, payload: { ...entry.payload, status: 'cancelled' } } : entry) }
    expect(flatten(projector.project(corrected)).find((entry) => entry.kind === 'tool')).toMatchObject({ verb: 'backgroundStopped' })
    expect(projector.project(corrected)).toEqual(buildTranscript(corrected))
  })

  it('rechecks approval subjects and provider error rendering when their dependencies change', () => {
    const projector = createTranscriptProjector()
    const response = item('response', 'approvalResponse', { requestId: 'approval', decision: 'decline' })
    const approval = item('approval', 'approvalRequest', { requestId: 'approval', approvalType: 'file', target: '/workspace/a.ts' })
    const current = history([approval, response, item('error', 'error', { message: 'failed' })], 'failed')
    const before = projector.project(current)
    const next = { ...current, turns: [{ id: 't1', status: 'failed', error: null, providerError: 'usageLimitExceeded' }], items: current.items.map((entry) => entry === approval ? { ...approval, payload: { ...approval.payload, target: '/workspace/b.ts' } } : entry) }
    expect(projector.project(next)).toMatchObject([{ kind: 'notice', detail: 'b.ts' }, { kind: 'divider', divider: 'usageLimit' }])
    expect(projector.project(next)).not.toBe(before)
  })

  it('updates origin, stopped status and repeated failures after prepend or authoritative replacement', () => {
    const projector = createTranscriptProjector()
    const current = history([item('input', 'userMessage', { text: 'Hello' })], 'cancelled')
    expect(projector.project(current, null, { spawnedFrom: 'parent' })[0]).toMatchObject({ origin: { threadId: 'parent' } })
    expect(projector.project(current, null, { stopped: ['t1'] })[1]).toMatchObject({ status: 'youStopped' })
    const prepended = { ...current, olderCursor: 'older', items: [item('old', 'agentMessage', { text: 'Older' }, 'old'), ...current.items], turns: [{ id: 'old', status: 'completed', error: null }, ...current.turns] }
    expect(projector.project(prepended, null, { spawnedFrom: 'parent' }).find((entry) => entry.kind === 'user')).toMatchObject({ origin: null })
    const failed = { ...history([item('e1', 'error', { message: 'busy' }), item('e2', 'error', { message: 'busy' }, 't2')]), turns: [{ id: 't1', status: 'failed', error: 'busy', providerError: 'serverOverloaded' }, { id: 't2', status: 'failed', error: 'busy', providerError: 'serverOverloaded' }] }
    expect(projector.project(failed)).toHaveLength(1)
    const replacement = { ...failed, items: [item('real', 'userMessage', { text: 'Retry' }), ...failed.items] }
    expect(projector.project(replacement)).toEqual(buildTranscript(replacement))
    expect(projector.project(replacement).some((entry) => entry.id === 't1/e1')).toBe(true)
    projector.clear()
    expect(projector.project(replacement)).toEqual(buildTranscript(replacement))
  })

  it('shares change objects with the pill and invalidates workspace-relative paths', () => {
    const changes = createChangesProjector()
    const projector = createTranscriptProjector(changes)
    const file = item('result', 'toolResult', { structuredContent: { kind: 'fileChange', changes: [{ path: '/workspace/a.ts', additions: 1, deletions: 0 }] } })
    const current = history([file, item('reply', 'agentMessage', { text: 'Done' })], 'completed')
    const output = projector.project(current, '/workspace')
    const reply = output.find((entry) => entry.kind === 'assistant')
    expect(reply?.kind === 'assistant' && reply.changes).toBe(changes.project(current, 't1', '/workspace'))
    projector.clear()
    expect(changes.project(current, 't1', '/workspace')).toBe(reply?.kind === 'assistant' ? reply.changes : null)
    const changedPath = projector.project(current, '/different')
    expect(changedPath.find((entry) => entry.kind === 'assistant')).toMatchObject({ changes: { files: [{ path: '/workspace/a.ts' }] } })
    expect(changedPath).toEqual(buildTranscript(current, '/different'))
  })
})
