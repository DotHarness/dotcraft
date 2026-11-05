import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

const fake = vi.hoisted(() => ({
  handlers: new Map<string, (...args: any[]) => any>(),
  contexts: [] as any[],
  sessionListener: null as ((event: any, ownerId?: number) => void) | null,
  discarded: [] as number[],
  instances: 0
}))
vi.mock('electron', () => ({ ipcMain: {
  handle: (channel: string, listener: (...args: any[]) => any) => fake.handlers.set(channel, listener),
  removeHandler: (channel: string) => fake.handlers.delete(channel),
  on: vi.fn(), removeListener: vi.fn()
} }))
vi.mock('../VoiceMicrophonePermissions', () => ({ voiceMicrophonePermissions: { getStatus: vi.fn(), request: vi.fn(), openSettings: vi.fn() } }))
vi.mock('../VoiceModelManager', () => ({ VoiceModelManager: class { modelPath = '/model' } }))
vi.mock('../VoiceWorkerClient', () => ({ UtilityVoiceWorkerClient: class {} }))
vi.mock('../voicePaths', () => ({ resolveVoiceRoot: () => '/voice' }))
vi.mock('../VoiceChatGptTranscriber', () => ({ ChatGptVoiceTranscriber: class {
  constructor(readonly readAuth: (refresh: boolean) => Promise<unknown>) {}
} }))
vi.mock('../VoiceRuntimeService', () => ({
  VoiceRuntimeError: class extends Error { constructor(readonly code: string) { super(code) } },
  VoiceRuntimeService: class {
    constructor() { fake.instances += 1 }
    async initialize() {}
    onSnapshot() {}
    onSessionEvent(listener: (event: unknown, ownerId?: number) => void) { fake.sessionListener = listener }
    getSnapshot() { return { sessions: [], capacity: 2 } }
    async submitTranscription(_input: unknown, context: unknown) { fake.contexts.push(context); return { sessionId: `session-${fake.contexts.length}` } }
    async discardOwnerSessions(id: number) { fake.discarded.push(id) }
    async shutdown() {}
  }
}))

import { createWindowIpcMain } from '../../windowIpc'
import { registerVoiceIpc, shutdownVoiceService } from '../voiceIpc'

const releases: Array<() => void | Promise<void>> = []
afterEach(async () => {
  await Promise.all(releases.splice(0).map((release) => release()))
  await shutdownVoiceService()
  fake.contexts.length = 0
  fake.discarded.length = 0
  fake.instances = 0
})

function owner(id: number, authToken: string) {
  const sender = Object.assign(new EventEmitter(), { id, isDestroyed: () => false, send: vi.fn() })
  const window = Object.assign(new EventEmitter(), { webContents: sender, isDestroyed: () => false })
  const scope = createWindowIpcMain(window as never)
  let account = { authToken }
  const captureChatGptAuth = vi.fn(() => {
    const captured = account
    return async () => ({ authToken: captured.authToken })
  })
  const release = registerVoiceIpc({ webContents: sender as never, captureChatGptAuth, isChatGptTranscriptionEnabled: () => true }, scope)
  releases.push(release, scope.dispose)
  return {
    sender, scope, release, captureChatGptAuth,
    switchAuth: (next: string) => { account = { authToken: next } },
    refreshAuth: (next: string) => { account.authToken = next },

    invoke: (channel: string, ...args: unknown[]) => fake.handlers.get(channel)?.({ sender }, ...args)
  }
}

describe('voice IPC window routing', () => {
  it('captures each sender authentication and sends session events only to that owner', async () => {
    const a = owner(11, 'account-a')
    const b = owner(22, 'account-b')
    await Promise.all([a.invoke('voice:submit-transcription', {}), b.invoke('voice:submit-transcription', {})])
    expect(fake.instances).toBe(1)
    const first = fake.contexts.find((context) => context.ownerId === 11)
    const second = fake.contexts.find((context) => context.ownerId === 22)
    a.switchAuth('new-workspace-account')
    await expect(first.chatGpt.transcriber.readAuth(false)).resolves.toEqual({ authToken: 'account-a' })
    await expect(first.chatGpt.transcriber.readAuth(true)).resolves.toEqual({ authToken: 'account-a' })
    b.refreshAuth('refreshed-b')
    await expect(second.chatGpt.transcriber.readAuth(false)).resolves.toEqual({ authToken: 'refreshed-b' })
    fake.sessionListener?.({ type: 'completed', transcript: 'second' }, 22)
    expect(a.sender.send).not.toHaveBeenCalled()
    expect(b.sender.send).toHaveBeenCalledWith('voice:session-event', { type: 'completed', transcript: 'second' })
    await a.release()
    a.scope.dispose()
    expect(first.signal.aborted).toBe(true)
    expect(second.signal.aborted).toBe(false)
    expect(fake.discarded).toEqual([11])
    await b.invoke('voice:submit-transcription', {})
    expect(fake.contexts.at(-1).ownerId).toBe(22)
  })
})
