import { afterEach, describe, expect, it } from 'vitest'
import type { FakeThread } from '../demo/fakeComputer'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function connected(prepare: (thread: FakeThread) => void) {
  const computer = createStudio(new Date())
  const thread = computer.projects.flatMap((project) => project.threads).find((candidate) => !candidate.archived && candidate.source !== 'subagent')!
  prepare(thread)
  const harness = createHarness([computer])
  harnesses.push(harness)
  const state = () => harness.computerState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  const key = Object.values(state().chats).find((chat) => chat.threadId === thread.id)!.key
  const updates = () => computer.calls.filter((call) => call.method === 'thread/readState/update')
  return { computer, harness, thread, key, state, updates }
}

describe('chat read receipts', () => {
  it('marks an unread chat read with the revision the phone saw when it is opened', async () => {
    const { harness, key, state, updates } = await connected((thread) => {
      thread.readState = { firstUnread: { type: 'turn', turnId: thread.turns[0].id }, revision: 'r1' }
    })
    expect(state().chats[key].readState?.firstUnread).not.toBeNull()

    harness.link().openChat(key)
    await waitFor(() => state().chats[key].readState?.firstUnread === null)

    expect(updates().map((call) => call.params)).toEqual([
      expect.objectContaining({ expectedRevision: 'r1', operation: { type: 'read' } }),
    ])
  })

  it('keeps an unread mark made elsewhere on the open chat until it is opened again', async () => {
    const { computer, harness, thread, key, state, updates } = await connected((candidate) => {
      candidate.readState = { firstUnread: null, revision: 'r1' }
    })
    harness.link().openChat(key)

    computer.setReadState(thread, { firstUnread: { type: 'threadStart' } })
    await waitFor(() => state().chats[key].readState?.firstUnread?.type === 'threadStart')
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(updates()).toEqual([])

    harness.link().closeChat(key)
    harness.link().openChat(key)
    await waitFor(() => state().chats[key].readState?.firstUnread === null)
    expect(updates()).toHaveLength(1)
  })
})
