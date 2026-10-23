import { mkdtemp, rm, writeFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  ChatGptVoiceTranscriber,
  readAppServerChatGptAuth,
  type VoiceChatGptAuth
} from '../VoiceChatGptTranscriber'

const WAV_BYTES = new Uint8Array([82, 73, 70, 70, 1, 2, 3, 4])
const tempRoots: string[] = []

afterEach(async () => {
  await Promise.all(tempRoots.splice(0).map((path) => rm(path, { recursive: true, force: true })))
})

describe('ChatGptVoiceTranscriber', () => {
  it('uploads the session WAV with the ChatGPT credentials and trims the transcript', async () => {
    const readAuth = vi.fn(async () => auth('token-a', 'account-a'))
    const fetchImpl = fakeFetch(() => jsonResponse(200, { text: '  hello there  ', language: 'en' }))
    const transcriber = new ChatGptVoiceTranscriber(readAuth, fetchImpl)

    await expect(transcriber.transcribe('session-a', await wavFile())).resolves.toEqual({ transcript: 'hello there' })

    expect(readAuth).toHaveBeenCalledExactlyOnceWith(false)
    const [url, init] = fetchImpl.mock.calls[0]
    expect(url).toBe('https://chatgpt.com/backend-api/transcribe')
    expect(init.method).toBe('POST')
    expect(init.headers).toEqual({
      Authorization: 'Bearer token-a',
      'ChatGPT-Account-Id': 'account-a',
      originator: 'codex_cli_rs'
    })
    const body = init.body as FormData
    expect([...body.keys()]).toEqual(['file'])
    const file = body.get('file') as File
    expect(file.name).toBe('dotcraft.wav')
    expect(file.type).toBe('audio/wav')
    expect(new Uint8Array(await file.arrayBuffer())).toEqual(WAV_BYTES)
  })

  it('refreshes the token and retries once after a 401', async () => {
    const readAuth = vi.fn(async (refresh: boolean) => auth(refresh ? 'fresh-token' : 'stale-token'))
    const fetchImpl = fakeFetch(
      () => jsonResponse(401, {}),
      () => jsonResponse(200, { text: 'retried' })
    )
    const transcriber = new ChatGptVoiceTranscriber(readAuth, fetchImpl)

    await expect(transcriber.transcribe('session-a', await wavFile())).resolves.toEqual({ transcript: 'retried' })

    expect(readAuth.mock.calls).toEqual([[false], [true]])
    expect(fetchImpl.mock.calls.map(([, init]) => (init.headers as Record<string, string>).Authorization))
      .toEqual(['Bearer stale-token', 'Bearer fresh-token'])
  })

  it('fails with auth-required after a second 401 or without a token', async () => {
    const fetchImpl = fakeFetch(() => jsonResponse(401, {}), () => jsonResponse(401, {}))
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fetchImpl)

    await expect(transcriber.transcribe('session-a', await wavFile())).rejects.toMatchObject({ code: 'auth-required' })
    expect(fetchImpl).toHaveBeenCalledTimes(2)

    const unused = fakeFetch()
    await expect(new ChatGptVoiceTranscriber(async () => null, unused).transcribe('session-a', await wavFile()))
      .rejects.toMatchObject({ code: 'auth-required' })
    expect(unused).not.toHaveBeenCalled()
  })

  it('maps rate limits, connection failures, other statuses, and unreadable bodies to error codes', async () => {
    const wavPath = await wavFile()
    const cases: Array<[() => Response | Promise<Response>, string]> = [
      [() => jsonResponse(429, {}), 'usage-limit'],
      [() => Promise.reject(new TypeError('fetch failed')), 'network-error'],
      [() => jsonResponse(500, { text: 'ignored' }), 'transcription-failed'],
      [() => new Response('not json', { status: 200 }), 'transcription-failed'],
      [() => jsonResponse(200, { transcript: 'wrong field' }), 'transcription-failed']
    ]
    for (const [respond, code] of cases) {
      const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fakeFetch(respond))
      await expect(transcriber.transcribe('session-a', wavPath)).rejects.toMatchObject({ code })
    }
  })

  it('aborts the in-flight request when the session is cancelled', async () => {
    const fetchImpl = fakeFetch((init) => new Promise<Response>((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
    }))
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fetchImpl)
    const transcription = transcriber.transcribe('session-a', await wavFile())
    await vi.waitFor(() => expect(fetchImpl).toHaveBeenCalled(), { interval: 1 })

    await transcriber.cancel('session-a')

    await expect(transcription).rejects.toMatchObject({ code: 'cancelled' })
    expect(fetchImpl.mock.calls[0][1].signal?.aborted).toBe(true)
  })

  it('never returns a transcript that arrives after shutdown', async () => {
    let respond!: (response: Response) => void
    const fetchImpl = fakeFetch(() => new Promise<Response>((resolve) => { respond = resolve }))
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fetchImpl)
    const transcription = transcriber.transcribe('session-a', await wavFile())
    await vi.waitFor(() => expect(fetchImpl).toHaveBeenCalled(), { interval: 1 })

    await transcriber.shutdown()
    respond(jsonResponse(200, { text: 'late' }))

    await expect(transcription).rejects.toMatchObject({ code: 'cancelled' })
  })
})

