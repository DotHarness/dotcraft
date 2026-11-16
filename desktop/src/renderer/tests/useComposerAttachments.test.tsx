import { useState } from 'react'
import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MAX_ATTACHMENT_BYTES } from '../../shared/attachments'
import { useComposerAttachments } from '../components/conversation/useComposerAttachments'
import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'
import { useConnectionStore } from '../stores/connectionStore'
import { useToastStore } from '../stores/toastStore'

const saveImageToTemp = vi.fn()
const dataUrl = 'data:image/png;base64,AQIDBAU='

function acquisition(remoteWorkspace: boolean) {
  return renderHook(() => {
    const [images, setImages] = useState<ImageAttachment[]>([])
    const [, setFiles] = useState<ComposerFileAttachment[]>([])
    const attachments = useComposerAttachments({ remoteWorkspace, images, setImages, setFiles, t: (key) => key })
    return { ...attachments, images }
  })
}

beforeEach(() => {
  saveImageToTemp.mockReset().mockResolvedValue({ path: 'C:/staged/image.png' })
  useConnectionStore.getState().reset()
  useToastStore.setState({ toasts: [] })
  installDesktopApiMock({ workspace: { saveImageToTemp } })
})

describe('composer image acquisition', () => {
  it.each([true, false])('stages original image bytes with the captured connection and client/host ownership (remote=%s)', async (remote) => {
    const source = { connectionKey: 'project-A', connectionId: 'A-1' }
    useConnectionStore.setState(source)
    const { result } = acquisition(remote)
    await act(async () => result.current.saveDataUrlAsTemp(dataUrl, 'original.png', 'image/png'))
    expect(saveImageToTemp).toHaveBeenCalledWith({ dataUrl, fileName: 'original.png', source })
    expect(result.current.images).toEqual([{
      tempPath: 'C:/staged/image.png', dataUrl, fileName: 'original.png', mimeType: 'image/png',
      source: remote ? 'client' : 'host'
    }])
  })

  it.each([0, 1])('accepts exactly 10 MiB and rejects an image one byte over the cap (extra=%s)', async (extra) => {
    const size = MAX_ATTACHMENT_BYTES + extra
    const padding = size % 3 === 1 ? 'AA==' : 'AAA='
    const imageDataUrl = 'data:image/png;base64,' + 'AAAA'.repeat(Math.floor(size / 3)) + padding
    const { result } = acquisition(true)
    await act(async () => result.current.saveDataUrlAsTemp(imageDataUrl, 'original.png', 'image/png'))
    if (extra) {
      expect(saveImageToTemp).not.toHaveBeenCalled()
      expect(result.current.images).toEqual([])
      expect(useToastStore.getState().toasts).toMatchObject([{ message: 'input.imageTooLarge' }])
    } else {
      expect(saveImageToTemp).toHaveBeenCalledExactlyOnceWith({ dataUrl: imageDataUrl, fileName: 'original.png' })
      expect(result.current.images[0].dataUrl).toBe(imageDataUrl)
    }
  })
})
