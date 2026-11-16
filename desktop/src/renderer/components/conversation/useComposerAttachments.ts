import { useCallback, type Dispatch, type SetStateAction } from 'react'
import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'
import { addToast } from '../../stores/toastStore'
import { classifyDroppedComposerFiles, extForFile, isImageFile, mergeComposerFileAttachments } from '../../utils/composerAttachments'
import { MAX_ATTACHMENT_BYTES } from '../../../shared/attachments'
import { captureComposerAttachmentTarget } from '../../utils/prepareComposerAttachments'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export function useComposerAttachments({
  remoteWorkspace, images, setImages, setFiles, t
}: {
  remoteWorkspace: boolean
  images: ImageAttachment[]
  setImages: Dispatch<SetStateAction<ImageAttachment[]>>
  setFiles: Dispatch<SetStateAction<ComposerFileAttachment[]>>
  t: Translate
}) {
  const saveDataUrlAsTemp = useCallback(async (dataUrl: string, fileName: string, mimeType: string): Promise<void> => {
    const target = captureComposerAttachmentTarget()
    try {
      const base64 = dataUrl.split(',')[1] ?? ''
      const size = Math.floor(base64.length * 3 / 4) - (base64.endsWith('==') ? 2 : base64.endsWith('=') ? 1 : 0)
      if (size > MAX_ATTACHMENT_BYTES) {
        addToast(t('input.imageTooLarge', { mb: MAX_ATTACHMENT_BYTES / 1024 / 1024 }), 'warning')
        return
      }
      if (images.length >= 5) {
        addToast(t('input.maxImages', { max: 5 }), 'warning')
        return
      }
      const { path } = await window.api.workspace.saveImageToTemp({ dataUrl, fileName, ...(target.source ? { source: target.source } : {}) })
      setImages((previous) => [...previous, { tempPath: path, dataUrl, fileName, mimeType, source: remoteWorkspace ? 'client' : 'host' }])
    } catch (error) {
      addToast(t('input.saveImageFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
    }
  }, [images.length, remoteWorkspace, setImages, t])

  const onPasteImage = useCallback((file: File): void => {
    if (!isImageFile(file)) {
      addToast(t('input.unsupportedImage', { ext: extForFile(file.name) || 'unknown' }), 'warning')
      return
    }
    const reader = new FileReader()
    reader.onload = () => { void saveDataUrlAsTemp(reader.result as string, file.name, file.type || 'image/png') }
    reader.readAsDataURL(file)
  }, [saveDataUrlAsTemp, t])

  const attachImages = useCallback((picked: File[]): void => {
    for (const file of picked) onPasteImage(file)
  }, [onPasteImage])

  const attachDroppedFiles = useCallback((dataTransfer: DataTransfer): void => {
    const { imageFiles, fileAttachments, skippedCount } = classifyDroppedComposerFiles(dataTransfer, window.api.workspace.getPathForFile)
    attachImages(imageFiles)
    if (fileAttachments.length) setFiles((previous) => mergeComposerFileAttachments(previous, fileAttachments))
    if (skippedCount) addToast(t('input.dropItemsSkipped', { count: skippedCount }), 'warning')
  }, [attachImages, setFiles, t])

  return { saveDataUrlAsTemp, onPasteImage, attachImages, attachDroppedFiles }
}