describe('readAppServerChatGptAuth', () => {
  const capabilities = { providerManagement: true }
  const providers = {
    providers: [
      { id: 'anthropic', authMethod: 'apiKey', hasApiKey: true },
      { id: 'chatgpt', authMethod: 'chatgptOAuth', isAuthenticated: true }
    ]
  }

  function appServer(status: unknown, list: unknown = providers) {
    return {
      sendRequest: vi.fn(async (method: string) => method === 'provider/list' ? list : status)
    }
  }

  it('reads nothing without provider management or a ChatGPT provider', async () => {
    const client = appServer({ isAuthenticated: true, authToken: 'token-a' })

    await expect(readAppServerChatGptAuth(client, {}, false)).resolves.toBeNull()
    await expect(readAppServerChatGptAuth(null, capabilities, false)).resolves.toBeNull()
    expect(client.sendRequest).not.toHaveBeenCalled()

    const noChatGpt = appServer({ isAuthenticated: true, authToken: 'token-a' }, { providers: [{ id: 'anthropic', authMethod: 'apiKey' }] })
    await expect(readAppServerChatGptAuth(noChatGpt, capabilities, false)).resolves.toBeNull()
    expect(noChatGpt.sendRequest.mock.calls.map(([method]) => method)).toEqual(['provider/list'])
  })

  it('returns the token and account of the ChatGPT provider and forwards a refresh request', async () => {
    const client = appServer({ isAuthenticated: true, accountId: 'account-a', authToken: 'token-a' })

    await expect(readAppServerChatGptAuth(client, capabilities, false)).resolves.toEqual(auth('token-a', 'account-a'))
    await readAppServerChatGptAuth(client, capabilities, true)

    expect(client.sendRequest.mock.calls
      .filter(([method]) => method === 'provider/auth/status')
      .map(([, params]) => params)).toEqual([
      { providerId: 'chatgpt', includeToken: true },
      { providerId: 'chatgpt', includeToken: true, refreshToken: true }
    ])
  })

  it('reports no sign-in without a token or an authenticated status', async () => {
    const statuses = [
      { isAuthenticated: true, accountId: 'account-a' },
      { isAuthenticated: false, authToken: 'token-a' },
      null
    ]
    for (const status of statuses) {
      await expect(readAppServerChatGptAuth(appServer(status), capabilities, false)).resolves.toBeNull()
    }
  })
})

function auth(authToken: string, accountId?: string): VoiceChatGptAuth {
  return { authToken, accountId }
}

async function wavFile(): Promise<string> {
  const root = await mkdtemp(join(tmpdir(), 'dotcraft-voice-chatgpt-'))
  tempRoots.push(root)
  const path = join(root, 'session.wav')
  await writeFile(path, WAV_BYTES)
  return path
}

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function fakeFetch(...responses: Array<(init: RequestInit) => Response | Promise<Response>>) {
  let index = 0
  return vi.fn(async (_url: string, init: RequestInit): Promise<Response> => {
    const respond = responses[index++]
    if (!respond) throw new Error('unexpected request')
    return await respond(init)
  })
}
