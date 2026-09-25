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

  it('fails with auth-required after a second 401', async () => {
    const fetchImpl = fakeFetch(() => jsonResponse(401, {}), () => jsonResponse(401, {}))
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fetchImpl)

    await expect(transcriber.transcribe('session-a', await wavFile())).rejects.toMatchObject({ code: 'auth-required' })
    expect(fetchImpl).toHaveBeenCalledTimes(2)
  })

  it('fails with auth-required when no token is available', async () => {
    const fetchImpl = fakeFetch()
    const transcriber = new ChatGptVoiceTranscriber(async () => null, fetchImpl)

    await expect(transcriber.transcribe('session-a', await wavFile())).rejects.toMatchObject({ code: 'auth-required' })
    expect(fetchImpl).not.toHaveBeenCalled()
  })

  it('fails with usage-limit when ChatGPT rejects with 429', async () => {
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fakeFetch(() => jsonResponse(429, {})))

    await expect(transcriber.transcribe('session-a', await wavFile())).rejects.toMatchObject({ code: 'usage-limit' })
  })

  it('fails with network-error when the connection fails', async () => {
    const transcriber = new ChatGptVoiceTranscriber(
      async () => auth('token-a'),
      fakeFetch(() => Promise.reject(new TypeError('fetch failed')))
    )

    await expect(transcriber.transcribe('session-a', await wavFile())).rejects.toMatchObject({ code: 'network-error' })
  })

  it('fails with transcription-failed for other statuses and unreadable bodies', async () => {
    const wavPath = await wavFile()
    const responses = [
      jsonResponse(500, { text: 'ignored' }),
      new Response('not json', { status: 200 }),
      jsonResponse(200, { transcript: 'wrong field' })
    ]
    for (const response of responses) {
      const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fakeFetch(() => response))
      await expect(transcriber.transcribe('session-a', wavPath)).rejects.toMatchObject({ code: 'transcription-failed' })
    }
  })

  it('aborts the in-flight request when the session is cancelled', async () => {
    const fetchImpl = fakeFetch((init) => new Promise<Response>((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
    }))
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fetchImpl)
    const transcription = transcriber.transcribe('session-a', await wavFile())
    await vi.waitFor(() => expect(fetchImpl).toHaveBeenCalled())

    await transcriber.cancel('session-a')

    await expect(transcription).rejects.toMatchObject({ code: 'cancelled' })
    expect(fetchImpl.mock.calls[0][1].signal?.aborted).toBe(true)
  })

  it('never returns a transcript that arrives after shutdown', async () => {
    let respond!: (response: Response) => void
    const fetchImpl = fakeFetch(() => new Promise<Response>((resolve) => { respond = resolve }))
    const transcriber = new ChatGptVoiceTranscriber(async () => auth('token-a'), fetchImpl)
    const transcription = transcriber.transcribe('session-a', await wavFile())
    await vi.waitFor(() => expect(fetchImpl).toHaveBeenCalled())

    await transcriber.shutdown()
    respond(jsonResponse(200, { text: 'late' }))

    await expect(transcription).rejects.toMatchObject({ code: 'cancelled' })
  })
})

describe('readAppServerChatGptAuth', () => {
  it('reads nothing from an AppServer without ChatGPT sign-in support', async () => {
    const client = { sendRequest: vi.fn() }

    await expect(readAppServerChatGptAuth(client, {}, false)).resolves.toBeNull()
    await expect(readAppServerChatGptAuth(null, { authOpenAiOAuth: true }, false)).resolves.toBeNull()
    expect(client.sendRequest).not.toHaveBeenCalled()
  })

  it('returns the token and account and forwards a refresh request', async () => {
    const client = {
      sendRequest: vi.fn(async () => ({ loggedIn: true, accountId: 'account-a', authToken: 'token-a' }))
    }
    const capabilities = { authOpenAiOAuth: true }

    await expect(readAppServerChatGptAuth(client, capabilities, false)).resolves.toEqual(auth('token-a', 'account-a'))
    await readAppServerChatGptAuth(client, capabilities, true)

    expect(client.sendRequest.mock.calls.map(([method, params]) => [method, params])).toEqual([
      ['auth/openai/status', { includeToken: true }],
      ['auth/openai/status', { includeToken: true, refreshToken: true }]
    ])
  })

  it('reports no sign-in without a token or a login', async () => {
    const capabilities = { authOpenAiOAuth: true }
    const statuses = [
      { loggedIn: true, accountId: 'account-a' },
      { loggedIn: false, authToken: 'token-a' },
      null
    ]
    for (const status of statuses) {
      const client = { sendRequest: vi.fn(async () => status) }
      await expect(readAppServerChatGptAuth(client, capabilities, false)).resolves.toBeNull()
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
