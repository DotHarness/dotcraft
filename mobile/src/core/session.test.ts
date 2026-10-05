import { afterEach, describe, expect, it } from 'vitest'
import { FakeComputer, type FakeThread } from '../demo/fakeComputer'
import { FakeRelay } from '../demo/fakeNetwork'
import { buildBoxSeed, createStudio, pairingUrl, type StudioOptions } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { plainMessage } from './draft'
import { parsePairingUrl } from './pairing'
import { needsYou } from './chatState'
import { computerStatus, runningChats, stateOf, type ComputerState } from './state'
import { startConfig } from './threadConfig'
import { buildTranscript } from './transcript'

const harnesses: Harness[] = []

function setup(computers: FakeComputer[], options?: Parameters<typeof createHarness>[1]): Harness & { state: () => ComputerState } {
  const harness = createHarness(computers, options)
  harnesses.push(harness)
  return { ...harness, state: () => harness.computerState() }
}

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

function studio(options?: StudioOptions): FakeComputer {
  const computer = createStudio(new Date(), options)
  computer.streamDelayMs = 1
  return computer
}

function waiting(state: ComputerState) {
  return runningChats(state).filter((chat) => needsYou(stateOf(chat)))
}

function keyOf(state: ComputerState, title: string): string {
  const chat = Object.values(state.chats).find((entry) => entry.title === title)
  if (!chat) throw new Error(`No chat titled ${title}`)
  return chat.key
}

async function online(harness: ReturnType<typeof setup>): Promise<ComputerState> {
  await harness.session.boot()
  await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
  return harness.state()
}

function methods(computer: FakeComputer): string[] {
  return computer.calls.map((call) => call.method)
}

function offer(computer: FakeComputer, code: string) {
  return parsePairingUrl(pairingUrl(computer, code))!
}

describe('connecting', () => {
  it('shows every waiting approval and question across the running projects', async () => {
    const harness = setup([studio()])
    const state = await online(harness)
    expect(waiting(state).map((chat) => chat.title).sort()).toEqual([
      'Retune the sidebar icon motion',
      'Set up the docs site build',
      'Upgrade Vite to 6.4',
    ])
    expect(Object.values(state.chats).some((chat) => chat.title === 'Summarize yesterday’s release notes')).toBe(false)
    expect(state.computer?.lastAddress).toBe('192.168.1.20')
  })

  it('initializes each relay as an approval-capable, user-input, streaming client', async () => {
    const computer = studio()
    const harness = setup([computer])
    await online(harness)
    const initialize = computer.calls.find((call) => call.method === 'initialize')
    expect(initialize?.params.capabilities).toMatchObject({ approvalSupport: true, requestUserInputSupport: true, streamingSupport: true })
    expect(harness.network.sockets.every((socket) => socket.url.startsWith('wss://') && !socket.url.includes('credential'))).toBe(true)
  })

  it('shows an older chat that starts waiting after connecting, but never a subagent', async () => {
    const computer = studio()
    const harness = setup([computer])
    const state = await online(harness)
    const { project } = computer.thread(state.chats[keyOf(state, 'Upgrade Vite to 6.4')].threadId)
    const now = new Date().toISOString()
    const unlisted = (id: string, source: FakeThread['source']): FakeThread => ({
      id,
      displayName: id,
      profileId: null,
      createdAt: now,
      lastActiveAt: now,
      turns: [{ id: 'turn_001', threadId: id, status: 'running', startedAt: now }],
      items: [],
      pending: [],
      stream: null,
      continuation: '',
      source,
    })
    project.threads.push(unlisted('thread_subagent', 'subagent'), unlisted('thread_older', 'user'))
    for (const id of ['thread_subagent', 'thread_older']) {
      computer.ask(id, { kind: 'approval', requestId: `request_${id}`, approvalType: 'shell', operation: 'npm test', target: 'npm test', reason: '' })
    }
    await waitFor(() => waiting(harness.state()).some((chat) => chat.threadId === 'thread_older'))
    expect(Object.values(harness.state().chats).some((chat) => chat.threadId === 'thread_subagent')).toBe(false)
  })
})

