import { net } from 'electron'
import { readFile } from 'fs/promises'

import { VoiceRuntimeError } from './VoiceRuntimeService'
import type { VoiceTranscriber, VoiceTranscriptionResult } from './VoiceWorkerClient'

const TRANSCRIBE_URL = 'https://chatgpt.com/backend-api/transcribe'
const ORIGINATOR = 'codex_cli_rs'
const AUTH_STATUS_TIMEOUT_MS = 30_000

export interface VoiceChatGptAuth {
  authToken: string
  accountId?: string
}

export type ReadVoiceChatGptAuth = (refresh: boolean) => Promise<VoiceChatGptAuth | null>

type FetchImpl = (url: string, init: RequestInit) => Promise<Response>

interface AppServerRequester {
  sendRequest(method: string, params: unknown, timeoutMs?: number | null): Promise<unknown>
}

export async function readAppServerChatGptAuth(
  client: AppServerRequester | null,
  capabilities: Record<string, unknown> | undefined,
  refresh: boolean
): Promise<VoiceChatGptAuth | null> {
  if (!client || capabilities?.authOpenAiOAuth !== true) return null
  const status = await client.sendRequest(
    'auth/openai/status',
    refresh ? { includeToken: true, refreshToken: true } : { includeToken: true },
    AUTH_STATUS_TIMEOUT_MS
  )
  if (typeof status !== 'object' || status === null) return null
  const { loggedIn, authToken, accountId } = status as Record<string, unknown>
  if (loggedIn !== true || typeof authToken !== 'string' || authToken.length === 0) return null
  return {
    authToken,
    accountId: typeof accountId === 'string' && accountId.length > 0 ? accountId : undefined
  }
}

export class ChatGptVoiceTranscriber implements VoiceTranscriber {
  private readonly requests = new Map<string, AbortController>()

  constructor(
    private readonly readAuth: ReadVoiceChatGptAuth,
    private readonly fetchImpl: FetchImpl = net.fetch.bind(net)
  ) {}

  async transcribe(sessionId: string, wavPath: string): Promise<VoiceTranscriptionResult> {
    const controller = new AbortController()
    this.requests.set(sessionId, controller)
    try {
      const audio = new Blob([await readFile(wavPath)], { type: 'audio/wav' })
      let response = await this.upload(audio, await this.readAuth(false), controller.signal)
      if (response.status === 401) {
        response = await this.upload(audio, await this.readAuth(true), controller.signal)
      }
      if (response.status === 401) throw new VoiceRuntimeError('auth-required')
      if (response.status === 429) throw new VoiceRuntimeError('usage-limit')
      if (!response.ok) throw new VoiceRuntimeError('transcription-failed')
      const text = await readTranscript(response)
      if (controller.signal.aborted) throw new VoiceRuntimeError('cancelled')
      if (text === null) throw new VoiceRuntimeError('transcription-failed')
      return { transcript: text.trim() }
    } finally {
      if (this.requests.get(sessionId) === controller) this.requests.delete(sessionId)
    }
  }

  async cancel(sessionId: string): Promise<void> {
    this.requests.get(sessionId)?.abort()
    this.requests.delete(sessionId)
  }

  async shutdown(): Promise<void> {
    for (const controller of this.requests.values()) controller.abort()
    this.requests.clear()
  }

  private async upload(audio: Blob, auth: VoiceChatGptAuth | null, signal: AbortSignal): Promise<Response> {
    if (!auth) throw new VoiceRuntimeError('auth-required')
    const body = new FormData()
    body.append('file', audio, 'dotcraft.wav')
    const headers: Record<string, string> = {
      Authorization: `Bearer ${auth.authToken}`,
      originator: ORIGINATOR
    }
    if (auth.accountId) headers['ChatGPT-Account-Id'] = auth.accountId
    try {
      return await this.fetchImpl(TRANSCRIBE_URL, { method: 'POST', headers, body, signal })
    } catch {
      throw new VoiceRuntimeError(signal.aborted ? 'cancelled' : 'network-error')
    }
  }
}

async function readTranscript(response: Response): Promise<string | null> {
  try {
    const body: unknown = await response.json()
    if (typeof body !== 'object' || body === null) return null
    const { text } = body as Record<string, unknown>
    return typeof text === 'string' ? text : null
  } catch {
    return null
  }
}
