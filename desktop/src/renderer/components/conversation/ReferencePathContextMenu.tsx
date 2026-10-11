import { SYSTEM_DEFAULT_TARGET_ID, type OpenTargetLocation } from '../../../shared/openTargets'
import { Copy, ExternalLink, FolderOpen, MessageSquarePlus } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useComposerFileReferenceStore } from '../../stores/composerFileReferenceStore'
import { useConversationStore } from '../../stores/conversationStore'
import {
  openPathInTarget,
  openTargetLabel,
  openTargetStatusLabel,
  renderOpenTargetIcon,
  useOpenTargets
} from '../../utils/openTargets'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'

interface ReferencePathContextMenuProps {
  position: ContextMenuPosition
  targetPath: string
  location?: OpenTargetLocation | null
  allowAddToChat?: boolean
  onClose: () => void
}

const menuWidth = 248

export function ReferencePathContextMenu({
  position,
  targetPath,
  location = null,
  allowAddToChat = true,
  onClose
}: ReferencePathContextMenuProps): JSX.Element {
  const t = useT()
  const projectRoot = useConversationStore((s) => s.workspacePath)
  const view = useOpenTargets(projectRoot)
  const statusLabel = openTargetStatusLabel(view.status, t)
  const effective = view.effective
  const otherTargets = view.targets.filter((target) => target.id !== effective?.id)

  async function revealInExplorer(): Promise<void> {
    try {
      await window.api.shell.revealLocalPath(targetPath)
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  async function copyPath(): Promise<void> {
    try {
      await navigator.clipboard.writeText(targetPath)
      addToast(t('toast.copied'), 'success')
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  function addToChat(): void {
    useComposerFileReferenceStore.getState().request(targetPath)
  }

  const statusRow: ContextMenuEntry | null = statusLabel
    ? { label: statusLabel, disabled: true, onClick: () => {} }
    : null
  const openEntries: ContextMenuEntry[] = [
    statusRow ?? (effective
      ? {
          label: t('threadHeader.openIn', { app: openTargetLabel(effective, t) }),
          icon: renderOpenTargetIcon(effective),
          onClick: () => { void openPathInTarget(effective.id, targetPath, t, location) }
        }
      : { label: t('openTargets.loading'), disabled: true, onClick: () => {} }),
    {
      label: t('conversation.reference.openWith'),
      icon: <ExternalLink size={16} />,
      onClick: () => {},
      submenu: statusRow ? [statusRow] : [
        ...otherTargets.map((target): ContextMenuEntry => ({
          label: openTargetLabel(target, t),
          icon: renderOpenTargetIcon(target),
          onClick: () => { void openPathInTarget(target.id, targetPath, t, location) }
        })),
        ...(otherTargets.length > 0 ? [{ type: 'separator' } as const] : []),
        {
          label: t('conversation.reference.defaultApp'),
          icon: <ExternalLink size={16} />,
          onClick: () => { void openPathInTarget(SYSTEM_DEFAULT_TARGET_ID, targetPath, t) }
        }
      ]
    }
  ]

  const items: ContextMenuEntry[] = [
    ...openEntries,
    { type: 'separator' },
    {
      label: t('conversation.reference.copyPath'),
      icon: <Copy size={16} />,
      onClick: () => { void copyPath() }
    },
    ...(allowAddToChat
      ? [{
          label: t('conversation.reference.addToChat'),
          icon: <MessageSquarePlus size={16} />,
          onClick: addToChat
        }]
      : []),
    {
      label: t('conversation.reference.openInExplorer'),
      icon: <FolderOpen size={16} />,
      onClick: () => { void revealInExplorer() }
    }
  ]

  return <ContextMenu items={items} position={position} onClose={onClose} width={menuWidth} />
}
