import { useEffect, useMemo, useState } from 'react'
import { Copy, ExternalLink, FolderOpen, MessageSquarePlus } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useComposerFileReferenceStore } from '../../stores/composerFileReferenceStore'
import {
  EDITOR_ICON_SIZE,
  listEditorsCached,
  placeExplorerFirst,
  renderEditorIcon,
  type EditorId,
  type EditorInfo
} from '../../utils/editorTargets'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'

interface ReferencePathContextMenuProps {
  position: ContextMenuPosition
  targetPath: string
  allowAddToChat?: boolean
  onClose: () => void
}

const menuWidth = 248

export function ReferencePathContextMenu({
  position,
  targetPath,
  allowAddToChat = true,
  onClose
}: ReferencePathContextMenuProps): JSX.Element {
  const t = useT()
  const [editors, setEditors] = useState<EditorInfo[]>([])
  const [lastOpenEditorId, setLastOpenEditorId] = useState<EditorId | undefined>(undefined)

  const orderedEditors = useMemo(
    () => placeExplorerFirst(editors),
    [editors]
  )
  const resolvedLastOpenId = useMemo<EditorId>(() => {
    if (lastOpenEditorId && orderedEditors.some((entry) => entry.id === lastOpenEditorId)) {
      return lastOpenEditorId
    }
    return 'explorer'
  }, [lastOpenEditorId, orderedEditors])
  const primaryEditor = useMemo(() => {
    return orderedEditors.find((entry) => entry.id === resolvedLastOpenId)
      ?? orderedEditors[0]
      ?? { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
  }, [orderedEditors, resolvedLastOpenId])
  const primaryAppLabel = t(primaryEditor.labelKey)

  useEffect(() => {
    let cancelled = false
    window.api.settings.get()
      .then((settings) => {
        if (!cancelled) setLastOpenEditorId(settings.lastOpenEditorId)
      })
      .catch(() => {})
    void listEditorsCached()
      .then((entries) => {
        if (!cancelled) setEditors(entries)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])

  async function launchWithEditor(editor: EditorInfo): Promise<void> {
    try {
      if (editor.id === 'explorer') {
        await window.api.shell.revealLocalPath(targetPath)
      } else {
        await window.api.shell.launchLocalPathInEditor(editor.id, targetPath)
      }
      await window.api.settings.set({ lastOpenEditorId: editor.id })
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  async function openDefaultApp(): Promise<void> {
    try {
      await window.api.shell.openLocalPath(targetPath)
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

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

  const items: ContextMenuEntry[] = [
    {
      label: t('threadHeader.openIn', { app: primaryAppLabel }),
      icon: renderEditorIcon(primaryEditor, EDITOR_ICON_SIZE),
      onClick: () => { void launchWithEditor(primaryEditor) }
    },
    {
      label: t('conversation.reference.openWith'),
      icon: <ExternalLink size={16} />,
      onClick: () => {},
      submenu: [
        ...orderedEditors.map((editor): ContextMenuEntry => ({
          label: t(editor.labelKey),
          icon: renderEditorIcon(editor, EDITOR_ICON_SIZE),
          onClick: () => { void launchWithEditor(editor) }
        })),
        ...(orderedEditors.length > 0 ? [{ type: 'separator' } as const] : []),
        {
          label: t('conversation.reference.defaultApp'),
          icon: <ExternalLink size={16} />,
          onClick: () => { void openDefaultApp() }
        }
      ]
    },
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
