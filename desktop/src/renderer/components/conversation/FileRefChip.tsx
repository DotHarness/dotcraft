import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { resolveLocalReference, type LocalReference } from '../../utils/referencePaths'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { isFolderReference, referenceBaseName } from './fileRefIconDom'
import { ReferencePathContextMenu } from './ReferencePathContextMenu'

export function FileRefChip({
  displayPath,
  targetPath,
  label,
  className,
  workspacePath,
  activeThreadId,
  remoteWorkspaceActive,
  onOpen
}: {
  displayPath: string
  targetPath: string
  label?: string
  className?: string
  workspacePath: string
  activeThreadId: string | null
  remoteWorkspaceActive: boolean
  onOpen?: (threadId: string) => void
}): JSX.Element {
  const t = useT()
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; reference: LocalReference } | null>(null)
  const fileName = referenceBaseName(displayPath)
  const reference = resolveLocalReference(targetPath, workspacePath)
  const title = reference?.path ?? targetPath
  const folder = isFolderReference(displayPath)
  const canOpen = folder
    ? !remoteWorkspaceActive && !!reference
    : !remoteWorkspaceActive && workspacePath.length > 0 && !!activeThreadId

  return (
    <>
      <ActionTooltip label={title}>
      <button
        type="button"
        aria-label={t('conversation.openFileRefAria', { file: fileName })}
        disabled={!canOpen}
        className={className ? `dc-ref dc-ref-file ${className}` : 'dc-ref dc-ref-file'}
        onContextMenu={(event) => {
          if (!reference) return
          event.preventDefault()
          event.stopPropagation()
          setContextMenu({
            position: { x: event.clientX, y: event.clientY },
            reference
          })
        }}
        onClick={() => {
          if (folder) {
            if (canOpen && reference) {
              void window.api.shell.revealLocalPath(reference.path.replace(/[/\\]+$/, ''))
                .catch(() => { addToast(t('conversation.reference.openFailed'), 'warning') })
            }
            return
          }
          if (!canOpen || !activeThreadId) return
          if (onOpen) return onOpen(activeThreadId)
          void openConversationLink({
            target: targetPath,
            workspacePath,
            threadId: activeThreadId,
            t
          })
        }}
        style={{ cursor: canOpen ? 'pointer' : 'default' }}
      >
        <FileTypeIcon path={displayPath} dir={folder} size={12} style={{ display: 'inline-block' }} />
        <span>{label ?? fileName}</span>
      </button>
      </ActionTooltip>
      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.reference.path}
          location={contextMenu.reference.location}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}