describe('relay', () => {
  const tunnel = 'wss://relay.example.com/r/connect?host=studio-pc'

  function relayed(computer: FakeComputer): FakeRelay {
    const relay = new FakeRelay('relay.example.com')
    relay.host(computer)
    return relay
  }

  it('reaches the computer through the relay when its addresses are unreachable and records that path', async () => {
    const computer = studio()
    const relay = relayed(computer)
    computer.reachable = false
    const harness = setup([computer])
    harness.network.relays = [relay]
    const state = await online(harness)
    expect(state.computer).toMatchObject({ lastAddress: 'relay', relay: { url: 'https://relay.example.com' } })
    expect(waiting(state)).toHaveLength(3)
    expect(harness.network.sockets.length).toBeGreaterThan(1)
    expect(harness.network.sockets.every((socket) => socket.tunnel === tunnel && socket.url.startsWith('wss://127.0.0.1:47610/'))).toBe(true)
  })

  it('learns the relay from /m/hello and falls back to it after the computer leaves the network', async () => {
    const computer = studio()
    const harness = setup([computer])
    harness.network.relays = [relayed(computer)]
    await online(harness)
    expect(harness.state().computer).toMatchObject({ lastAddress: '192.168.1.20', relay: { url: 'https://relay.example.com' } })

    computer.reachable = false
    computer.dropConnections()
    await waitFor(() => harness.state().link === 'connecting' && harness.link().reconnectPending)
    harness.timers.runAll()
    await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
    expect(harness.state().computer?.lastAddress).toBe('relay')
  })

  it('pairs through the relay in the pairing code when the computer is on another network', async () => {
    const computer = studio()
    const relay = relayed(computer)
    computer.reachable = false
    const harness = setup([computer], { paired: 0 })
    harness.network.relays = [relay]
    await harness.session.boot()
    await harness.session.beginPairing(offer(computer, 'demo-code'))
    await harness.session.allow()
    expect(harness.state().computer).toMatchObject({ lastAddress: 'relay', relay: { url: 'https://relay.example.com' } })
    await waitFor(() => harness.state().link === 'online')
  })

  it('shows the computer offline when the relay answers hostOffline', async () => {
    const computer = studio()
    const relay = relayed(computer)
    relay.hosts.clear()
    computer.reachable = false
    const harness = setup([computer])
    harness.network.relays = [relay]
    await harness.session.boot()
    await waitFor(() => harness.state().link === 'offline')
    expect(harness.network.requests.map((request) => request.tunnel ?? request.url)).toEqual([
      'https://192.168.1.20:47610/m/hello',
      'https://100.101.102.103:47610/m/hello',
      tunnel,
    ])
    expect(harness.state().identityChanged).toBe(false)
    expect(harness.state().computer).not.toBeNull()
    expect(harness.link().reconnectPending).toBe(true)
  })
})

describe('approvals and questions', () => {
  it('replays a pending approval when the chat opens and answers Allow once with accept', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Upgrade Vite to 6.4')
    harness.link().openChat(key)
    await waitFor(() => (harness.state().pending[key]?.length ?? 0) === 1)
    const request = harness.state().pending[key][0]
    expect(request).toMatchObject({ kind: 'approval', approvalType: 'shell', operation: 'pnpm add -D vite@6.4.3 @vitejs/plugin-react@4.3.4' })
    harness.link().decide(key, request.requestId, 'once')
    await waitFor(() => computer.decisions.length === 1)
    expect(computer.decisions[0].decision).toBe('accept')
    expect(harness.state().pending[key]).toBeUndefined()
    await waitFor(() => stateOf(harness.state().chats[key]) === 'done')
    const transcript = buildTranscript(harness.state().details[key].history)
    const unfolded = transcript.flatMap((entry) => (entry.kind === 'activity' ? entry.children : [entry]))
    expect(unfolded.some((entry) => entry.kind === 'notice' && entry.notice === 'allowedOnce')).toBe(true)
  })

  it('maps Allow for session and Reject to acceptForSession and decline', async () => {
    const computer = studio()
    const harness = setup([computer])
    const state = await online(harness)
    const vite = keyOf(state, 'Upgrade Vite to 6.4')
    const motion = keyOf(state, 'Retune the sidebar icon motion')
    harness.link().openChat(vite)
    harness.link().openChat(motion)
    await waitFor(() => Boolean(harness.state().pending[vite] && harness.state().pending[motion]))
    const pending = harness.state().pending
    harness.link().decide(vite, pending[vite][0].requestId, 'session')
    harness.link().decide(motion, pending[motion][0].requestId, 'reject')
    await waitFor(() => computer.decisions.length === 2)
    expect(computer.decisions.map((entry) => entry.decision).sort()).toEqual(['acceptForSession', 'decline'])
  })

  it('answers a question with the chosen option', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Set up the docs site build')
    harness.link().openChat(key)
    await waitFor(() => Boolean(harness.state().pending[key]))
    const request = harness.state().pending[key][0]
    expect(request.kind).toBe('question')
    harness.link().answer(key, request.requestId, { package_manager: ['pnpm'] })
    await waitFor(() => computer.answers.length === 1)
    expect(computer.answers[0].answers).toEqual({ package_manager: { answers: ['pnpm'] } })
  })

  it('dismisses the card when the computer answers first', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Upgrade Vite to 6.4')
    harness.link().openChat(key)
    await waitFor(() => Boolean(harness.state().pending[key]))
    computer.answerFromComputer(harness.state().chats[key].threadId, { decision: 'accept' })
    await waitFor(() => harness.state().pending[key] === undefined)
    expect(computer.decisions).toHaveLength(1)
  })

  it('dismisses the card when the computer answered while the chat was closed', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Upgrade Vite to 6.4')
    harness.link().openChat(key)
    await waitFor(() => Boolean(harness.state().pending[key]))
    harness.link().closeChat(key)
    await waitFor(() => methods(computer).includes('thread/unsubscribe'))
    computer.answerFromComputer(harness.state().chats[key].threadId, { decision: 'accept' })
    harness.link().openChat(key)
    await waitFor(() => harness.state().pending[key] === undefined)
  })
})

