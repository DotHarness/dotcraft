import { useState } from 'react'
import { FileText, X } from 'lucide-react'
import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useT } from '../../contexts/LocaleContext'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ImageLightbox } from './ImageLightbox'

interface AttachmentStripProps {
  images: ImageAttachment[]
  files: ComposerFileAttachment[]
  onRemoveImage: (index: number) => void
  onRemoveFile: (index: number) => void
  removeImageLabel?: string
  removeFileLabel?: string
}

const chipStyle = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  flexShrink: 0,
  maxWidth: '220px',
  padding: '4px 8px',
  borderRadius: '8px',
  border: '1px solid var(--glass-border)',
  fontSize: '12px',
  color: 'var(--text-secondary)',
  backdropFilter: 'var(--glass-blur-soft)',
  WebkitBackdropFilter: 'var(--glass-blur-soft)'
} as const

const removeButtonStyle = {
  width: 16,
  height: 16,
  borderRadius: '50%',
  border: '1px solid var(--glass-border)',
  background: 'var(--glass-surface-soft)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  padding: 0,
  lineHeight: 1,
  flexShrink: 0,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center'
} as const

const railStyle = {
  display: 'flex',
  flexWrap: 'nowrap',
  gap: '8px',
  overflowX: 'auto',
  alignItems: 'flex-start',
  minWidth: 0
} as const

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

const imageRemoveButtonStyle = {
  width: 22,
  height: 22,
  borderRadius: '50%',
  border: '1px solid var(--glass-border)',
  background: 'var(--bg-elevated)',
  color: 'var(--text-primary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 0,
  cursor: 'pointer',
  boxShadow: 'var(--shadow-overlay)'
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
  removeImageLabel = 'Remove image',
  removeFileLabel = 'Remove file'
}: AttachmentStripProps): JSX.Element | null {
  const t = useT()
  const [previewImage, setPreviewImage] = useState<ImageAttachment | null>(null)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  if (images.length === 0 && files.length === 0) return null

  const canOpenFileAttachment = !remoteWorkspaceActive && workspacePath.length > 0 && !!activeThreadId

  return (
    <>
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: '8px',
          paddingBottom: '4px',
          alignItems: 'stretch',
          minWidth: 0
        }}
      >
        {images.length > 0 && (
          <div style={railStyle}>
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
                <ActionTooltip label={removeImageLabel} placement="top" wrapperStyle={imageRemoveButtonWrapperStyle}>
                  <button
                    type="button"
                    aria-label={removeImageLabel}
                    onClick={() => { onRemoveImage(idx) }}
                    style={imageRemoveButtonStyle}
                  >
                    <X size={14} strokeWidth={2.4} aria-hidden />
                  </button>
                </ActionTooltip>
              </div>
            ))}
          </div>
        )}

        {files.length > 0 && (
          <div style={railStyle}>
            {files.map((file, idx) => (
              <ActionTooltip key={`file-${file.path}-${idx}`} label={file.path}>
                <div
                  style={{
                    ...chipStyle,
                    background: 'var(--glass-surface-soft)'
                  }}
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
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '6px',
                      minWidth: 0,
                      padding: 0,
                      border: 'none',
                      background: 'transparent',
                      color: 'inherit',
                      cursor: canOpenFileAttachment ? 'pointer' : 'default',
                      font: 'inherit'
                    }}
                  >
                    <FileText size={14} strokeWidth={1.9} aria-hidden style={{ flexShrink: 0 }} />
                    <span
                      style={{
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        maxWidth: '150px'
                      }}
                    >
                      {file.fileName}
                    </span>
                  </button>
                  <ActionTooltip label={removeFileLabel} placement="top">
                    <button
                      type="button"
                      onClick={() => { onRemoveFile(idx) }}
                      aria-label={removeFileLabel}
                      style={removeButtonStyle}
                    >
                      <X size={11} strokeWidth={2.4} aria-hidden />
                    </button>
                  </ActionTooltip>
                </div>
              </ActionTooltip>
            ))}
          </div>
        )}
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
