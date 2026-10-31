// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TextFileChangedPayload } from '../../shared/viewer/types'
import { useViewerFileVersion } from '../components/detail/viewers/useViewerFileVersion'
import { previewUnavailableReason } from '../components/detail/viewers/filePreview'
import { installDesktopApiMock } from './desktopApiMock'

const PATH = '/workspace/model.xlsx'
let emit: (payload: TextFileChangedPayload) => void = () => {}
const unwatch = vi.fn()

beforeEach(() => {
  unwatch.mockClear()
  installDesktopApiMock({
    workspace: {
      viewer: {
        classify: vi.fn(async () => ({ contentClass: 'spreadsheet' as const, mime: '', sizeBytes: 10, mtimeMs: 100 })),
        watchText: vi.fn(async (_params, callback) => {
          emit = callback
          return unwatch
        })
      }
    }
  })
})

const change = (mtimeMs: number, sizeBytes = 10) => act(() => emit({ absolutePath: PATH, mtimeMs, sizeBytes }))

describe('useViewerFileVersion', () => {
  it('flags a change on disk only when the file differs from the loaded version', async () => {
    const { result, unmount } = renderHook(() => useViewerFileVersion(PATH))
    await waitFor(() => expect(window.api.workspace.viewer.classify).toHaveBeenCalled())
    await act(async () => {})

    change(100)
    expect(result.current.stale).toBe(false)
    change(200, 12)
    expect(result.current).toMatchObject({ stale: true, version: 0 })

    act(() => result.current.refresh())
    expect(result.current).toMatchObject({ stale: false, version: 1 })
    change(200, 12)
    expect(result.current.stale).toBe(false)
    change(300, 12)
    expect(result.current.stale).toBe(true)

    unmount()
    expect(unwatch).toHaveBeenCalledTimes(1)
  })
})

describe('previewUnavailableReason', () => {
  it('blocks unsupported types and files over their class limit', () => {
    expect(previewUnavailableReason('unsupported', 1)).toEqual({ kind: 'type' })
    expect(previewUnavailableReason('video', 256 * 1024 * 1024)).toBeNull()
    expect(previewUnavailableReason('video', 256 * 1024 * 1024 + 1)).toMatchObject({ kind: 'size', limitBytes: 256 * 1024 * 1024 })
    expect(previewUnavailableReason('spreadsheet', 41 * 1024 * 1024)).toMatchObject({ kind: 'size', limitBytes: 40 * 1024 * 1024 })
    expect(previewUnavailableReason('presentation', undefined)).toBeNull()
    expect(previewUnavailableReason('text', 500 * 1024 * 1024)).toBeNull()
  })
})