describe('turn control', () => {
  it('adds a message to a running turn with turn/steer and stops it with turn/interrupt', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Fix the flaky turn-diff test')
    harness.link().openChat(key)
    await waitFor(() => harness.state().details[key]?.loading === false)
    await harness.link().send(key, plainMessage('Also run the lint step.'))
    expect(methods(computer)).toContain('turn/steer')
    expect(methods(computer)).not.toContain('turn/enqueue')
    await waitFor(() =>
      buildTranscript(harness.state().details[key].history).some(
        (entry) => entry.kind === 'user' && entry.added && entry.text === 'Also run the lint step.' && !entry.id.startsWith('echo-'),
      ),
    )
    await harness.link().stop(key)
    expect(methods(computer)).toContain('turn/interrupt')
    await waitFor(() => stateOf(harness.state().chats[key]) === 'done')
    const transcript = buildTranscript(harness.state().details[key].history)
    expect(transcript.some((entry) => entry.kind === 'activity' && entry.status === 'stopped')).toBe(true)
  })

  it('starts a new chat in a stopped project by ensuring it, with project defaults', async () => {
    const computer = studio()
    const harness = setup([computer])
    const state = await online(harness)
    const chats = state.projects.find((project) => project.name === 'Chats')!
    expect(chats.running).toBe(false)
    const key = await harness.link().newChat(chats.id, 'summarize the open pull requests')
    await harness.link().send(key, plainMessage('summarize the open pull requests'))
    expect(harness.state().projects.find((project) => project.id === chats.id)?.running).toBe(true)
    const order = methods(computer).filter((method) => ['thread/start', 'thread/subscribe', 'turn/start'].includes(method))
    expect(order.slice(-3)).toEqual(['thread/start', 'thread/subscribe', 'turn/start'])
    expect(computer.calls.find((call) => call.method === 'thread/start')!.params.config).toBeUndefined()
    expect(harness.state().chats[key].title).toBe('Summarize the open pull requests')
    await waitFor(() => stateOf(harness.state().chats[key]) === 'done')
  })

  it('sends the whole read configuration with one changed field to thread/config/update', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Explain the release script')
    harness.link().openChat(key)
    await waitFor(() => harness.state().details[key]?.loading === false)
    await harness.link().updateConfig(key, { kind: 'approval', policy: 'autoApprove' })
    const update = computer.calls.find((call) => call.method === 'thread/config/update')!
    expect(update.params.config).toEqual({
      providerId: 'studio',
      model: 'atlas-2',
      reasoning: { enabled: true, effort: 'high', output: 'full' },
      speed: 'standard',
      agentProfileId: null,
      approvalPolicy: 'autoApprove',
    })
    expect(harness.state().details[key].config?.approvalPolicy).toBe('autoApprove')
  })

  it('names the chosen model for a new chat and passes only the other controls that changed', async () => {
    const computer = studio()
    const harness = setup([computer])
    const state = await online(harness)
    const project = state.projects.find((entry) => entry.name === 'dotcraft')!
    const controls = { providerId: 'studio', model: 'atlas-2', reasoning: 'high', speed: 'fast', approvalPolicy: 'prompt' } as const
    await harness.link().newChat(project.id, 'tidy the release script', startConfig({ touched: { speed: true }, controls }))
    expect(computer.calls.find((call) => call.method === 'thread/start')!.params.config).toEqual({ providerId: 'studio', model: 'atlas-2', speed: 'fast' })
  })

  it('starts a new chat on the model chosen for the switched provider', async () => {
    const computer = studio()
    const harness = setup([computer])
    const state = await online(harness)
    const project = state.projects.find((entry) => entry.name === 'dotcraft')!
    const controls = { providerId: 'local', model: 'quill-14b', reasoning: 'default', speed: 'standard', approvalPolicy: 'prompt' } as const
    const key = await harness.link().newChat(project.id, 'tidy the release script', startConfig({ touched: {}, controls }))
    expect(computer.calls.find((call) => call.method === 'thread/start')!.params.config).toEqual({ providerId: 'local', model: 'quill-14b' })
    harness.link().openChat(key)
    await waitFor(() => harness.state().details[key]?.config?.model === 'quill-14b')
  })

  it('forks a chat into a new chat in the same project and archives a chat off the lists', async () => {
    const harness = setup([studio()])
    const key = keyOf(await online(harness), 'Explain the release script')
    const forked = await harness.link().fork(key)
    expect(forked).not.toBe(key)
    expect(harness.state().chats[forked]).toMatchObject({ projectId: harness.state().chats[key].projectId, title: 'Explain the release script' })

    await harness.link().archive(key)
    expect(harness.state().chats[key]).toBeUndefined()
    expect(harness.state().chats[forked]).toBeDefined()
  })
})

