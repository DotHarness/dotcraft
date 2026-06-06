import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useT } from '../../contexts/LocaleContext'
import { openConversationLink, openImagePathInViewer } from '../../utils/conversationDeepLink'
import { ActionTooltip } from '../ui/ActionTooltip'

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
  fontSize: '10px',
  cursor: 'pointer',
  padding: 0,
  lineHeight: 1,
  flexShrink: 0
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
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  if (images.length === 0 && files.length === 0) return null

  const canOpenAttachment = !remoteWorkspaceActive && workspacePath.length > 0 && !!activeThreadId

  return (
    <div
      style={{
        display: 'flex',
        flexWrap: 'nowrap',
        gap: '8px',
        overflowX: 'auto',
        paddingBottom: '4px',
        alignItems: 'flex-start'
      }}
    >
      {images.map((img, idx) => (
        <div
          key={`image-${img.tempPath}-${idx}`}
          style={{
            ...chipStyle,
            background: 'var(--glass-surface-soft)'
          }}
        >
          <ActionTooltip label={img.fileName} placement="top">
          <button
            type="button"
            onClick={() => {
              if (!activeThreadId || !workspacePath) return
              void openImagePathInViewer({
                absolutePath: img.tempPath,
                workspacePath,
                threadId: activeThreadId,
                t
              })
            }}
            disabled={!canOpenAttachment}
            aria-label={t('conversation.openImageAttachmentAria', { file: img.fileName })}
            style={{
              padding: 0,
              border: 'none',
              background: 'transparent',
              lineHeight: 0,
              borderRadius: '3px',
              cursor: canOpenAttachment ? 'pointer' : 'default',
              opacity: canOpenAttachment ? 1 : 0.9
            }}
          >
            <img
              src={img.dataUrl}
              alt=""
              style={{
                width: 20,
                height: 20,
                borderRadius: '3px',
                objectFit: 'cover',
                flexShrink: 0
              }}
            />
          </button>
          </ActionTooltip>
          <ActionTooltip label={img.fileName} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              maxWidth: '140px',
              display: 'block'
            }}
          >
            {img.fileName}
          </span>
          </ActionTooltip>
          <ActionTooltip label={removeImageLabel} placement="top">
          <button
            type="button"
            onClick={() => { onRemoveImage(idx) }}
            aria-label={removeImageLabel}
            style={removeButtonStyle}
          >
            ✕
          </button>
          </ActionTooltip>
        </div>
      ))}

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
            disabled={!canOpenAttachment}
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
              cursor: canOpenAttachment ? 'pointer' : 'default',
              font: 'inherit'
            }}
          >
            <span aria-hidden style={{ flexShrink: 0 }}>
              📄
            </span>
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
            ✕
          </button>
          </ActionTooltip>
        </div>
        </ActionTooltip>
      ))}
    </div>
  )
}
