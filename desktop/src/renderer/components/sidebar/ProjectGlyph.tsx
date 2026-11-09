import { CircleDashed, Cloud, Folder, FolderOpen, Monitor, Server } from 'lucide-react'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { isRemoteProject } from './projectThreads'
import styles from './ProjectGlyph.module.css'

export function ProjectGlyph({
  project,
  collapsed,
  cold
}: {
  project: WorkspaceProjectSummary
  collapsed: boolean
  cold: boolean
}): JSX.Element {
  const ProjectIcon = isRemoteProject(project)
    ? (project.remote?.source === 'device'
      ? Monitor
      : project.remote?.source === 'servers' || project.remote?.source === 'ssh' ? Server : Cloud)
    : (collapsed ? Folder : FolderOpen)
  const showStatus = project.state === 'connecting' || project.state === 'error'

  return (
    <span className={styles.glyph} data-cold={cold || undefined}>
      <ProjectIcon size={15} strokeWidth={1.7} aria-hidden />
      {cold ? (
        <CircleDashed size={9} strokeWidth={2.35} aria-hidden className={styles.coldBadge} />
      ) : showStatus ? (
        <span aria-hidden className={styles.statusBadge} data-state={project.state} />
      ) : null}
    </span>
  )
}