describe('reconnect and catch-up', () => {
  it('catches up after a drop without duplicates and replays the waiting approval', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Upgrade Vite to 6.4')
    harness.link().openChat(key)
    await waitFor(() => Boolean(harness.state().pending[key]))
    const before = harness.state().details[key].history.items.length

    computer.dropConnections()
    await waitFor(() => harness.state().link === 'connecting' && harness.link().reconnectPending)
    expect(harness.state().pending[key]).toBeUndefined()
    expect(harness.timers.delays().some((ms) => ms >= 1_000 && ms <= 30_000)).toBe(true)

    const { thread } = computer.thread(harness.state().chats[key].threadId)
    computer.addItem(thread, thread.turns[0].id, 'agentMessage', { text: 'Checked the lockfile while you were away.' })

    harness.timers.runAll()
    await waitFor(() => harness.state().link === 'online' && harness.state().pending[key]?.length === 1)
    await waitFor(() => harness.state().details[key].loading === false)
    const items = harness.state().details[key].history.items
    expect(items).toHaveLength(before + 1)
    expect(new Set(items.map((item) => `${item.turnId}/${item.id}`)).size).toBe(items.length)
    const texts = buildTranscript(harness.state().details[key].history).filter((entry) => entry.kind === 'assistant')
    expect(texts.filter((entry) => entry.text === 'Checked the lockfile while you were away.')).toHaveLength(1)
  })

  it('keeps an earlier turn intact when the next turn restarts its item ids, live and after catch-up', async () => {
    const computer = studio()
    const harness = setup([computer])
    const key = keyOf(await online(harness), 'Rename the settings segments')
    harness.link().openChat(key)
    await waitFor(() => harness.state().details[key]?.loading === false)
    const transcript = () => buildTranscript(harness.state().details[key].history)
    const earlier = transcript()
    const reply = 'Sure, here’s more detail.'

    await harness.link().send(key, plainMessage('Check the French names too.'))
    await waitFor(
      () => !harness.state().chats[key].runtime?.running && transcript().some((entry) => entry.kind === 'assistant' && entry.text === reply && !entry.streaming),
    )
    const live = transcript()
    expect(live.slice(0, earlier.length)).toEqual(earlier)
    expect(live.slice(earlier.length).map((entry) => [entry.kind, 'text' in entry ? entry.text : null])).toEqual([
      ['user', 'Check the French names too.'],
      ['activity', null],
      ['assistant', reply],
    ])

    computer.dropConnections()
    await waitFor(() => harness.state().link === 'connecting' && harness.link().reconnectPending)
    harness.timers.runAll()
    await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
    await waitFor(() => harness.state().details[key].loading === false)
    expect(transcript()).toEqual(live)
  })

  it('keeps an unreachable computer offline while it retries in the background', async () => {
    const computer = studio()
    computer.reachable = false
    const harness = setup([computer])
    await harness.session.boot()
    await waitFor(() => harness.state().link === 'offline' && harness.link().reconnectPending)
    harness.timers.runAll()
    expect(harness.state().link).toBe('offline')
    await waitFor(() => harness.link().reconnectPending)
    expect(harness.state().link).toBe('offline')

    computer.reachable = true
    harness.timers.runAll()
    expect(harness.state().link).toBe('offline')
    await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
  })

  it('ignores a repeated network report that does not change the network, so an offline computer stays offline', async () => {
    const computer = studio()
    computer.reachable = false
    const harness = setup([computer])
    await harness.session.boot()
    await waitFor(() => harness.state().link === 'offline')
    harness.session.networkChanged('WIFI/true/true')
    await waitFor(() => harness.state().link === 'offline' && harness.link().reconnectPending)
    harness.session.networkChanged('WIFI/true/true')
    expect(harness.state().link).toBe('offline')
  })

  it('reconnects at once on a network change or a return to the foreground, and closes everything in the background', async () => {
    const computer = studio()
    computer.reachable = false
    const harness = setup([computer])
    await harness.session.boot()
    await waitFor(() => harness.state().link === 'offline')
    expect(harness.link().reconnectPending).toBe(true)
    computer.reachable = true
    harness.session.networkChanged('WIFI/true/true')
    await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
    expect(computer.connectionCount).toBeGreaterThan(0)

    harness.session.setForeground(false)
    await waitFor(() => computer.connectionCount === 0)
    harness.session.setForeground(true)
    await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
    expect(computer.connectionCount).toBeGreaterThan(0)
  })
})

