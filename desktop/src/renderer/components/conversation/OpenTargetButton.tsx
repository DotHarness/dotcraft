import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { SplitButton, type SplitButtonItem } from '../ui/SplitButton'
import type { ButtonVariant } from '../ui/Button'
import {
  EDITOR_ICON_SIZE,
  listEditorsCached,
  placeExplorerFirst,
  renderEditorIcon,
  type EditorId,
  type EditorInfo
} from '../../utils/editorTargets'

interface OpenTargetButtonProps {
  targetPath: string
  tooltipLabel: string
  menuAriaLabel: string
  showPrimaryLabel?: boolean
  primaryButtonLabel?: string
  primaryIcon?: ReactNode
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
  variant?: ButtonVariant
}

/**
 * Opens a path in the last-used editor, with a menu for choosing a different one.
 *
 * The menu picks the default rather than running a command, so its items mark the
 * current choice instead of acting immediately — clicking one persists the choice
 * and the principal segment launches it. Chrome, keyboard behaviour, and geometry
 * come from the shared compound trigger.
 */
export function OpenTargetButton({
  targetPath,
  tooltipLabel,
  menuAriaLabel,
  showPrimaryLabel = false,
  primaryButtonLabel,
  primaryIcon,
  tooltipPlacement = 'bottom',
  variant = 'secondary'
}: OpenTargetButtonProps): JSX.Element {
  const t = useT()
  const [loading, setLoading] = useState(false)
  const [editors, setEditors] = useState<EditorInfo[]>([])
  const [lastOpenEditorId, setLastOpenEditorId] = useState<EditorId | undefined>(undefined)

  const orderedEditors = useMemo(() => placeExplorerFirst(editors), [editors])
  const resolvedLastOpenId = useMemo<EditorId>(() => {
    if (lastOpenEditorId && orderedEditors.some((entry) => entry.id === lastOpenEditorId)) {
      return lastOpenEditorId
    }
    return 'explorer'
  }, [lastOpenEditorId, orderedEditors])

  const primaryEditor = useMemo(() => {
    const preferred = orderedEditors.find((entry) => entry.id === resolvedLastOpenId)
    if (preferred) return preferred
    return orderedEditors[0] ?? { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
  }, [orderedEditors, resolvedLastOpenId])

  const primaryAriaLabel = useMemo(() => {
    if (primaryEditor.id === 'explorer') return primaryButtonLabel ?? t('threadHeader.open')
    return t('threadHeader.openIn', { app: t(primaryEditor.labelKey) })
  }, [primaryButtonLabel, primaryEditor, t])

  useEffect(() => {
    window.api.settings.get()
      .then((settings) => {
        setLastOpenEditorId(settings.lastOpenEditorId)
      })
      .catch(() => {})
    setLoading(true)
    void listEditorsCached()
      .then((entries) => {
        setEditors(entries)
      })
      .catch(() => {})
      .finally(() => {
        setLoading(false)
      })
  }, [])

  async function handleSwitchDefault(id: EditorId): Promise<void> {
    setLastOpenEditorId(id)
    try {
      await window.api.settings.set({ lastOpenEditorId: id })
    } catch {
      // Keep silent to avoid interrupting regular conversation flow.
    }
  }

  async function handleLaunch(id: EditorId): Promise<void> {
    try {
      await window.api.shell.launchEditor(id, targetPath)
    } catch {
      // Keep silent to avoid interrupting regular conversation flow.
    }
  }

  const items: SplitButtonItem[] = orderedEditors.map((entry) => ({
    key: entry.id,
    label: t(entry.labelKey),
    icon: renderEditorIcon(entry, EDITOR_ICON_SIZE),
    selected: entry.id === resolvedLastOpenId,
    onClick: () => { void handleSwitchDefault(entry.id) }
  }))

  return (
    <SplitButton
      variant={variant}
      label={showPrimaryLabel ? (primaryButtonLabel ?? t('threadHeader.open')) : undefined}
      ariaLabel={primaryAriaLabel}
      icon={primaryIcon ?? renderEditorIcon(primaryEditor, EDITOR_ICON_SIZE)}
      onClick={() => { void handleLaunch(primaryEditor.id) }}
      items={items}
      menuLabel={menuAriaLabel}
      tooltip={tooltipLabel}
      disabledReason={loading ? t('quickOpen.loading') : undefined}
      tooltipPlacement={tooltipPlacement}
      disabled={loading}
    />
  )
}
