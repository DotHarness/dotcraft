import { BrowserWindow, ipcMain } from 'electron'
import { join } from 'path'

import type { VoiceTranscriptionInput } from '../../shared/voice'
import { VoiceModelManager } from './VoiceModelManager'
import { VoiceRuntimeError, VoiceRuntimeService } from './VoiceRuntimeService'
import { UtilityVoiceWorkerClient } from './VoiceWorkerClient'
import { resolveVoiceRoot } from './voicePaths'
import { voiceMicrophonePermissions } from './VoiceMicrophonePermissions'

const HANDLERS = [
  'voice:get-microphone-permission-status',
  'voice:request-microphone-permission',
  'voice:open-microphone-settings',
  'voice:get-snapshot',
  'voice:install-model',
  'voice:cancel-model-install',
  'voice:remove-model',
  'voice:repair-model',
  'voice:submit-transcription',
  'voice:retry-transcription',
  'voice:discard-session'
] as const

let servicePromise: Promise<VoiceRuntimeService> | null = null

export function registerVoiceIpc(): void {
  for (const channel of HANDLERS) ipcMain.removeHandler(channel)

  ipcMain.handle('voice:get-microphone-permission-status', () => voiceMicrophonePermissions.getStatus())
  ipcMain.handle('voice:request-microphone-permission', () => voiceMicrophonePermissions.request())
  ipcMain.handle('voice:open-microphone-settings', () => voiceMicrophonePermissions.openSettings())
  ipcMain.handle('voice:get-snapshot', () => withService((service) => service.getSnapshot()))
  ipcMain.handle('voice:install-model', () => withService((service) => service.installModel()))
  ipcMain.handle('voice:cancel-model-install', () => withService((service) => service.cancelModelInstall()))
  ipcMain.handle('voice:remove-model', () => withService((service) => service.removeModel()))
  ipcMain.handle('voice:repair-model', () => withService((service) => service.repairModel()))
  ipcMain.handle('voice:submit-transcription', (_event, input: VoiceTranscriptionInput) => (
    withService((service) => service.submitTranscription(input))
  ))
  ipcMain.handle('voice:retry-transcription', (_event, sessionId: string) => (
    withService((service) => service.retryTranscription(validateSessionId(sessionId)))
  ))
  ipcMain.handle('voice:discard-session', (_event, sessionId: string) => (
    withService((service) => service.discardSession(validateSessionId(sessionId)))
  ))
}

export async function shutdownVoiceService(): Promise<void> {
  const promise = servicePromise
  if (!promise) return
  const service = await promise.catch(() => null)
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
    transcriber: new UtilityVoiceWorkerClient({ modulePath: resolveVoiceWorkerModule() })
  })
  service.onSnapshot((snapshot) => broadcast('voice:snapshot', snapshot))
  service.onSessionEvent((event) => broadcast('voice:session-event', event))
  const initializing = service.initialize().then(() => service)
  servicePromise = initializing
  return initializing
}

async function withService<T>(operation: (service: VoiceRuntimeService) => T | Promise<T>): Promise<T> {
  try {
    return await operation(await getService())
  } catch (error) {
    if (error instanceof VoiceRuntimeError) throw new Error(error.code)
    throw error
  }
}
function resolveVoiceWorkerModule(): string {
  return join(__dirname, 'voiceWorker.js')
}

function validateSessionId(value: unknown): string {
  if (typeof value !== 'string' || !/^[0-9a-f-]{36}$/i.test(value)) {
    throw new VoiceRuntimeError('invalid-audio')
  }
  return value
}

function broadcast(channel: string, payload: unknown): void {
  for (const window of BrowserWindow.getAllWindows()) {
    if (!window.isDestroyed()) window.webContents.send(channel, payload)
  }
}