describe('failure states', () => {
  it('keeps the last synced chats and remembers that phone access is off across a restart', async () => {
    const computer = studio()
    const harness = setup([computer])
    await online(harness)
    computer.turnGatewayOff()
    await waitFor(() => computerStatus(harness.state()) === 'access-off')
    expect(Object.keys(harness.state().chats).length).toBeGreaterThan(0)
    harness.session.setForeground(false)
    expect(harness.storage.value?.computers[computer.id].accessOff).toBe(true)

    const restarted = setup([computer], { storage: harness.storage })
    await restarted.session.boot()
    await waitFor(() => restarted.state().link === 'offline')
    expect(computerStatus(restarted.state())).toBe('access-off')
    computer.gatewayOn = true
    restarted.session.networkChanged('WIFI/true/true')
    await waitFor(() => computerStatus(restarted.state()) === 'online')
    expect(restarted.state().accessOff).toBe(false)
  })

  it('forgets the computer when it revokes this phone', async () => {
    const computer = studio()
    const harness = setup([computer])
    await online(harness)
    computer.revokeAll()
    await waitFor(() => harness.session.store.getState().order.length === 0)
    expect(harness.session.store.getState().revokedBy).toBe('Studio PC')
    expect(harness.credentials.values.size).toBe(0)
    expect(harness.storage.value).toBeNull()
  })

  it('stops connecting and reports identity changed when the certificate no longer matches', async () => {
    const computer = studio()
    const harness = setup([computer])
    computer.certificate = 'b'.repeat(64)
    await harness.session.boot()
    await waitFor(() => harness.state().identityChanged)
    expect(harness.link().reconnectPending).toBe(false)
    await harness.session.removeComputer(computer.id)
    expect(harness.session.store.getState().order).toEqual([])
  })

  it('makes a stopped project read-only and starts it again on request', async () => {
    const computer = studio()
    const harness = setup([computer])
    const state = await online(harness)
    const lab = state.projects.find((project) => project.name === 'design-lab')!
    computer.stopProject(lab.id)
    await waitFor(() => harness.state().projects.find((project) => project.id === lab.id)?.running === false)
    expect(harness.state().phases[lab.id]).toBeUndefined()
    expect(await harness.link().startProject(lab.id)).toBe(true)
    expect(harness.state().phases[lab.id]).toBe('ready')
  })

  it('says a project cannot start when its start fails', async () => {
    const computer = studio({ cantStart: ['chats'] })
    const harness = setup([computer])
    const state = await online(harness)
    const chats = state.projects.find((project) => project.name === 'Chats')!
    expect(await harness.link().startProject(chats.id)).toBe(false)
    expect(harness.state().phases[chats.id]).toBe('cantStart')
  })
})

