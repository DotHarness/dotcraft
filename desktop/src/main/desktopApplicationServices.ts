import type { PairedComputersManager } from './pairedComputers/pairedComputersManager'
import type { DesktopOwnedThreads } from './threadTasks/ownedThreads'
import type { BrowserWindow } from 'electron'
import type { AppSettings } from './settings'
import type { DesktopLifecycleController } from './desktopLifecycle'
import type { LocalProjectList } from './localProjects'
import type { ProjectCreationConfirmManager } from './projectCreationConfirm'
import type { DesktopHubClient } from './desktopHub'
import type { DesktopThreadTasks } from './threadTasks/threadTaskService'
import type { WindowAttentionNotifier } from './windowAttentionNotifier'
import type { AppUpdateService } from './appUpdate'
import type { WhatsNewCatalog } from './whatsNewCatalog'
import type { WhatsNewMediaCache } from './whatsNewMediaCache'
import type { DesktopWindowRuntime } from './windows/windowRuntime'

export interface DesktopApplicationServices {
  settings: AppSettings
  lifecycle: DesktopLifecycleController
  localProjects: LocalProjectList
  projectCreationConfirm: ProjectCreationConfirmManager
  tasks: DesktopThreadTasks
  ownedThreads: DesktopOwnedThreads
  readonly pairedComputers: PairedComputersManager
  windows: Map<number, DesktopWindowRuntime>
  notifyWindowAttention: WindowAttentionNotifier
  createHubClient(settings: AppSettings): DesktopHubClient
  updateSharedSettings(partial: Partial<AppSettings>, owner?: DesktopWindowRuntime): Promise<void>
  persistWindowSettings(settings: AppSettings): void
  broadcastWorkspaceProjects(): void
  restoreRecentProjects(window: DesktopWindowRuntime): Promise<void>
  refreshAppMenu(): void
  hasTray(): boolean
  openNewWindow(): void
  openDesktopWindow(workspacePath?: string | null, threadId?: string | null): Promise<void>
  focusedWindow(): DesktopWindowRuntime | undefined
  windowFor(owner: BrowserWindow): DesktopWindowRuntime | undefined
  getAppUpdateService(): AppUpdateService
  getWhatsNewMediaCache(): WhatsNewMediaCache
  getWhatsNewCatalog(): WhatsNewCatalog
  broadcastPinnedThreadIdsChanged(payload: { workspacePath: string; threadIds: string[] }): void
}
