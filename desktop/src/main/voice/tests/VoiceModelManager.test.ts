import { createHash } from 'crypto'
import { mkdtemp, readFile, rm, writeFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { VoiceModelManager } from '../VoiceModelManager'
import type { ManagedVoiceModelDescriptor } from '../voiceModelDescriptor'

const roots: string[] = []

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => rm(root, { recursive: true, force: true })))
})

describe('VoiceModelManager', () => {
  it('downloads, verifies, and atomically installs the managed model', async () => {
    const bytes = Buffer.from('verified model')
    const root = await createRoot()
    const fetchImpl = vi.fn(async () => response(bytes))
    const manager = createManager(root, bytes, fetchImpl)

    await manager.install()

    expect(manager.getState()).toMatchObject({ phase: 'installed', bytesDownloaded: bytes.length })
    expect(await readFile(manager.modelPath)).toEqual(bytes)
  })

  it('resumes a partial download with a byte range', async () => {
    const bytes = Buffer.from('resume this model')
    const root = await createRoot()
    const fetchImpl = vi.fn(async (_url: string | URL | Request, init?: RequestInit) => {
      expect(new Headers(init?.headers).get('Range')).toBe('bytes=6-')
      return response(bytes.subarray(6), 206)
    })
    const manager = createManager(root, bytes, fetchImpl)
    await manager.initialize()
    await writeFile(manager.partialPath, bytes.subarray(0, 6))

    await manager.install()

    expect(await readFile(manager.modelPath)).toEqual(bytes)
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })

  it('promotes a completed partial without another network request', async () => {
    const bytes = Buffer.from('already downloaded')
    const root = await createRoot()
    const fetchImpl = vi.fn()
    const manager = createManager(root, bytes, fetchImpl)
    await manager.initialize()
    await writeFile(manager.partialPath, bytes)

    await manager.install()

    expect(await readFile(manager.modelPath)).toEqual(bytes)
    expect(fetchImpl).not.toHaveBeenCalled()
  })

  it('marks a hash mismatch as damaged', async () => {
    const expected = Buffer.from('expected model')
    const root = await createRoot()
    const manager = createManager(root, expected, async () => response(Buffer.from('corrupt')))

    await manager.install()

    expect(manager.getState()).toMatchObject({ phase: 'damaged', errorCode: 'model-damaged' })
  })
})

async function createRoot(): Promise<string> {
  const root = await mkdtemp(join(tmpdir(), 'dotcraft-voice-model-'))
  roots.push(root)
  return root
}

function createManager(
  root: string,
  expected: Uint8Array,
  fetchImpl: typeof fetch
): VoiceModelManager {
  const descriptor: ManagedVoiceModelDescriptor = {
    id: 'test-model',
    fileName: 'model.bin',
    revision: 'test-revision',
    sha256: createHash('sha256').update(expected).digest('hex'),
    displayBytes: expected.byteLength,
    downloadUrl: 'https://example.invalid/model.bin'
  }
  return new VoiceModelManager({ voiceRoot: root, descriptor, fetchImpl })
}

function response(bytes: Uint8Array, status = 200): Response {
  return new Response(bytes, {
    status,
    headers: { 'content-length': String(bytes.byteLength) }
  })
}
