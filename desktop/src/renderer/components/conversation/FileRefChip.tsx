import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { resolveLocalReferencePath } from '../../utils/referencePaths'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { ReferencePathContextMenu } from './ReferencePathContextMenu'

export function FileRefChip({
  displayPath,
  targetPath,
  label,
  className,
  workspacePath,
  activeThreadId,
  remoteWorkspaceActive
}: {
  displayPath: string
  targetPath: string
  label?: string
  className?: string
  workspacePath: string
  activeThreadId: string | null
  remoteWorkspaceActive: boolean
}): JSX.Element {
  const t = useT()
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string } | null>(null)
  const fileName = displayPath.split(/[/\\]/).pop() ?? displayPath
  const resolvedTargetPath = resolveLocalReferencePath(targetPath, workspacePath)
  const title = resolvedTargetPath ?? targetPath
  const canOpen = !remoteWorkspaceActive && workspacePath.length > 0 && !!activeThreadId

  return (
    <>
      <ActionTooltip label={title}>
      <button
        type="button"
        aria-label={t('conversation.openFileRefAria', { file: fileName })}
        disabled={!canOpen}
        className={className ? `dc-ref dc-ref-file ${className}` : 'dc-ref dc-ref-file'}
        onContextMenu={(event) => {
          if (!resolvedTargetPath) return
          event.preventDefault()
          event.stopPropagation()
          setContextMenu({
            position: { x: event.clientX, y: event.clientY },
            targetPath: resolvedTargetPath
          })
        }}
        onClick={() => {
          if (!canOpen || !activeThreadId) return
          void openConversationLink({
            target: targetPath,
            workspacePath,
            threadId: activeThreadId,
            t
          })
        }}
        style={{ cursor: canOpen ? 'pointer' : 'default' }}
      >
        <FileTypeIcon path={displayPath} size={12} style={{ display: 'inline-block' }} />
        <span>{label ?? fileName}</span>
      </button>
      </ActionTooltip>
      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}
