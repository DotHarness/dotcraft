import { useMemo, useState, type JSX, type ReactNode } from 'react'
import { Cloud, Folder, FolderPlus, MessageCircle, Server } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { isDefaultChatWorkspacePathCandidate } from '../../../shared/defaultChatWorkspace'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import {
  FooterMenuButton,
  FooterMenuDivider,
  FooterMenuSearchField
} from './composerFooterPrimitives'

export function workspaceSlug(path: string): string {
  const trimmed = path.trim().replace(/[\\/]+$/, '')
  const leaf = trimmed.split(/[\\/]+/).filter(Boolean).pop() || 'worktree'
  const slug = leaf
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return slug || 'worktree'
}

export function projectIdentity(project: WorkspaceProjectSummary): string {
  return project.projectId?.trim() || normalizeWorkspaceProjectKey(project.path)
}

export function projectLabel(project: WorkspaceProjectSummary): string {
  return project.name || workspaceSlug(project.path)
}

export function projectIcon(project: WorkspaceProjectSummary): ReactNode {
  if (project.kind !== 'remote') {
    return <Folder size={14} strokeWidth={1.8} aria-hidden />
  }
  return project.remote?.source === 'servers'
    ? <Server size={14} strokeWidth={1.8} aria-hidden />
    : <Cloud size={14} strokeWidth={1.8} aria-hidden />
}

export interface WorkspaceProjectChoices {
  projects: WorkspaceProjectSummary[]
  selectedProjectId: string
  selectedProject: WorkspaceProjectSummary | undefined
  chat: WorkspaceProjectSummary | null
  foregroundIsChat: boolean
}

export function useWorkspaceProjectChoices(
  workspacePath: string,
  enabled = true
): WorkspaceProjectChoices {
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const chat = useWorkspaceProjectsStore((s) => s.chat)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const selectedProjectId = foregroundProjectId || normalizeWorkspaceProjectKey(workspacePath)
  const foregroundIsChat =
    isDefaultChatWorkspacePathCandidate(workspacePath) ||
    (chat != null && projectIdentity(chat) === selectedProjectId)

  const options = useMemo(() => {
    if (!enabled) return []
    // The Chat workspace is not a project, so its path never becomes a project row.
    if (foregroundIsChat) return projects
    if (projects.some((project) => projectIdentity(project) === selectedProjectId)) {
      return projects
    }
    return [
      {
        projectId: selectedProjectId,
        kind: 'local' as const,
        path: workspacePath,
        identityWorkspacePath: workspacePath,
        name: workspaceSlug(workspacePath),
        state: 'foreground' as const,
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinned: false
      },
      ...projects
    ].filter((project) => project.path.trim().length > 0)
  }, [enabled, foregroundIsChat, projects, selectedProjectId, workspacePath])

  return {
    projects: options,
    selectedProjectId,
    selectedProject: options.find((project) => projectIdentity(project) === selectedProjectId),
    chat,
    foregroundIsChat
  }
}

export function WorkspaceProjectMenuItems({
  choices,
  busy = false,
  addBusy = false,
  onSelect,
  onAddProject
}: {
  choices: WorkspaceProjectChoices
  busy?: boolean
  addBusy?: boolean
  onSelect: (project: WorkspaceProjectSummary) => void
  onAddProject: () => void
}): JSX.Element {
  const t = useT()
  const [query, setQuery] = useState('')
  const { chat, foregroundIsChat, projects, selectedProjectId } = choices

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    if (!needle) return projects
    return projects.filter((project) =>
      projectLabel(project).toLowerCase().includes(needle) ||
      project.path.toLowerCase().includes(needle)
    )
  }, [projects, query])

  return (
    <>
      <FooterMenuSearchField
        value={query}
        placeholder={t('workspaceFooter.searchProjects')}
        onChange={setQuery}
      />
      <div style={{ maxHeight: '220px', overflowY: 'auto', padding: '4px 0' }}>
        {filtered.length === 0 ? (
          <div style={{ padding: '8px', color: 'var(--text-dimmed)' }}>
            {t('workspaceFooter.noProjects')}
          </div>
        ) : filtered.map((project) => {
          const checked = projectIdentity(project) === selectedProjectId
          return (
            <FooterMenuButton
              key={projectIdentity(project)}
              icon={projectIcon(project)}
              checked={checked}
              disabled={busy || (project.kind === 'remote' && !checked)}
              onClick={() => onSelect(project)}
            >
              <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {projectLabel(project)}
              </span>
            </FooterMenuButton>
          )
        })}
      </div>
      <FooterMenuDivider />
      <FooterMenuButton
        icon={<FolderPlus size={15} strokeWidth={1.8} aria-hidden />}
        disabled={addBusy}
        onClick={onAddProject}
      >
        <span style={{ flex: 1 }}>{t('addProject.addNew')}</span>
      </FooterMenuButton>
      {chat != null && !foregroundIsChat && (
        <FooterMenuButton
          icon={<MessageCircle size={15} strokeWidth={1.8} aria-hidden />}
          disabled={busy}
          onClick={() => onSelect(chat)}
        >
          <span style={{ flex: 1 }}>{t('workspaceFooter.leaveProject')}</span>
        </FooterMenuButton>
      )}
    </>
  )
}
