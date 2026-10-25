import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { parseThreadToolAction } from '../../renderer/utils/threadToolDisplay'
import type { ConversationItem } from '../../renderer/types/conversation'
import { resetDesktopThreadToolBindings } from '../desktopRuntimeThreadTools'
import { MAX_THREAD_TASKS, PROPOSAL_EXPIRY_MS, ThreadTaskStore } from '../threadTasks/threadTaskStore'
import { PARENT, WORKSPACE, createHarness, structured, type Harness } from './threadTaskTestHelpers'

async function propose(harness: Harness, title: string, extra: Record<string, unknown> = {}) {
  const result = await harness.call({ initiation: 'ownInitiative', title, prompt: `Brief for ${title}`, ...extra })
  return structured(result).task as { id: string }
}

function connection(harness: Harness) {
  return { workspacePath: WORKSPACE, request: harness.client.sendRequest }
}

describe('CreateThread proposals', () => {
  beforeEach(() => {
    resetDesktopThreadToolBindings()
  })

  it('stores an ownInitiative proposal and returns at once without starting anything', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')

    const task = await propose(harness, 'Remove dead config option', { model: 'deepseek-v4', reasoningEffort: 'low' })

    expect(task.id).toMatch(/^task_[0-9a-f]{8}$/)
    expect(harness.store.get(PARENT, task.id)).toEqual(expect.objectContaining({
      kind: 'proposal',
      state: 'pending',
      toolCallId: 'call-turn-1',
      defaults: { model: 'deepseek-v4', reasoningEffort: 'low' }
    }))
    expect(harness.calls().some(([method]) => method === 'thread/start')).toBe(false)
  })

  it('starts a proposal by proposalId exactly as proposed and refuses a second start', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    const task = await propose(harness, 'Add coverage', { model: 'deepseek-v4' })

    const started = await harness.call({ proposalId: task.id })

    expect(started?.success).toBe(true)
    const start = harness.calls().find(([method]) => method === 'thread/start')?.[1]
    expect(start).toEqual(expect.objectContaining({ spawnedFromThreadId: PARENT, config: { model: 'deepseek-v4' } }))
    const turn = harness.calls().find(([method]) => method === 'turn/start')?.[1] as { input: Array<{ text: string }> }
    expect(turn.input[0].text).toBe('Brief for Add coverage')
    expect(harness.store.get(PARENT, task.id)?.state).toBe('started')
    const display = parseThreadToolAction({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: started?.structuredContent
    } as unknown as ConversationItem)
    expect(display).toEqual(expect.objectContaining({ kind: 'created', threadId: 'thread-child-1', started: true }))

    const again = await harness.call({ proposalId: task.id })
    expect(again?.success).toBe(false)
    expect(harness.calls().filter(([method]) => method === 'thread/start')).toHaveLength(1)
  })

  it('refuses extra arguments with proposalId unless a brief comes with them', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    const task = await propose(harness, 'Split helper')

    const refused = await harness.call({ proposalId: task.id, model: 'deepseek-v4' })
    expect(refused?.success).toBe(false)
    expect(harness.store.get(PARENT, task.id)?.state).toBe('pending')

    const malformed = await harness.call({ proposalId: 'task_nothex!' })
    expect(malformed?.success).toBe(false)

    const withBrief = await harness.call({ proposalId: task.id, model: 'deepseek-v4', prompt: 'Rewritten brief' })
    expect(withBrief?.success).toBe(true)
    const start = harness.calls().find(([method]) => method === 'thread/start')?.[1] as { config?: unknown }
    expect(start.config).toBeUndefined()
    const turn = harness.calls().find(([method]) => method === 'turn/start')?.[1] as { input: Array<{ text: string }> }
    expect(turn.input[0].text).toBe('Rewritten brief')
  })

  it('withdraws a waiting proposal and refuses to start or withdraw it again', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    const task = await propose(harness, 'Stale idea')

    const dismissed = await harness.call({ taskId: task.id, reason: 'fixed here' }, 'turn-1', 'DismissProposal')
    expect(dismissed?.success).toBe(true)
    expect(harness.store.get(PARENT, task.id)?.state).toBe('dismissed')
    expect(harness.tasks.dismiss(PARENT, task.id)).toBe('alreadyDismissed')
    expect(harness.tasks.dismiss(PARENT, 'task_00000000')).toBe('notFound')

    const start = await harness.call({ proposalId: task.id })
    expect(start?.success).toBe(false)
  })

  it('starts proposals from the card with the chosen worktree and permissions', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    const first = await propose(harness, 'First')
    const second = await propose(harness, 'Second')

    const results = await harness.tasks.startProposals(connection(harness), PARENT, [first.id, second.id], {
      workLocation: 'worktree',
      baseRef: 'main',
      approvalPolicy: 'prompt'
    })

    expect(results.map((result) => result.ok)).toEqual([true, true])
    const worktreeCalls = harness.calls().filter(([method]) => method === 'worktree/createAndStart')
    expect(worktreeCalls).toHaveLength(2)
    expect(worktreeCalls[0][1]).toEqual(expect.objectContaining({
      baseRef: 'main',
      displayName: 'First',
      config: { approvalPolicy: 'prompt' },
      operationId: expect.any(String)
    }))
    expect(harness.store.snapshot(PARENT).tasks.map((task) => task.state)).toEqual(['started', 'started'])
  })

  it('starts a proposal with reasoning off when the card turns it off', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    const task = await propose(harness, 'Quick rename', { reasoningEffort: 'high' })

    await harness.tasks.startProposals(connection(harness), PARENT, [task.id], { reasoningEffort: 'off' })

    const start = harness.calls().find(([method]) => method === 'thread/start')?.[1] as { config: { reasoning: unknown } }
    expect(start.config.reasoning).toEqual({ enabled: false, output: 'full' })
  })
})

