import { beforeEach, describe, expect, it, vi } from 'vitest'
import { resetDesktopThreadToolBindings } from '../desktopRuntimeThreadTools'
import { MAX_AUTO_STARTS } from '../threadTasks/threadTaskService'
import { PARENT, createHarness, structured, type Harness } from './threadTaskTestHelpers'

async function pendingOffer(harness: Harness, title = 'Fix flaky upload test', turnId = 'turn-1') {
  harness.composerTurn(turnId)
  const result = harness.call({ initiation: 'offer', title }, turnId)
  await vi.waitFor(() => {
    expect(harness.store.snapshot(PARENT).tasks.some((task) => task.title === title && task.state === 'pending')).toBe(true)
  })
  const task = harness.store.snapshot(PARENT).tasks.find((candidate) => candidate.title === title)!
  return { result, task }
}

describe('CreateThread offers', () => {
  beforeEach(() => {
    resetDesktopThreadToolBindings()
  })

  it('waits for New chat, creates the chat with the card options, then starts it from the brief', async () => {
    const harness = createHarness()
    const { result, task } = await pendingOffer(harness)
    expect(harness.notifyOffer).toHaveBeenCalledTimes(1)

    const answer = await harness.tasks.answerOffer(
      { workspacePath: 'F:\\examples\\workspace', request: (method, params) => harness.client.sendRequest(method, params) },
      PARENT,
      task.id,
      { choice: 'newThread', options: { approvalPolicy: 'autoApprove', model: 'deepseek-v4', providerId: 'deepseek', reasoningEffort: 'high' } }
    )
    expect(answer).toEqual({ ok: true })

    const offerResult = await result
    expect(offerResult?.success).toBe(true)
    expect(structured(offerResult).outcome).toBe('accepted')
    const accepted = harness.store.get(PARENT, task.id)!
    expect(accepted.state).toBe('accepted')
    expect(accepted.childThreadId).toBe('thread-child-1')
    const start = harness.calls().find(([method]) => method === 'thread/start')?.[1]
    expect(start).toEqual(expect.objectContaining({
      displayName: 'Fix flaky upload test',
      spawnedFromThreadId: PARENT,
      config: expect.objectContaining({ approvalPolicy: 'autoApprove', model: 'deepseek-v4', providerId: 'deepseek' })
    }))
    expect(harness.calls().some(([method]) => method === 'turn/start')).toBe(false)

    const brief = await harness.call({ proposalId: task.id, prompt: 'Fix `upload.test.ts`.', background: 'CI is red.' })
    expect(brief?.success).toBe(true)
    expect(structured(brief)).toEqual(expect.objectContaining({
      thread: expect.objectContaining({ id: 'thread-child-1' }),
      started: true
    }))
    const turnStart = harness.calls().find(([method]) => method === 'turn/start')?.[1] as { threadId: string; input: Array<{ text: string }> }
    expect(turnStart.threadId).toBe('thread-child-1')
    expect(turnStart.input[0].text).toContain('Fix `upload.test.ts`.')
    expect(turnStart.input[0].text).toContain('CI is red.')
    expect(harness.store.get(PARENT, task.id)?.state).toBe('started')
  })

  it('keeps the task here when the user chooses Do it here', async () => {
    const harness = createHarness()
    const { result, task } = await pendingOffer(harness)

    await harness.tasks.answerOffer({ workspacePath: 'w', request: harness.client.sendRequest }, PARENT, task.id, { choice: 'here' })

    expect(structured(await result).outcome).toBe('kept')
    expect(harness.store.get(PARENT, task.id)?.state).toBe('keptHere')
    expect(harness.calls().some(([method]) => method === 'thread/start')).toBe(false)
  })

  it('closes the offer when the user sends a new message on that thread', async () => {
    const harness = createHarness()
    const { result, task } = await pendingOffer(harness)

    harness.tasks.observeUserRequest('turn/enqueue', { threadId: 'other-thread' })
    harness.tasks.observeUserRequest('turn/steer', { threadId: PARENT, expectedTurnId: 'turn-1' })

    expect(structured(await result).outcome).toBe('typedPast')
    expect(harness.store.get(PARENT, task.id)?.state).toBe('notAnswered')
  })

  it('abandons the offer when its turn ends or Desktop disconnects', async () => {
    const harness = createHarness()
    const first = await pendingOffer(harness, 'First', 'turn-1')
    harness.tasks.handleNotification('turn/cancelled', { turn: { id: 'turn-1', threadId: PARENT } })
    expect(structured(await first.result).outcome).toBe('abandoned')
    expect(harness.store.get(PARENT, first.task.id)?.state).toBe('notAnswered')

    const second = await pendingOffer(harness, 'Second', 'turn-2')
    harness.tasks.abandonAllOffers()
    expect(structured(await second.result).outcome).toBe('abandoned')
  })

  it('reports unstarted when the new chat cannot be created', async () => {
    const harness = createHarness({
      handler: (method) => (method === 'thread/start' ? Promise.reject(new Error('disk full')) : undefined)
    })
    const { result, task } = await pendingOffer(harness)

    const answer = await harness.tasks.answerOffer({ workspacePath: 'w', request: harness.client.sendRequest }, PARENT, task.id, { choice: 'newThread' })

    expect(answer).toEqual({ ok: false, error: 'disk full' })
    expect(structured(await result).outcome).toBe('unstarted')
    expect(harness.store.get(PARENT, task.id)?.state).toBe('failed')
  })

  it('shows no card when the turn did not come from the Desktop composer', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-mobile', { originChannel: 'dotcraft-mobile' })
    harness.composerTurn('turn-wake', { triggerKind: 'terminal' })

    for (const turnId of ['turn-mobile', 'turn-wake']) {
      const offer = await harness.call({ initiation: 'offer', title: 'New work' }, turnId)
      expect(offer?.success).toBe(true)
      expect(structured(offer).task).toBeUndefined()
      const proposal = await harness.call({ initiation: 'ownInitiative', title: 'Follow-up', prompt: 'Do it.' }, turnId)
      expect(proposal?.success).toBe(true)
      expect(structured(proposal).task).toBeUndefined()
    }
    expect(harness.store.snapshot(PARENT).tasks).toEqual([])
    expect(harness.notifyOffer).not.toHaveBeenCalled()
  })

  it('reads an unseen turn from history to decide who can see the card', async () => {
    const harness = createHarness({
      handler: (method, params) => {
        if (method === 'thread/turns/list') return { data: [{ id: 'turn-q', originChannel: 'dotcraft-desktop' }] }
        if (method === 'thread/items/list') {
          expect(params).toEqual(expect.objectContaining({ turnId: 'turn-q', sortDirection: 'ascending' }))
          return { data: [{ turnId: 'turn-q', item: { type: 'userMessage', payload: { triggerKind: 'subagent' } } }] }
        }
        return undefined
      }
    })

    const proposal = await harness.call({ initiation: 'ownInitiative', title: 'Follow-up', prompt: 'Do it.' }, 'turn-q')

    expect(structured(proposal).task).toBeUndefined()
    expect(harness.store.snapshot(PARENT).tasks).toEqual([])
  })

  it('answers at once when the setting keeps new tasks here', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    harness.setMode('here')
    const always = await harness.call({ initiation: 'offer', title: 'A' })

    expect(always?.success).toBe(true)
    expect(harness.store.snapshot(PARENT).tasks).toEqual([])
    expect(harness.notifyOffer).not.toHaveBeenCalled()
  })

  it('auto-starts new chats without notifying, up to the cap since the last user message', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')
    harness.setMode('newThread')

    for (let index = 0; index < MAX_AUTO_STARTS; index++) {
      const result = await harness.call({ initiation: 'offer', title: `Task ${index}` })
      expect(structured(result).outcome).toBe('accepted')
    }
    expect(harness.notifyOffer).not.toHaveBeenCalled()
    expect(harness.store.snapshot(PARENT).tasks.every((task) => task.state === 'accepted')).toBe(true)

    const asked = await pendingOffer(harness, 'Eleventh')
    expect(harness.notifyOffer).toHaveBeenCalledTimes(1)
    harness.tasks.observeUserRequest('turn/start', { threadId: PARENT })
    expect(structured(await asked.result).outcome).toBe('typedPast')

    const afterReset = await harness.call({ initiation: 'offer', title: 'After reset' })
    expect(structured(afterReset).outcome).toBe('accepted')
  })
})
