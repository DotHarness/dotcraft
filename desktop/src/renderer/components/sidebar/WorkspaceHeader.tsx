import { useMemo } from 'react'
import { stripWorkspaceLockedIpcPrefix } from '../../../shared/workspaceSwitchErrors'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import type { ContextMenuEntry } from '../ui/ContextMenu'
import { SectionOptionsMenu } from './SidebarSectionParts'
import { localProjectsByLastOpened, useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'

/** Extracts a clean user-facing message from a workspace switch error. */
function switchErrorMessage(err: unknown): string {
  const raw = err instanceof Error ? err.message : String(err)
  // Strip the Electron IPC prefix "Error invoking remote method '...': Error: ..."
  const match = raw.match(/Error invoking remote method '[^']+': Error: (.+)/)
  const inner = match ? match[1] : raw
  return stripWorkspaceLockedIpcPrefix(inner)
}

interface WorkspaceHeaderProps {
  workspaceName: string
  workspacePath: string
}

export function WorkspaceHeader({
  workspaceName,
  workspacePath
}: WorkspaceHeaderProps): JSX.Element {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        padding: '5px 8px 5px 14px',
        flexShrink: 0,
        minHeight: '32px'
      }}
    >
      <ActionTooltip label={workspacePath} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flex: 1 }}>
        <span
          style={{
            flex: 1,
            fontSize: 'var(--type-secondary-size)',
            lineHeight: 'var(--type-secondary-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)',
            color: 'var(--text-secondary)',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            display: 'block'
          }}
        >
          {workspaceName || 'DotCraft'}
        </span>
      </ActionTooltip>
    </div>
  )
}

interface WorkspaceOptionsMenuProps {
  workspacePath: string
  localWorkspacePath?: string
  localActionsDisabled?: boolean
  leadingItems?: ContextMenuEntry[]
  onOpenChange?: (open: boolean) => void
}

export function WorkspaceOptionsMenu({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled = false,
  leadingItems = [],
  onOpenChange
}: WorkspaceOptionsMenuProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const recents = useMemo(
    () => localProjectsByLastOpened(projects).filter((project) => !sameWorkspaceProjectKey(project.path, workspacePath)),
    [projects, workspacePath]
  )
  const hasWorkspace = workspacePath.trim().length > 0

  function openInExplorer(): void {
    if (localActionsDisabled) return
    void window.api.shell.openPath(localWorkspacePath || workspacePath)
  }

  async function switchWorkspace(): Promise<void> {
    try {
      await window.api.workspace.clearSelection()
    } catch (err) {
      window.alert(switchErrorMessage(err))
    }
  }

  async function switchToRecent(path: string): Promise<void> {
    try {
      await window.api.workspace.switch(path)
    } catch (err) {
      window.alert(switchErrorMessage(err))
    }
  }

  async function clearProjects(): Promise<void> {
    const confirmed = await confirm({
      title: t('workspaceHeader.clearRecentConfirmTitle'),
      message: t('workspaceHeader.clearRecentConfirmMessage'),
      confirmLabel: t('workspaceHeader.clearRecentConfirmAction'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    try {
      await window.api.workspace.clearProjects()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : String(err))
    }
  }

  const workspaceItems: ContextMenuEntry[] = hasWorkspace
    ? [
        { type: 'label', label: workspacePath },
        {
          label: t('workspaceHeader.openInExplorer'),
          disabled: localActionsDisabled,
          onClick: openInExplorer
        },
        { label: t('workspaceHeader.switchWorkspace'), onClick: () => { void switchWorkspace() } },
        {
          label: t('workspaceHeader.recentWorkspaces'),
          disabled: recents.length === 0,
          onClick: () => {},
          submenu: recents.length > 0
            ? [
                ...recents.map((recent) => ({
                  label: recent.name,
                  title: recent.path,
                  onClick: () => { void switchToRecent(recent.path) }
                })),
                { type: 'separator' },
                {
                  label: t('workspaceHeader.clearRecentWorkspaces'),
                  onClick: () => { void clearProjects() }
                }
              ]
            : undefined
        }
      ]
    : []
  const items: ContextMenuEntry[] = leadingItems.length > 0 && workspaceItems.length > 0
    ? [...leadingItems, { type: 'separator' }, ...workspaceItems]
    : [...leadingItems, ...workspaceItems]

  return (
    <SectionOptionsMenu
      label={t('workspaceHeader.optionsAria')}
      items={items}
      onOpenChange={onOpenChange}
    />
  )
}
