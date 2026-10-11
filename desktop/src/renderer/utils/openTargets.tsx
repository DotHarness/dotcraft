import { useEffect, type JSX } from 'react'
import { FolderOpen } from 'lucide-react'
import {
  resolveEffectiveOpenTarget,
  type OpenTargetInfo,
  type OpenTargetLocation
} from '../../shared/openTargets'
import { EditorGenericIcon, ExplorerIcon, TerminalBashIcon } from '../components/ui/AppIcons'
import type { ContextMenuEntry } from '../components/ui/ContextMenu'
import { useOpenTargetsStore, type OpenTargetsStatus } from '../stores/openTargetsStore'
import { addToast } from '../stores/toastStore'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export const OPEN_TARGET_ICON_SIZE = 16

export function openTargetLabel(target: OpenTargetInfo, t: Translate): string {
  return target.labelKey ? t(target.labelKey) : target.label
}

export function renderOpenTargetIcon(target: OpenTargetInfo, size = OPEN_TARGET_ICON_SIZE): JSX.Element {
  if (target.iconDataUrl) {
    return (
      <img
        src={target.iconDataUrl}
        alt=""
        width={size}
        height={size}
        style={{ display: 'block', objectFit: 'contain', borderRadius: 2 }}
        draggable={false}
      />
    )
  }
  if (target.iconKey === 'fileManager') return <ExplorerIcon size={size} />
  if (target.iconKey === 'terminal') return <TerminalBashIcon size={size} />
  return <EditorGenericIcon size={size} />
}

export async function openPathInTarget(
  targetId: string,
  path: string,
  t: Translate,
  location?: OpenTargetLocation | null
): Promise<void> {
  try {
    await window.api.shell.openInTarget({
      targetId,
      path,
      ...(location ? { line: location.line, column: location.column } : {})
    })
  } catch {
    addToast(t('conversation.reference.openFailed'), 'warning')
  }
}

export interface OpenTargetsView {
  status: OpenTargetsStatus
  targets: OpenTargetInfo[]
  effective: OpenTargetInfo | undefined
  refresh: () => Promise<void>
}

export function useOpenTargets(projectRoot: string | null | undefined, enabled = true): OpenTargetsView {
  const status = useOpenTargetsStore((s) => s.status)
  const allTargets = useOpenTargetsStore((s) => s.targets)
  const preferences = useOpenTargetsStore((s) => s.preferences)
  const refresh = useOpenTargetsStore((s) => s.refresh)

  useEffect(() => {
    if (enabled) void refresh()
  }, [enabled, refresh])

  return {
    status,
    targets: allTargets.filter((target) => !target.hidden),
    effective: resolveEffectiveOpenTarget(allTargets, preferences, projectRoot),
    refresh
  }
}

export function openTargetStatusLabel(status: OpenTargetsStatus, t: Translate): string | null {
  if (status === 'ready') return null
  return status === 'error' ? t('openTargets.loadFailed') : t('openTargets.loading')
}

export function buildOpenInSubmenu({
  view,
  projectRoot,
  t,
  onPicked
}: {
  view: OpenTargetsView
  projectRoot: string
  t: Translate
  onPicked?: () => void
}): ContextMenuEntry {
  const statusLabel = openTargetStatusLabel(view.status, t)
  const rows: ContextMenuEntry[] = statusLabel
    ? [{ label: statusLabel, disabled: true, onClick: () => {} }]
    : view.targets.map((target): ContextMenuEntry => ({
        label: openTargetLabel(target, t),
        icon: renderOpenTargetIcon(target),
        onClick: () => {
          onPicked?.()
          useOpenTargetsStore.getState().remember(target.id, projectRoot)
          void openPathInTarget(target.id, projectRoot, t)
        }
      }))
  return {
    label: t('openTargets.openIn'),
    icon: <FolderOpen size={14} aria-hidden />,
    onClick: () => {},
    submenu: rows
  }
}