describe('pairing', () => {
  it('pairs after Allow, stores the credential, and connects', async () => {
    const computer = studio()
    const harness = setup([computer], { paired: 0 })
    await harness.session.boot()
    await harness.session.beginPairing(offer(computer, 'demo-code'))
    expect(harness.session.store.getState().pairing.step).toBe('allow')
    await harness.session.allow()
    expect(harness.session.store.getState().pairing).toEqual({ step: 'connected', name: 'Studio PC' })
    expect(harness.credentials.values.get(computer.id)).toMatch(/^credential_/)
    await waitFor(() => harness.state().link === 'online')
    expect(harness.storage.value?.computers[computer.id].computer.name).toBe('Studio PC')
  })

  it('asks for a new code when the pairing code was used', async () => {
    const computer = studio()
    const harness = setup([computer], { paired: 0 })
    await harness.session.boot()
    await harness.session.beginPairing(offer(computer, 'used-code'))
    await harness.session.allow()
    expect(harness.session.store.getState().pairing.step).toBe('invalid')
    expect(harness.session.store.getState().order).toEqual([])
  })

  it('adds another computer once it is allowed, selects it, and keeps the first one connected', async () => {
    const current = studio()
    const next = new FakeComputer(buildBoxSeed(new Date()))
    const harness = setup([current, next])
    await online(harness)
    await harness.session.beginPairing(offer(next, 'build-box-code'))
    harness.session.resetPairing()
    expect(harness.session.store.getState().order).toEqual([current.id])
    await harness.session.beginPairing(offer(next, 'build-box-code'))
    await harness.session.allow()
    expect(harness.session.store.getState()).toMatchObject({ order: [current.id, next.id], selected: next.id })
    await waitFor(() => harness.computerState(next.id).link === 'online')
    expect(harness.state().link).toBe('online')
    expect(current.removedDevices).toEqual([])
  })

  it('replaces the pairing of a computer scanned again and removes the old device from it', async () => {
    const computer = studio()
    const harness = setup([computer])
    await online(harness)
    computer.addPairingCode('again')
    await harness.session.beginPairing(offer(computer, 'again'))
    await harness.session.allow()
    expect(harness.session.store.getState().order).toEqual([computer.id])
    await waitFor(() => computer.removedDevices.includes('dev_demo'))
    await waitFor(() => harness.state().link === 'online' && !harness.state().syncing)
  })
})

describe('several computers', () => {
  it('keeps every computer connected with its own chats, and one revoking this phone leaves the other working', async () => {
    const first = studio()
    const second = studio({ id: 'office-pc', name: 'Office PC', addresses: ['192.168.1.30'] })
    const harness = setup([first, second], { paired: 2 })
    await harness.session.boot()
    await waitFor(() => [first.id, second.id].every((id) => harness.computerState(id).link === 'online' && !harness.computerState(id).syncing))
    expect(Object.keys(harness.computerState(second.id).chats).length).toBeGreaterThan(0)
    harness.session.select(second.id)
    expect(harness.session.store.getState().selected).toBe(second.id)

    second.revokeAll()
    await waitFor(() => harness.session.store.getState().order.length === 1)
    expect(harness.session.store.getState()).toMatchObject({ order: [first.id], selected: first.id, revokedBy: 'Office PC' })
    expect([...harness.credentials.values.keys()]).toEqual([first.id])
    expect(harness.state().link).toBe('online')
    expect(first.connectionCount).toBeGreaterThan(0)
  })

  it('shows an identity change on one computer while the other stays connected', async () => {
    const first = studio()
    const second = studio({ id: 'office-pc', name: 'Office PC', addresses: ['192.168.1.30'] })
    const harness = setup([first, second], { paired: 2 })
    second.certificate = 'c'.repeat(64)
    await harness.session.boot()
    await waitFor(() => harness.computerState(second.id).identityChanged && harness.state().link === 'online')
    await harness.session.removeComputer(second.id)
    expect(harness.session.store.getState().order).toEqual([first.id])
    expect(harness.state().link).toBe('online')
  })
})