describe('thread task store', () => {
  let tempDir = ''

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'thread-task-store-'))
  })

  afterEach(() => {
    rmSync(tempDir, { recursive: true, force: true })
  })

  const base = { threadId: PARENT, workspacePath: WORKSPACE, defaults: {} }

  it('keeps at most the cap per thread and reports evicted waiting proposals', () => {
    const store = new ThreadTaskStore(null)
    const ids: string[] = []
    for (let index = 0; index < MAX_THREAD_TASKS; index++) {
      ids.push(store.add({ ...base, kind: 'proposal', title: `P${index}` }).task.id)
    }

    const added = store.add({ ...base, kind: 'proposal', title: 'Newest' })

    expect(added.evicted.map((task) => task.id)).toEqual([ids[0]])
    expect(added.others).toHaveLength(MAX_THREAD_TASKS - 1)
    expect(store.snapshot(PARENT).tasks).toHaveLength(MAX_THREAD_TASKS)
    expect(store.get(PARENT, ids[0])).toBeUndefined()
  })

  it('expires waiting proposals after the expiry window', () => {
    let now = Date.parse('2026-10-01T00:00:00Z')
    const store = new ThreadTaskStore(null, () => now)
    const { task } = store.add({ ...base, kind: 'proposal', title: 'Later' })

    now += PROPOSAL_EXPIRY_MS + 1

    expect(store.get(PARENT, task.id)?.state).toBe('expired')
  })

  it('survives a restart and marks offers that were still waiting as not answered', () => {
    const filePath = join(tempDir, 'thread-tasks.json')
    const first = new ThreadTaskStore(filePath)
    const proposal = first.add({ ...base, kind: 'proposal', title: 'Proposal', prompt: 'Brief' }).task
    const offer = first.add({ ...base, kind: 'offer', title: 'Offer' }).task
    const accepted = first.add({ ...base, kind: 'offer', title: 'Accepted' }).task
    first.update(PARENT, accepted.id, { state: 'accepted', childThreadId: 'thread-child' })
    const launching = first.add({ ...base, kind: 'proposal', title: 'Launching', prompt: 'Brief' }).task
    first.update(PARENT, launching.id, { state: 'starting', childThreadId: 'thread-launching' })

    const reloaded = new ThreadTaskStore(filePath)

    expect(reloaded.get(PARENT, proposal.id)).toEqual(expect.objectContaining({ state: 'pending', prompt: 'Brief' }))
    expect(reloaded.get(PARENT, offer.id)?.state).toBe('notAnswered')
    expect(reloaded.get(PARENT, accepted.id)).toEqual(expect.objectContaining({ state: 'accepted', childThreadId: 'thread-child' }))
    expect(reloaded.get(PARENT, launching.id)?.state).toBe('started')
  })
})
