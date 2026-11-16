import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({ pick: vi.fn(), read: vi.fn() }))
vi.mock('expo-document-picker', () => ({ getDocumentAsync: mocks.pick }))
vi.mock('expo-image-manipulator', () => ({ ImageManipulator: {}, SaveFormat: { JPEG: 'jpeg' } }))
vi.mock('expo-image-picker', () => ({}))
vi.mock('./readBase64', () => ({ readBase64: mocks.read }))

import { pickFile } from './attachmentPicker'

const limit = 10 * 1024 * 1024
beforeEach(() => { vi.resetAllMocks() })

describe('file attachment size', () => {
  it('accepts an entire 10 MiB document without changing its bytes', async () => {
    const dataBase64 = Buffer.alloc(limit, 7).toString('base64')
    mocks.pick.mockResolvedValue({ canceled: false, assets: [{ uri: 'file:///picked/document.bin', name: 'document.bin', size: limit }] })
    mocks.read.mockResolvedValue(dataBase64)
    expect(await pickFile()).toMatchObject({ kind: 'file', file: { name: 'document.bin', dataBase64 } })
    expect(mocks.read).toHaveBeenCalledWith('file:///picked/document.bin')
  })

  it('rejects a known oversized document before reading it', async () => {
    mocks.pick.mockResolvedValue({ canceled: false, assets: [{ uri: 'file:///picked/large.bin', name: 'large.bin', size: limit + 1 }] })
    expect(await pickFile()).toEqual({ kind: 'tooLarge', name: 'large.bin' })
    expect(mocks.read).not.toHaveBeenCalled()
  })

  it('checks the actual contents when the picker does not report a size', async () => {
    mocks.pick.mockResolvedValue({ canceled: false, assets: [{ uri: 'file:///picked/large.bin', name: 'large.bin' }] })
    mocks.read.mockResolvedValue(Buffer.alloc(limit + 1).toString('base64'))
    expect(await pickFile()).toEqual({ kind: 'tooLarge', name: 'large.bin' })
  })
})
