import { afterEach, describe, expect, it } from 'vitest'
import type { FakeComputer } from '../demo/fakeComputer'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { buildTranscript } from './transcript'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

function setup(computer: FakeComputer): Harness {
  const harness = createHarness([computer])
  harnesses.push(harness)
  return harness
}

function studio(): FakeComputer {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  return computer
}

async function online(harness: Harness): Promise<void> {
  await harness.session.boot()
  await waitFor(() => harness.computerState().link === 'online' && !harness.computerState().syncing)
}

function keyOf(harness: Harness, title: string): string {
  return Object.values(harness.computerState().chats).find((chat) => chat.title === title)!.key
}

describe('pull to refresh', () => {
  it('reloads projects, chat lists, and the open chat while online, and resolves once they are in', async () => {
    const computer = studio()
    const harness = setup(computer)
    await online(harness)
    const key = keyOf(harness, 'Explain the release script')
    harness.link().openChat(key)
    await waitFor(() => harness.computerState().details[key]?.loading === false)

    const { project, thread } = computer.thread(harness.computerState().chats[key].threadId)
    thread.displayName = 'Explain the release script again'
    computer.addItem(thread, thread.turns[0].id, 'agentMessage', { text: 'Added while the phone looked away.' })
    const chats = computer.projects.find((entry) => entry.name === 'Chats')!
    chats.running = true

    await harness.link().refresh(key)
    const state = harness.computerState()
    expect(state.chats[key].title).toBe('Explain the release script again')
    expect(state.projects.find((entry) => entry.id === chats.id)?.running).toBe(true)
    expect(state.phases[chats.id]).toBe('ready')
    expect(state.phases[project.id]).toBe('ready')
    const texts = buildTranscript(state.details[key].history).filter((entry) => entry.kind === 'assistant')
    expect(texts.some((entry) => entry.text === 'Added while the phone looked away.')).toBe(true)
  })

  it('reconnects an offline computer at once instead of waiting for the backoff', async () => {
    const computer = studio()
    computer.reachable = false
    const harness = setup(computer)
    await harness.session.boot()
    await waitFor(() => harness.computerState().link === 'offline' && harness.link().reconnectPending)

    computer.reachable = true
    await harness.link().refresh()
    expect(harness.computerState()).toMatchObject({ link: 'online', syncing: false })
    expect(harness.link().reconnectPending).toBe(false)
  })

  it('reconnects a computer whose phone access is off and resolves when the attempt fails again', async () => {
    const computer = studio()
    const harness = setup(computer)
    await online(harness)
    computer.turnGatewayOff()
    await waitFor(() => harness.computerState().accessOff && harness.link().reconnectPending)
    const links: string[] = []
    const unsubscribe = harness.session.store.subscribe(() => links.push(harness.computerState().link))

    await harness.link().refresh()
    unsubscribe()
    expect(links).toContain('connecting')
    expect(harness.computerState()).toMatchObject({ accessOff: true, link: 'offline' })
    expect(harness.link().reconnectPending).toBe(true)
  })
})
