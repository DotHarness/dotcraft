import { useState, type ReactNode } from 'react'
import { FileText, X } from 'lucide-react'
import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useT } from '../../contexts/LocaleContext'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { AttachmentRemoveButton } from './AttachmentRemoveButton'
import { ImageLightbox } from './ImageLightbox'

interface AttachmentStripProps {
  images: ImageAttachment[]
  files: ComposerFileAttachment[]
  onRemoveImage: (index: number) => void
  onRemoveFile: (index: number) => void
  contextAttachments?: ReactNode
  hasContextAttachments?: boolean
  removeImageLabel?: string
  removeFileLabel?: string
}

const imageThumbnailFrameStyle = {
  position: 'relative',
  width: 96,
  height: 96,
  border: '1px solid var(--glass-border)',
  borderRadius: '14px',
  background: 'var(--bg-secondary)',
  overflow: 'hidden',
  flexShrink: 0,
  boxShadow: 'var(--composer-input-shadow)'
} as const

const imageThumbnailButtonStyle = {
  display: 'block',
  width: '100%',
  height: '100%',
  padding: 0,
  border: 'none',
  background: 'transparent',
  lineHeight: 0,
  cursor: 'zoom-in'
} as const

const imageThumbnailButtonWrapperStyle = {
  display: 'block',
  width: '100%',
  height: '100%'
} as const

const imageThumbnailStyle = {
  display: 'block',
  width: '100%',
  height: '100%',
  objectFit: 'cover'
} as const

const imageRemoveButtonWrapperStyle = {
  position: 'absolute',
  top: 5,
  right: 5,
  zIndex: 1
} as const

export function AttachmentStrip({
  images,
  files,
  onRemoveImage,
  onRemoveFile,
  contextAttachments,
  hasContextAttachments = false,
  removeImageLabel = 'Remove image',
  removeFileLabel = 'Remove file'
}: AttachmentStripProps): JSX.Element | null {
  const t = useT()
  const [previewImage, setPreviewImage] = useState<ImageAttachment | null>(null)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  if (images.length === 0 && files.length === 0 && !hasContextAttachments) return null

  const canOpenFileAttachment = !remoteWorkspaceActive && workspacePath.length > 0 && !!activeThreadId

  return (
    <>
      <div className="dc-attachment-strip">
        {images.length > 0 && (
          <>
            {images.map((img, idx) => (
              <div key={`image-${img.tempPath}-${idx}`} style={imageThumbnailFrameStyle}>
                <ActionTooltip label={img.fileName} placement="top" wrapperStyle={imageThumbnailButtonWrapperStyle}>
                  <button
                    type="button"
                    onClick={() => setPreviewImage(img)}
                    aria-label={t('conversation.previewImageAttachmentAria', { file: img.fileName })}
                    style={imageThumbnailButtonStyle}
                  >
                    <img src={img.dataUrl} alt="" style={imageThumbnailStyle} />
                  </button>
                </ActionTooltip>
                <IconButton
                  icon={<X size={14} strokeWidth={2.4} aria-hidden />}
                  label={removeImageLabel}
                  tooltipLabel={removeImageLabel}
                  tooltipPlacement="top"
                  tooltipWrapperStyle={imageRemoveButtonWrapperStyle}
                  size={22}
                  radius={11}
                  className="dc-attachment-image-remove"
                  onClick={() => { onRemoveImage(idx) }}
                  style={{ boxShadow: 'var(--shadow-level-1)' }}
                />
              </div>
            ))}
          </>
        )}

        {files.length > 0 && (
          <>
            {files.map((file, idx) => (
              <ActionTooltip key={`file-${file.path}-${idx}`} label={file.path}>
                <div
                  className="dc-context-attachment dc-context-attachment--file"
                >
                  <button
                    type="button"
                    onClick={() => {
                      if (!activeThreadId || !workspacePath) return
                      void openConversationLink({
                        target: file.path,
                        workspacePath,
                        threadId: activeThreadId,
                        t
                      })
                    }}
                    disabled={!canOpenFileAttachment}
                    aria-label={t('conversation.openFileRefAria', { file: file.fileName })}
                    className="dc-attachment-file-link"
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '6px',
                      minWidth: 0,
                      padding: '2px 4px',
                      margin: '-2px -4px',
                      border: 'none',
                      borderRadius: '4px',
                      cursor: canOpenFileAttachment ? 'pointer' : 'default',
                      font: 'inherit'
                    }}
                  >
                    <span className="dc-context-attachment__icon"><FileText size={20} strokeWidth={1.9} aria-hidden /></span>
                    <span className="dc-context-attachment__copy">
                      <span className="dc-context-attachment__title">{file.fileName}</span>
                      <span className="dc-context-attachment__subtitle">{file.fileName.includes('.') ? file.fileName.split('.').pop()?.toUpperCase() : t('menu.file')}</span>
                    </span>
                  </button>
                  <AttachmentRemoveButton label={removeFileLabel} onRemove={() => { onRemoveFile(idx) }} />
                </div>
              </ActionTooltip>
            ))}
          </>
        )}
        {contextAttachments}
      </div>
      {previewImage && (
        <ImageLightbox
          src={previewImage.dataUrl}
          alt={previewImage.fileName}
          onClose={() => setPreviewImage(null)}
        />
      )}
    </>
  )
}
