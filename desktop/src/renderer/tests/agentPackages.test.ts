import { beforeEach, describe, expect, it, vi } from 'vitest'
import { readAgentExport, uploadAgentPackage } from '../components/agents/agentPackages'

const sendRequest = vi.fn()
const MiB = 1024 * 1024

beforeEach(() => {
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {} })
  Object.defineProperty(window, 'api', { configurable: true, value: { appServer: { sendRequest } } })
  sendRequest.mockReset()
})

describe('agent packages', () => {
  it('uploads in 1 MiB chunks under one import id and returns the final preview', async () => {
    const bytes = new Uint8Array(MiB * 2 + 10)
    bytes[0] = 0x50
    bytes[MiB] = 0x51
    const preview = { importId: 'imp-1', name: 'Writer' }
    sendRequest
      .mockResolvedValueOnce({ importId: 'imp-1', receivedBytes: MiB })
      .mockResolvedValueOnce({ importId: 'imp-1', receivedBytes: MiB * 2 })
      .mockResolvedValueOnce({ importId: 'imp-1', receivedBytes: bytes.length, preview })

    const result = await uploadAgentPackage(new File([bytes], 'writer.agent.zip'))

    expect(result).toBe(preview)
    const calls = sendRequest.mock.calls.map(([, params]) => params)
    expect(calls.map((params) => [params.importId, params.offset, params.totalBytes])).toEqual([
      [undefined, 0, bytes.length],
      ['imp-1', MiB, bytes.length],
      ['imp-1', MiB * 2, bytes.length]
    ])
    expect(atob(calls[1].dataBase64).charCodeAt(0)).toBe(0x51)
    expect(atob(calls[2].dataBase64).length).toBe(10)
  })

  it('joins export chunks and discards the export when a chunk fails', async () => {
    sendRequest
      .mockResolvedValueOnce({ totalBytes: 5, dataBase64: btoa('PK') })
      .mockResolvedValueOnce({ totalBytes: 5, dataBase64: btoa('\u0005\u0006!') })
    const bytes = await readAgentExport('Writer', 'user', [{ kind: 'skill', name: 'notes' }])
    expect(Array.from(bytes)).toEqual([0x50, 0x4b, 0x05, 0x06, 0x21])
    expect(sendRequest.mock.calls.map(([, params]) => params.offset)).toEqual([0, 2])

    sendRequest.mockReset()
    sendRequest.mockRejectedValueOnce(new Error('agentPackageTooLarge')).mockResolvedValueOnce({})
    await expect(readAgentExport('Writer', 'user', [])).rejects.toThrow('agentPackageTooLarge')
    expect(sendRequest.mock.calls.at(-1)?.[1]).toMatchObject({ offset: -1 })
  })
})
