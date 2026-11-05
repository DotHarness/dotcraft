import type { WebContents } from 'electron'
import { join } from 'path'

import type { VoiceChatGptState, VoiceTranscriptionInput } from '../../shared/voice'
import type { WindowIpcMain } from '../windowIpc'
import { ChatGptVoiceTranscriber, type ReadVoiceChatGptAuth } from './VoiceChatGptTranscriber'
import { VoiceModelManager } from './VoiceModelManager'
import { VoiceRuntimeError, VoiceRuntimeService } from './VoiceRuntimeService'
import { UtilityVoiceWorkerClient } from './VoiceWorkerClient'
import { resolveVoiceRoot } from './voicePaths'
import { voiceMicrophonePermissions } from './VoiceMicrophonePermissions'

export interface VoiceHost {
  webContents: WebContents
  captureChatGptAuth(): ReadVoiceChatGptAuth
  isChatGptTranscriptionEnabled(): boolean
}

interface VoiceOwner {
  host: VoiceHost
  controller: AbortController
  signedIn: boolean
  refreshGeneration: number
}

let servicePromise: Promise<VoiceRuntimeService> | null = null
const owners = new Map<number, VoiceOwner>()

export function registerVoiceIpc(host: VoiceHost, ipc: WindowIpcMain): () => Promise<void> {
  const id = host.webContents.id
  const owner: VoiceOwner = { host, controller: new AbortController(), signedIn: false, refreshGeneration: 0 }
  owners.set(id, owner)
  let released = false
  const release = async (): Promise<void> => {
    if (released) return
    released = true
    owner.controller.abort()
    if (owners.get(id) === owner) owners.delete(id)
    const service = await servicePromise?.catch(() => null)
    await service?.discardOwnerSessions(id)
  }
  host.webContents.once('destroyed', () => { void release() })

  ipc.handle('voice:get-microphone-permission-status', () => voiceMicrophonePermissions.getStatus())
  ipc.handle('voice:request-microphone-permission', () => voiceMicrophonePermissions.request())
  ipc.handle('voice:open-microphone-settings', () => voiceMicrophonePermissions.openSettings())
  ipc.handle('voice:get-snapshot', () => withService(owner, async (service) => {
    await refreshOwner(owner)
    return snapshotFor(service, owner)
  }))
  ipc.handle('voice:install-model', () => withService(owner, (service) => service.installModel()))
  ipc.handle('voice:cancel-model-install', () => withService(owner, (service) => service.cancelModelInstall()))
  ipc.handle('voice:remove-model', () => withService(owner, (service) => service.removeModel()))
  ipc.handle('voice:repair-model', () => withService(owner, (service) => service.repairModel()))
  ipc.handle('voice:submit-transcription', (_event, input: VoiceTranscriptionInput) => {
    const readAuth = host.captureChatGptAuth()
    return withService(owner, (service) => service.submitTranscription(input, {
      ownerId: id,
      signal: owner.controller.signal,
      chatGpt: {
        transcriber: new ChatGptVoiceTranscriber(readAuth),
        isSignedIn: async () => (await readAuth(false)) !== null,
        isEnabled: () => host.isChatGptTranscriptionEnabled()
      }
    }))
  })
  ipc.handle('voice:retry-transcription', (_event, sessionId: string) => withService(owner, (service) => (
    service.retryTranscription(validateSessionId(sessionId), id)
  )))
  ipc.handle('voice:discard-session', (_event, sessionId: string) => withService(owner, (service) => (
    service.discardSession(validateSessionId(sessionId), id)
  )))
  return release
}

export function refreshVoiceChatGptAvailability(ownerId?: number): void {
  if (!servicePromise) return
  for (const [id, owner] of owners) {
    if (ownerId === undefined || id === ownerId) void refreshOwner(owner)
  }
}

export async function shutdownVoiceService(): Promise<void> {
  for (const owner of owners.values()) owner.controller.abort()
  owners.clear()
  const service = await servicePromise?.catch(() => null)
  if (service) await service.shutdown()
  servicePromise = null
}

async function getService(): Promise<VoiceRuntimeService> {
  if (servicePromise) return servicePromise
  const voiceRoot = resolveVoiceRoot()
  const modelManager = new VoiceModelManager({ voiceRoot })
  const service = new VoiceRuntimeService({
    voiceRoot,
    modelManager,
    localTranscriber: new UtilityVoiceWorkerClient({
      modulePath: join(__dirname, 'voiceWorker.js'),
      modelPath: modelManager.modelPath
    })
  })
  service.onSnapshot(() => {
    for (const owner of owners.values()) send(owner, 'voice:snapshot', snapshotFor(service, owner))
  })
  service.onSessionEvent((event, ownerId) => {
    const owner = ownerId === undefined ? undefined : owners.get(ownerId)
    if (owner) send(owner, 'voice:session-event', event)
  })
  servicePromise = service.initialize().then(() => service)
  return servicePromise
}

async function refreshOwner(owner: VoiceOwner): Promise<void> {
  const generation = ++owner.refreshGeneration
  const signedIn = await owner.host.captureChatGptAuth()(false).then((auth) => auth !== null).catch(() => false)
  if (generation !== owner.refreshGeneration || owner.controller.signal.aborted) return
  owner.signedIn = signedIn
  const service = await servicePromise?.catch(() => null)
  if (service) send(owner, 'voice:snapshot', snapshotFor(service, owner))
}

function snapshotFor(service: VoiceRuntimeService, owner: VoiceOwner) {
  const chatGpt: VoiceChatGptState = {
    signedIn: owner.signedIn,
    enabled: owner.host.isChatGptTranscriptionEnabled()
  }
  return { ...service.getSnapshot(owner.host.webContents.id), chatGpt }
}

async function withService<T>(owner: VoiceOwner, operation: (service: VoiceRuntimeService) => T | Promise<T>): Promise<T> {
  try {
    owner.controller.signal.throwIfAborted()
    const service = await getService()
    owner.controller.signal.throwIfAborted()
    return await operation(service)
  } catch (error) {
    if (error instanceof VoiceRuntimeError) throw new Error(error.code)
    throw error
  }
}

function validateSessionId(value: unknown): string {
  if (typeof value !== 'string' || !/^[0-9a-f-]{36}$/i.test(value)) throw new VoiceRuntimeError('invalid-audio')
  return value
}

function send(owner: VoiceOwner, channel: string, payload: unknown): void {
  if (!owner.controller.signal.aborted && !owner.host.webContents.isDestroyed()) owner.host.webContents.send(channel, payload)
}
